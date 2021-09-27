using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public class UsageTests
    {
        private readonly ITestOutputHelper output;

        private static readonly TemporaryNativeStorage storage;

        private const int beatmap_set_import_count = 1000;

        static UsageTests()
        {
            storage = new TemporaryNativeStorage("realm-test");
            storage.DeleteDirectory(string.Empty);
        }

        public UsageTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestConstructRealm()
        {
            runTestWithRealm(realmFactory => { realmFactory.Context.Refresh(); });
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestImportSingleBeatmap()
        {
            runTestWithRealm(realmFactory =>
            {
                var realm = realmFactory.Context;

                realm.Write(() =>
                {
                    var ruleset = createRuleset();
                    realm.Add(ruleset, true);

                    var beatmapSet = createBeatmapSet(ruleset);

                    realm.Add(beatmapSet);
                });

                foreach (var file in realm.All<RealmFile>())
                    Assert.Equal(1, file.ReferenceCount);
            });
        }

        [Fact]
        public void TestImportManyBeatmapsSingleTransaction()
        {
            runTestWithRealm(realmFactory =>
            {
                var realm = realmFactory.Context;

                realm.Write(() =>
                {
                    var ruleset = createRuleset();
                    realm.Add(ruleset, true);

                    for (int i = 0; i < beatmap_set_import_count; i++)
                    {
                        var beatmapSet = createBeatmapSet(ruleset);
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
            runTestWithRealm(realmFactory =>
            {
                var ruleset = createRuleset();

                var realm = realmFactory.Context;

                realm.Write(() => realm.Add(ruleset, true));

                var threadRef = ThreadSafeReference.Create(ruleset);

                var task = Task.Factory.StartNew(() =>
                {
                    using (var innerRealm = realmFactory.CreateContext())
                    {
                        ruleset = innerRealm.ResolveReference(threadRef);

                        for (int i = 0; i < beatmap_set_import_count; i++)
                            innerRealm.Write(() => innerRealm.Add(createBeatmapSet(ruleset)));
                    }
                });

                refreshUntilCompleted(realm, task);

                output.WriteLine($"inserted {realm.All<RealmBeatmapSet>().Count()} sets");

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
            runTestWithRealm(realmFactory =>
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
                            realm.Add(beatmap = new RealmBeatmap(createRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));
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
            runTestWithRealm(realmFactory =>
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
                            realm.Add(beatmap = new RealmBeatmap(createRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata()));
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
            runTestWithRealm(realmFactory =>
            {
                int thread1;

                var ruleset = createRuleset();

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

                            liveBeatmap.PerformRead(b => { output.WriteLine(b.DifficultyName); });

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
            runTestWithRealm(realmFactory =>
            {
                Realm? realm = null;
                int thread1;

                var ruleset = createRuleset();

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
            runTestWithRealm(realmFactory =>
            {
                var syncContext = new LocalSyncContext();

                Realm? realm;
                int thread1;

                Task.Factory.StartNew(() =>
                {
                    thread1 = Thread.CurrentThread.ManagedThreadId;
                    SynchronizationContext.SetSynchronizationContext(syncContext);

                    realm = realmFactory.CreateContext();
                    realm.Write(() => realm.Add(createBeatmapSet(createRuleset())));
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

            output.WriteLine($"refreshed {refreshCount} times");
        }

        private void runTestWithRealm(Action<RealmContextFactory> testAction, [CallerMemberName] string caller = "")
        {
            AsyncContext.Run(() =>
            {
                using (var realmFactory = new RealmContextFactory(storage, caller))
                {
                    output.WriteLine($"Running test using realm file {storage.GetFullPath(realmFactory.Filename)}");
                    testAction(realmFactory);

                    realmFactory.Dispose();
                    output.WriteLine($"Final database size: {storage.GetStream(realmFactory.Filename)?.Length ?? 0}");

                    realmFactory.Compact();
                    output.WriteLine($"Final database size after compact: {storage.GetStream(realmFactory.Filename)?.Length ?? 0}");
                }
            });
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

        public class LocalSyncContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                SetSynchronizationContext(this);
                d(state);
            }
        }
    }
}
