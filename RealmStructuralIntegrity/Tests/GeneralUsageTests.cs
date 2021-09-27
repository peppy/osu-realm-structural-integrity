using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Database;
using osu.Game.Models;
using Realms;
using Xunit;
using Xunit.Abstractions;

namespace osu.Game.Tests
{
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public class GeneralUsageTests : TestBase
    {
        private const int beatmap_set_import_count = 1000;

        public GeneralUsageTests(ITestOutputHelper logger)
            : base(logger)
        {
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestConstructRealm()
        {
            RunTestWithRealm(realmFactory => { realmFactory.Context.Refresh(); });
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestImportSingleBeatmap()
        {
            RunTestWithRealm(realmFactory =>
            {
                var realm = realmFactory.Context;

                realm.Write(() =>
                {
                    var ruleset = CreateRuleset();
                    realm.Add(ruleset, true);

                    var beatmapSet = CreateBeatmapSet(ruleset);

                    realm.Add(beatmapSet);
                });

                foreach (var file in realm.All<RealmFile>())
                    Assert.Equal(1, file.ReferenceCount);
            });
        }

        [Fact]
        public void TestImportManyBeatmapsSingleTransaction()
        {
            RunTestWithRealm(realmFactory =>
            {
                var realm = realmFactory.Context;

                realm.Write(() =>
                {
                    var ruleset = CreateRuleset();
                    realm.Add(ruleset, true);

                    for (int i = 0; i < beatmap_set_import_count; i++)
                    {
                        var beatmapSet = CreateBeatmapSet(ruleset);
                        realm.Add(beatmapSet);
                    }
                });

                foreach (var file in realm.All<RealmFile>())
                    Assert.Equal(1, file.ReferenceCount);
            });
        }

        [Fact]
        public void TestImportManyBeatmapsIndividualTransactions()
        {
            RunTestWithRealm(realmFactory =>
            {
                var ruleset = CreateRuleset();

                var realm = realmFactory.Context;

                realm.Write(() => realm.Add(ruleset, true));

                var threadRef = ThreadSafeReference.Create(ruleset);

                var task = Task.Factory.StartNew(() =>
                {
                    using (var innerRealm = realmFactory.CreateContext())
                    {
                        ruleset = innerRealm.ResolveReference(threadRef);

                        for (int i = 0; i < beatmap_set_import_count; i++)
                            innerRealm.Write(() => innerRealm.Add(CreateBeatmapSet(ruleset)));
                    }
                });

                refreshUntilCompleted(realm, task);

                Logger.WriteLine($"inserted {realm.All<RealmBeatmapSet>().Count()} sets");

                foreach (var file in realm.All<RealmFile>())
                    Assert.Equal(1, file.ReferenceCount);
            });
        }

        /// <summary>
        /// Example of how a primary key can be stored and then used for a lookup on a different thread context.
        /// Of note, this will only work if Refresh is first called on the target context to pull in the changes.
        /// Because of this there's a chance it might not resolve (if the object was since deleted).
        /// </summary>
        [Fact]
        public void TestThreadedAccessViaPrimaryKey()
        {
            RunTestWithRealm(realmFactory =>
            {
                // retrieve context to bind main realm to this thread.
                var context = realmFactory.Context;

                RealmBeatmap? beatmap = null;

                Guid key = Guid.Empty;

                Task.Run(() =>
                {
                    // write to realm on an async thread.
                    using (var realm = realmFactory.CreateContext())
                    {
                        Assert.NotEqual(context, realm);

                        using (var transaction = realm.BeginWrite())
                        {
                            realm.Add(beatmap = new RealmBeatmap(CreateRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));
                            transaction.Commit();
                        }

                        // the key must be accessed before the local context is closed.
                        key = beatmap.ID;
                    }
                }).Wait();

                Debug.Assert(beatmap != null);

                // can check the managed state outside of original context.
                Assert.True(beatmap.IsManaged);
                Assert.False(beatmap.IsValid);

                // refresh is required to bring the local context up-to-date with the changes made off-thread.
                context.Refresh();

                // re-retrieve beatmap on main thread.
                beatmap = context.Find<RealmBeatmap>(key);

                Assert.Equal(key, beatmap.ID);
            });
        }

        /// <summary>
        /// Example of how <see cref="ThreadSafeReference"/> can be used to ferry data between threads.
        /// Of note:
        /// - References can be resolved even if the originating context has been closed.
        /// - This method does not require a Refresh() call on the target context, making it potentially beneficial compared to a key lookup.
        /// - ResolveReference may return null if the referenced object has been deleted.
        /// </summary>
        [Fact]
        public void TestThreadedAccessViaSafeReference()
        {
            RunTestWithRealm(realmFactory =>
            {
                // retrieve context to bind main realm to this thread.
                var context = realmFactory.Context;

                RealmBeatmap? beatmap = null;

                ThreadSafeReference.Object<RealmBeatmap>? threadSafeReference = null;
                Guid key = Guid.Empty;

                Task.Run(() =>
                {
                    // write to realm on an async thread.
                    using (var realm = realmFactory.CreateContext())
                    {
                        Assert.NotEqual(context, realm);

                        using (var transaction = realm.BeginWrite())
                        {
                            realm.Add(beatmap = new RealmBeatmap(CreateRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));
                            transaction.Commit();
                        }

                        threadSafeReference = ThreadSafeReference.Create(beatmap);

                        // the key must be accessed before the local context is closed.
                        key = beatmap.ID;
                    }
                }).Wait();

                Debug.Assert(beatmap != null);

                // can check the managed state outside of original context.
                Assert.True(beatmap.IsManaged);
                Assert.False(beatmap.IsValid);

                //context.Refresh();

                // re-retrieve beatmap on main thread.
                beatmap = context.ResolveReference(threadSafeReference);

                Assert.Equal(key, beatmap.ID);
            });
        }

        [Fact]
        public void TestThreadedAccessViaLive()
        {
            RunTestWithRealm(realmFactory =>
            {
                int thread1;

                var ruleset = CreateRuleset();

                var task = Task.Factory.StartNew(() =>
                {
                    thread1 = Thread.CurrentThread.ManagedThreadId;

                    using (var realm = realmFactory.CreateContext())
                    {
                        var beatmap = realm.Write(() => realm.Add(new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), new RealmBeatmapMetadata())));

                        var liveBeatmap = beatmap.ToLive();

                        Task.Factory.StartNew(() =>
                        {
                            Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                            liveBeatmap.PerformRead(b => { Logger.WriteLine(b.DifficultyName); });

                            liveBeatmap.PerformWrite(b => b.Hidden = true);

                            Assert.Throws<InvalidOperationException>(() => liveBeatmap.PerformRead(b => b.Difficulty));
                        }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();
                    }
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler);

                refreshUntilCompleted(realmFactory.Context, task);

                Assert.Equal(1, realmFactory.Context.All<RealmBeatmap>().Count(b => b.Hidden));
            });
        }

        [Fact]
        public void TestThreadedAccessWithoutSharedSynchronizationContext()
        {
            RunTestWithRealm(realmFactory =>
            {
                Realm? realm = null;
                int thread1;

                var ruleset = CreateRuleset();

                Task.Factory.StartNew(() =>
                {
                    thread1 = Thread.CurrentThread.ManagedThreadId;

                    realm = realmFactory.CreateContext();

                    realm.Write(() => realm.Add(new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), new RealmBeatmapMetadata())));
                    realm.Refresh();

                    Task.Factory.StartNew(() =>
                    {
                        Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                        // expected one of these to crash as this context was opened on another thread
                        Assert.Throws<Exception>(() => realm.Refresh());
                    }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();

                realm?.Dispose();
            });
        }

        [Fact]
        public void TestThreadedAccessViaSharedSynchronizationContext()
        {
            RunTestWithRealm(realmFactory =>
            {
                var syncContext = new LocalSyncContext();

                Realm? realm;
                int thread1;

                Task.Factory.StartNew(() =>
                {
                    thread1 = Thread.CurrentThread.ManagedThreadId;
                    SynchronizationContext.SetSynchronizationContext(syncContext);

                    realm = realmFactory.CreateContext();
                    realm.Write(() => realm.Add(CreateBeatmapSet(CreateRuleset())));
                    realm.Refresh();

                    Task.Factory.StartNew(() =>
                    {
                        Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread1);
                        SynchronizationContext.SetSynchronizationContext(syncContext);

                        realm.Refresh();
                    }, TaskCreationOptions.LongRunning).Wait();
                }, TaskCreationOptions.LongRunning).Wait();
            });
        }

        private void refreshUntilCompleted(Realm realm, Task task)
        {
            int refreshCount = 0;

            while (!task.IsCompleted)
            {
                realm.Refresh();
                refreshCount++;
            }

            Logger.WriteLine($"refreshed {refreshCount} times");
        }
    }
}
