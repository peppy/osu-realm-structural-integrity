// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Statistics;
using Realms;

namespace osu.Game.Database
{
    public class RealmContextFactory : Component, IRealmFactory
    {
        private readonly Storage storage;

        public readonly string Filename;

        private const int schema_version = 6;

        /// <summary>
        /// Lock object which is held for the duration of a write operation (via <see cref="GetForWrite"/>).
        /// </summary>
        private readonly object writeLock = new object();

        /// <summary>
        /// Lock object which is held during <see cref="BlockAllOperations"/> sections.
        /// </summary>
        private readonly SemaphoreSlim blockingLock = new SemaphoreSlim(1);

        private static readonly GlobalStatistic<int> reads = GlobalStatistics.Get<int>("Realm", "Get (Read)");
        private static readonly GlobalStatistic<int> writes = GlobalStatistics.Get<int>("Realm", "Get (Write)");
        private static readonly GlobalStatistic<int> refreshes = GlobalStatistics.Get<int>("Realm", "Dirty Refreshes");
        private static readonly GlobalStatistic<int> contexts_created = GlobalStatistics.Get<int>("Realm", "Contexts (Created)");
        private static readonly GlobalStatistic<int> pending_writes = GlobalStatistics.Get<int>("Realm", "Pending writes");
        private static readonly GlobalStatistic<int> active_usages = GlobalStatistics.Get<int>("Realm", "Active usages");

        private readonly object updateContextLock = new object();

        private Realm? context;

        public Realm Context
        {
            get
            {
                // if (!ThreadSafety.IsUpdateThread)
                //     throw new InvalidOperationException($"Use {nameof(GetForRead)} or {nameof(GetForWrite)} when performing realm operations from a non-update thread");

                lock (updateContextLock)
                {
                    if (context == null)
                    {
                        context = CreateContext();
                        Logger.Log($"Opened realm \"{context.Config.DatabasePath}\" at version {context.Config.SchemaVersion}");
                    }

                    // creating a context will ensure our schema is up-to-date and migrated.

                    return context;
                }
            }
        }

        public RealmContextFactory(Storage storage, string filename)
        {
            this.storage = storage;

            Filename = filename;

            const string realm_extension = ".realm";

            if (!Filename.EndsWith(realm_extension, StringComparison.Ordinal))
                Filename += realm_extension;
        }

        public RealmUsage GetForRead()
        {
            reads.Value++;
            return new RealmUsage(CreateContext());
        }

        public RealmWriteUsage GetForWrite()
        {
            writes.Value++;
            pending_writes.Value++;

            Monitor.Enter(writeLock);
            return new RealmWriteUsage(CreateContext(), writeComplete);
        }

        /// <summary>
        /// Flush any active contexts and block any further writes.
        /// </summary>
        /// <remarks>
        /// This should be used in places we need to ensure no ongoing reads/writes are occurring with realm.
        /// ie. to move the realm backing file to a new location.
        /// </remarks>
        /// <returns>An <see cref="IDisposable"/> which should be disposed to end the blocking section.</returns>
        public IDisposable BlockAllOperations()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(RealmContextFactory));

            Logger.Log(@"Blocking realm operations.", LoggingTarget.Database);

            blockingLock.Wait();
            flushContexts();

            return new InvokeOnDisposal<RealmContextFactory>(this, endBlockingSection);

            static void endBlockingSection(RealmContextFactory factory)
            {
                factory.blockingLock.Release();
                Logger.Log(@"Restoring realm operations.", LoggingTarget.Database);
            }
        }

        protected override void Update()
        {
            base.Update();

            lock (updateContextLock)
            {
                if (context?.Refresh() == true)
                    refreshes.Value++;
            }
        }

        public Realm CreateContext()
        {
            try
            {
                if (IsDisposed)
                    throw new ObjectDisposedException(nameof(RealmContextFactory));

                blockingLock.Wait();

                contexts_created.Value++;

                return Realm.GetInstance(getConfiguration());
            }
            finally
            {
                blockingLock.Release();
            }
        }

        public void Compact() => Realm.Compact(getConfiguration());

        private RealmConfiguration getConfiguration()
        {
            return new RealmConfiguration(storage.GetFullPath(Filename, true))
            {
                SchemaVersion = schema_version,
                MigrationCallback = onMigration,
            };
        }

        private void writeComplete()
        {
            Monitor.Exit(writeLock);
            pending_writes.Value--;
        }

        private void onMigration(Migration migration, ulong lastSchemaVersion)
        {
        }

        private void flushContexts()
        {
            Logger.Log(@"Flushing realm contexts...", LoggingTarget.Database);
            Debug.Assert(blockingLock.CurrentCount == 0);

            Realm? previousContext;

            lock (updateContextLock)
            {
                previousContext = context;
                context = null;
            }

            // wait for all threaded usages to finish
            while (active_usages.Value > 0)
                Thread.Sleep(50);

            previousContext?.Dispose();

            Logger.Log(@"Realm contexts flushed.", LoggingTarget.Database);
        }

        protected override void Dispose(bool isDisposing)
        {
            context?.Dispose();

            if (!IsDisposed)
            {
                // intentionally block all operations indefinitely. this ensures that nothing can start consuming a new context after disposal.
                BlockAllOperations();
                blockingLock.Dispose();
            }

            base.Dispose(isDisposing);
        }

        public void ResetDatabase()
        {
            using (BlockAllOperations())
            {
                try
                {
                    storage.DeleteDatabase(Filename);
                }
                catch
                {
                    // for now we are not sure why file handles are kept open by EF, but this is generally only used in testing
                }
            }
        }

        /// <summary>
        /// A usage of realm from an arbitrary thread.
        /// </summary>
        public class RealmUsage : IDisposable
        {
            public readonly Realm Realm;

            internal RealmUsage(Realm context)
            {
                active_usages.Value++;
                Realm = context;
            }

            /// <summary>
            /// Disposes this instance, calling the initially captured action.
            /// </summary>
            public virtual void Dispose()
            {
                Realm.Dispose();
                active_usages.Value--;
            }
        }

        /// <summary>
        /// A transaction used for making changes to realm data.
        /// </summary>
        public class RealmWriteUsage : RealmUsage
        {
            private readonly Action onWriteComplete;
            private readonly Transaction transaction;

            internal RealmWriteUsage(Realm context, Action onWriteComplete)
                : base(context)
            {
                this.onWriteComplete = onWriteComplete;
                transaction = Realm.BeginWrite();
            }

            /// <summary>
            /// Commit all changes made in this transaction.
            /// </summary>
            public void Commit() => transaction.Commit();

            /// <summary>
            /// Revert all changes made in this transaction.
            /// </summary>
            public void Rollback() => transaction.Rollback();

            /// <summary>
            /// Disposes this instance, calling the initially captured action.
            /// </summary>
            public override void Dispose()
            {
                // rollback if not explicitly committed.
                transaction.Dispose();

                base.Dispose();

                onWriteComplete();
            }
        }
    }
}
