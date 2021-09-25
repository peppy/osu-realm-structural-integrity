using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using osu.Framework.Extensions;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Models;
using Realms;
using Xunit;
using Xunit.Abstractions;

namespace osu.Game
{
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public class UsageTests : IDisposable
    {
        private readonly ITestOutputHelper output;

        private readonly TemporaryNativeStorage storage;
        private readonly RealmContextFactory realmFactory;

        private const int beatmap_import_count = 1000;

        public UsageTests(ITestOutputHelper output)
        {
            this.output = output;

            storage = new TemporaryNativeStorage("realm-test");
            realmFactory = new RealmContextFactory(storage);

            output.WriteLine($"Running tests at storage location {storage.GetFullPath(string.Empty)}");
        }

        public void Dispose()
        {
            realmFactory.Dispose();

            output.WriteLine($"Final database size: {storage.GetStream("client.realm")?.Length ?? 0}");

            realmFactory.Compact();

            output.WriteLine($"Final database size after compact: {storage.GetStream("client.realm")?.Length ?? 0}");

            storage.Dispose();
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestConstructRealm()
        {
            AsyncContext.Run(() => { realmFactory.Context.Refresh(); });
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestImportSingleBeatmap()
        {
            AsyncContext.Run(() =>
            {
                using (var usage = realmFactory.GetForWrite())
                {
                    var ruleset = createRuleset();
                    usage.Realm.Add(ruleset, true);

                    var beatmapSet = createBeatmapSet(ruleset);

                    usage.Realm.Add(beatmapSet);

                    usage.Commit();

                    foreach (var file in usage.Realm.All<RealmFile>())
                        Assert.Equal(1, file.ReferenceCount);
                }
            });
        }

        [Fact]
        public void TestImportManyBeatmapsSingleTransaction()
        {
            AsyncContext.Run(() =>
            {
                using (var usage = realmFactory.GetForWrite())
                {
                    var ruleset = createRuleset();
                    usage.Realm.Add(ruleset, true);

                    for (int i = 0; i < beatmap_import_count; i++)
                    {
                        var beatmapSet = createBeatmapSet(ruleset);
                        usage.Realm.Add(beatmapSet);
                    }

                    usage.Commit();

                    foreach (var file in usage.Realm.All<RealmFile>())
                        Assert.Equal(1, file.ReferenceCount);
                }
            });
        }

        [Fact]
        public void TestImportManyBeatmapsIndividualTransactions()
        {
            AsyncContext.Run(() =>
            {
                var ruleset = createRuleset();

                realmFactory.Context.Write(() => realmFactory.Context.Add(ruleset, true));

                for (int i = 0; i < beatmap_import_count; i++)
                {
                    using (var transaction = realmFactory.Context.BeginWrite())
                    {
                        realmFactory.Context.Add(createBeatmapSet(ruleset));
                        transaction.Commit();
                    }
                }

                foreach (var file in realmFactory.Context.All<RealmFile>())
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
            AsyncContext.Run(() =>
            {
                // retrieve context to bind main realm to this thread.
                var context = realmFactory.Context;

                RealmBeatmap? beatmap = null;

                Guid key = Guid.Empty;

                Task.Run(() =>
                {
                    // write to realm on an async thread.
                    using (var usage = realmFactory.GetForWrite())
                    {
                        Assert.NotEqual(context, usage.Realm);

                        usage.Realm.Add(beatmap = new RealmBeatmap(createRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));

                        usage.Commit();

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
            AsyncContext.Run(() =>
            {
                // retrieve context to bind main realm to this thread.
                var context = realmFactory.Context;

                RealmBeatmap? beatmap = null;

                ThreadSafeReference.Object<RealmBeatmap>? threadSafeReference = null;
                Guid key = Guid.Empty;

                Task.Run(() =>
                {
                    // write to realm on an async thread.
                    using (var usage = realmFactory.GetForWrite())
                    {
                        Assert.NotEqual(context, usage.Realm);

                        usage.Realm.Add(beatmap = new RealmBeatmap(createRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));
                        usage.Commit();

                        threadSafeReference = ThreadSafeReference.Create(beatmap);
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
        public void TestThreadedAccessWithoutSharedSynchronizationContext()
        {
            AsyncContext.Run(() =>
            {
                Realm? realm = null;
                int thread1 = -1;

                var ruleset = createRuleset();

                Task.Factory.StartNew(() =>
                {
                    thread1 = Thread.CurrentThread.ManagedThreadId;

                    realm = Realm.GetInstance(new RealmConfiguration(Path.GetTempFileName()));
                    realm.Write(() => realm.Add(new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), new RealmBeatmapMetadata())));
                    realm.Refresh();
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();

                Task.Factory.StartNew(() =>
                {
                    Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                    Debug.Assert(realm != null);

                    // expected one of these to crash as this context was opened on another thread?
                    realm.Refresh();
                    realm.Write(() => realm.Add(new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), new RealmBeatmapMetadata())));
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();
            });
        }

        [Fact]
        public void TestThreadedAccessViaSharedSynchronizationContext()
        {
            RealmBeatmap? beatmap = null;

            var syncContext = new SynchronizationContext();

            Task.Factory.StartNew(() =>
            {
                using (var usage = realmFactory.GetForWrite())
                {
                    usage.Realm.Add(beatmap = new RealmBeatmap(createRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));
                    usage.Commit();
                }
            }, TaskCreationOptions.LongRunning).Wait();

            int thread1 = -1;
            Task.Factory.StartNew(() =>
            {
                thread1 = Thread.CurrentThread.ManagedThreadId;
                SynchronizationContext.SetSynchronizationContext(syncContext);
                beatmap = realmFactory.Context.All<RealmBeatmap>().First();
            }, TaskCreationOptions.LongRunning).Wait();

            Task.Factory.StartNew(() =>
            {
                Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                Debug.Assert(beatmap != null);

                SynchronizationContext.SetSynchronizationContext(syncContext);
                Assert.False(beatmap.Hidden);
            }, TaskCreationOptions.LongRunning).Wait();
        }

        private static RealmBeatmapSet createBeatmapSet(RealmRuleset ruleset)
        {
            RealmFile createRealmFile() => new RealmFile { Hash = Guid.NewGuid().ToString().ComputeSHA2Hash() };

            var metadata = new RealmBeatmapMetadata
            {
                Title = "My Love",
                Artist = "Kuba Oms"
            };

            var beatmapSet = new RealmBeatmapSet
            {
                Beatmaps =
                {
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Easy", },
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Normal", },
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Hard", },
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Insane", }
                },
                Files =
                {
                    new RealmNamedFileUsage(createRealmFile(), "test [easy].osu"),
                    new RealmNamedFileUsage(createRealmFile(), "test [normal].osu"),
                    new RealmNamedFileUsage(createRealmFile(), "test [hard].osu"),
                    new RealmNamedFileUsage(createRealmFile(), "test [insane].osu"),
                }
            };

            for (int i = 0; i < 8; i++)
                beatmapSet.Files.Add(new RealmNamedFileUsage(createRealmFile(), $"hitsound{i}.mp3"));

            foreach (var b in beatmapSet.Beatmaps)
                b.BeatmapSet = beatmapSet;

            return beatmapSet;
        }

        private static RealmRuleset createRuleset() =>
            new RealmRuleset(0, "osu!", "osu", true);
    }
}
