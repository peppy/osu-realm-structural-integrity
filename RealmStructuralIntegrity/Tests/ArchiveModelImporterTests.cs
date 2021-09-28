// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.IO.Archives;
using osu.Game.Stores;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests
{
    public class ArchiveModelImporterTests : TestBase
    {
        [Test]
        public void TestImportBeatmap()
        {
            RunTestWithRealm((realmFactory, storage) =>
            {
                var importer = new BeatmapImporter(storage, realmFactory);

                using (new RulesetStore(realmFactory, storage))
                using (var reader = new ZipArchiveReader(TestResources.GetTestBeatmapStream()))
                    importer.Import(reader).Wait();
            });
        }
    }
}
