using System;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Models;
using Xunit;
using Xunit.Abstractions;

namespace osu.Game
{
    public class UsageTests : IDisposable
    {
        private readonly ITestOutputHelper output;

        private static readonly TemporaryNativeStorage storage = new TemporaryNativeStorage("realm-test");

        public UsageTests(ITestOutputHelper output)
        {
            this.output = output;

            output.WriteLine($"Running tests at storage location {storage.GetFullPath(string.Empty)}");
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        [Fact]
        public void TestConstructRealm()
        {
            using (var realmFactory = new RealmContextFactory(storage))
            {
                realmFactory.Context.Refresh();
            }

            using (var realmFactory = new RealmContextFactory(storage))
            {
                realmFactory.Context.Refresh();
            }
        }

        /// <summary>
        /// Just test the construction of a new database works.
        /// </summary>
        // [Fact]
        public void TestPersistNewBeatmap()
        {
            using (var realmFactory = new RealmContextFactory(storage))
            {
                using (var usage = realmFactory.GetForWrite())
                {
                    usage.Realm.Add(new BeatmapSetInfo
                    {
                        Beatmaps =
                        {
                            new BeatmapInfo
                            {
                                Version = "Easy",
                                Difficulty = new BeatmapDifficulty(),
                            },
                            new BeatmapInfo
                            {
                                Version = "Normal",
                                Difficulty = new BeatmapDifficulty(),
                            },
                            new BeatmapInfo
                            {
                                Version = "Hard",
                                Difficulty = new BeatmapDifficulty(),
                            }
                        },
                        Metadata = new BeatmapMetadata
                        {
                            Title = "My Love",
                            Artist = "Kuba Oms"
                        },
                    });

                    usage.Commit();
                }
            }
        }

        public void Dispose()
        {
            storage?.Dispose();
        }
    }
}
