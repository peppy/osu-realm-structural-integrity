// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Logging;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Stores;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests
{
    [TestFixture]
    public class ArchiveModelImporterTests : TestBase
    {
        [Test]
        public void TestImportBeatmapThenCleanup()
        {
            RunTestWithRealm((realmFactory, storage) =>
            {
                var importer = new BeatmapImporter(storage, realmFactory);

                using (new RulesetStore(realmFactory, storage))
                {
                    using (var reader = new ZipArchiveReader(TestResources.GetTestBeatmapStream()))
                        importer.Import(reader).Wait();

                    Assert.AreEqual(0, realmFactory.Context.All<RealmBeatmapSet>().Count());

                    realmFactory.Context.Refresh();

                    Assert.AreEqual(1, realmFactory.Context.All<RealmBeatmapSet>().Count());

                    using (var reader = new ZipArchiveReader(TestResources.GetTestBeatmapStream()))
                        importer.Import(reader).Wait();

                    realmFactory.Context.Refresh();

                    Assert.AreEqual(2, realmFactory.Context.All<RealmBeatmapSet>().Count());
                    Assert.AreEqual(1, realmFactory.Context.All<RealmBeatmapSet>().Count(s => s.DeletePending));
                }
            });

            Logger.Log("Running with no work to purge pending deletions");

            RunTestWithRealm((realmFactory, _) =>
            {
                Assert.AreEqual(1, realmFactory.Context.All<RealmBeatmapSet>().Count());
                Assert.AreEqual(0, realmFactory.Context.All<RealmBeatmapSet>().Count(s => s.DeletePending));
            });
        }
    }
}
