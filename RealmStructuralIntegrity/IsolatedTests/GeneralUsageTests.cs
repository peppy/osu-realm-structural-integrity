using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Models;
using Realms;

namespace osu.Game.IsolatedTests
{
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    [TestFixture]
    public class GeneralUsageTests : RealmTest
    {
        private const int beatmap_set_import_count = 1000;

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Test]
        public void TestConstructRealm()
        {
            RunTestWithRealm((realmFactory, _) => { realmFactory.Context.Refresh(); });
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Test]
        public void TestImportSingleBeatmap()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                    Assert.AreEqual(1, file.Usages.Count());
            });
        }

        [Test]
        public void TestImportManyBeatmapsSingleTransaction()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                    Assert.AreEqual(1, file.Usages.Count());
            });
        }

        [Test]
        public void TestImportManyBeatmapsIndividualTransactions()
        {
            RunTestWithRealm((realmFactory, _) =>
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

                Logger.Log($"inserted {realm.All<RealmBeatmapSet>().Count()} sets");

                foreach (var file in realm.All<RealmFile>())
                    Assert.AreEqual(1, file.Usages.Count());
            });
        }

        /// <summary>
        /// Example of how a primary key can be stored and then used for a lookup on a different thread context.
        /// Of note, this will only work if Refresh is first called on the target context to pull in the changes.
        /// Because of this there's a chance it might not resolve (if the object was since deleted).
        /// </summary>
        [Test]
        public void TestThreadedAccessViaPrimaryKey()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                        Assert.AreNotEqual(context, realm);

                        beatmap = realm.Write(r => r.Add(new RealmBeatmap(CreateRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata())));

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

                Assert.AreEqual(key, beatmap.ID);
            });
        }

        /// <summary>
        /// Example of how <see cref="ThreadSafeReference"/> can be used to ferry data between threads.
        /// Of note:
        /// - References can be resolved even if the originating context has been closed.
        /// - This method does not require a Refresh() call on the target context, making it potentially beneficial compared to a key lookup.
        /// - ResolveReference may return null if the referenced object has been deleted.
        /// </summary>
        [Test]
        public void TestThreadedAccessViaSafeReference()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                        Assert.AreNotEqual(context, realm);

                        beatmap = realm.Write(r => r.Add(new RealmBeatmap(CreateRuleset(), new RealmBeatmapDifficulty(), new RealmBeatmapMetadata())));

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

                Assert.AreEqual(key, beatmap.ID);
            });
        }

        [Test]
        public void TestThreadedAccessViaLive()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                            Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                            liveBeatmap.PerformRead(b => { Logger.Log(b.DifficultyName); });

                            liveBeatmap.PerformWrite(b => b.Hidden = true);

                            Assert.Throws<InvalidOperationException>(() => liveBeatmap.PerformRead(b => b.Difficulty));
                        }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();
                    }
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler);

                refreshUntilCompleted(realmFactory.Context, task);

                Assert.AreEqual(1, realmFactory.Context.All<RealmBeatmap>().Count(b => b.Hidden));
            });
        }

        [Test]
        public void TestThreadedAccessWithoutSharedSynchronizationContext()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                        Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                        // expected one of these to crash as this context was opened on another thread
                        Assert.Throws<Exception>(() => realm.Refresh());
                    }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();

                realm?.Dispose();
            });
        }

        [Test]
        public void TestThreadedAccessViaSharedSynchronizationContext()
        {
            RunTestWithRealm((realmFactory, _) =>
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
                        Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, thread1);
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

            Logger.Log($"refreshed {refreshCount} times");
        }
    }
}
