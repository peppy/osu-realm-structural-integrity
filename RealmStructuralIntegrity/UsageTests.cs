using System;
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
        private static readonly TemporaryNativeStorage storage = new TemporaryNativeStorage("realm-test");

        public UsageTests(ITestOutputHelper output)
        {
            output.WriteLine($"Running tests at storage location {storage.GetFullPath(string.Empty)}");
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestConstructRealm()
        {
            AsyncContext.Run(() =>
            {
                using (var realmFactory = new RealmContextFactory(storage))
                {
                    realmFactory.Context.Refresh();
                }
            });
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestPersistNewBeatmap()
        {
            AsyncContext.Run(() =>
            {
                using (var realmFactory = new RealmContextFactory(storage))
                {
                    using (var usage = realmFactory.GetForWrite())
                    {
                        var metdata = new RealmBeatmapMetadata
                        {
                            Title = "My Love",
                            Artist = "Kuba Oms"
                        };

                        var beatmapSet = new RealmBeatmapSet
                        {
                            Beatmaps =
                            {
                                new RealmBeatmap
                                {
                                    DifficultyName = "Easy",
                                    Difficulty = new RealmBeatmapDifficulty(),
                                    Metadata = metdata,
                                },
                                new RealmBeatmap
                                {
                                    DifficultyName = "Normal",
                                    Difficulty = new RealmBeatmapDifficulty(),
                                    Metadata = metdata,
                                },
                                new RealmBeatmap
                                {
                                    DifficultyName = "Hard",
                                    Difficulty = new RealmBeatmapDifficulty(),
                                    Metadata = metdata,
                                }
                            },
                            Files =
                            {
                                new RealmNamedFileUsage
                                {
                                    Filename = "test [easy].osu",
                                    File = new RealmFile
                                    {
                                        Hash = Guid.NewGuid().ToString().ComputeSHA2Hash()
                                    }
                                },
                                new RealmNamedFileUsage
                                {
                                    Filename = "test [normal].osu",
                                    File = new RealmFile
                                    {
                                        Hash = Guid.NewGuid().ToString().ComputeSHA2Hash()
                                    }
                                },
                                new RealmNamedFileUsage
                                {
                                    Filename = "test [hard].osu",
                                    File = new RealmFile
                                    {
                                        Hash = Guid.NewGuid().ToString().ComputeSHA2Hash()
                                    }
                                },
                            }
                        };

                        foreach (var b in beatmapSet.Beatmaps)
                            b.BeatmapSet = beatmapSet;

                        usage.Realm.Add(beatmapSet);

                        usage.Commit();

                        foreach (var file in usage.Realm.All<RealmFile>())
                            Assert.Equal(1, file.ReferenceCount);
                    }
                }
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
                using var realmFactory = new RealmContextFactory(storage);

                // retrieve context to bind main realm to this thread.
                var context = realmFactory.Context;

                RealmBeatmap beatmap = null;

                Guid key = Guid.Empty;

                Task.Run(() =>
                {
                    // write to realm on an async thread.
                    using (var usage = realmFactory.GetForWrite())
                    {
                        Assert.NotEqual(context, usage.Realm);

                        usage.Realm.Add(beatmap = new RealmBeatmap());

                        usage.Commit();

                        // the key must be accessed before the local context is closed.
                        key = beatmap.ID;
                    }
                }).Wait();

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
                using var realmFactory = new RealmContextFactory(storage);

                // retrieve context to bind main realm to this thread.
                var context = realmFactory.Context;

                RealmBeatmap beatmap = null;

                ThreadSafeReference.Object<RealmBeatmap> threadSafeReference = null;
                Guid key = Guid.Empty;

                Task.Run(() =>
                {
                    // write to realm on an async thread.
                    using (var usage = realmFactory.GetForWrite())
                    {
                        Assert.NotEqual(context, usage.Realm);

                        usage.Realm.Add(beatmap = new RealmBeatmap());
                        usage.Commit();

                        threadSafeReference = ThreadSafeReference.Create(beatmap);
                        key = beatmap.ID;
                    }
                }).Wait();

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
                Realm realm = null;
                int thread1 = -1;

                Task.Factory.StartNew(() =>
                {
                    thread1 = Thread.CurrentThread.ManagedThreadId;

                    realm = Realm.GetInstance(new RealmConfiguration(Path.GetTempFileName()));
                    realm.Write(() => realm.Add(new RealmBeatmap()));
                    realm.Refresh();
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();

                Task.Factory.StartNew(() =>
                {
                    Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread1);

                    // expected one of these to crash as this context was opened on another thread?
                    realm.Refresh();
                    realm.Write(() => realm.Add(new RealmBeatmap()));
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.HideScheduler).Wait();
            });
        }

        [Fact]
        public void TestThreadedAccessViaSharedSynchronizationContext()
        {
            using var realmFactory = new RealmContextFactory(storage);

            RealmBeatmap beatmap = null;

            var syncContext = new SynchronizationContext();

            Task.Factory.StartNew(() =>
            {
                using (var usage = realmFactory.GetForWrite())
                {
                    usage.Realm.Add(beatmap = new RealmBeatmap());
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

                SynchronizationContext.SetSynchronizationContext(syncContext);
                Assert.False(beatmap.Hidden);
            }, TaskCreationOptions.LongRunning).Wait();
        }

        public void Dispose()
        {
            storage?.Dispose();
        }
    }
}
