// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Stores;
using osu.Game.Tests.Resources;
using Realms;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace osu.Game.IsolatedTests
{
    [TestFixture]
    public class BeatmapImporterTests : RealmTest
    {
        [Test]
        public void TestImportBeatmapThenCleanup()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                Live<RealmBeatmapSet>? imported;

                using (var reader = new ZipArchiveReader(TestResources.GetTestBeatmapStream()))
                    imported = await importer.Import(reader);

                Assert.AreEqual(1, realmFactory.Context.All<RealmBeatmapSet>().Count());

                Assert.NotNull(imported);
                Debug.Assert(imported != null);

                imported.PerformWrite(s => s.DeletePending = true);

                Assert.AreEqual(1, realmFactory.Context.All<RealmBeatmapSet>().Count(s => s.DeletePending));
            });

            Logger.Log("Running with no work to purge pending deletions");

            RunTestWithRealm((realmFactory, _) => { Assert.AreEqual(0, realmFactory.Context.All<RealmBeatmapSet>().Count()); });
        }

        [Test]
        public void TestImportWhenClosed()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                await LoadOszIntoStore(importer, realmFactory.Context);
            });
        }

        [Test]
        public void TestImportThenDelete()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                deleteBeatmapSet(imported, realmFactory.Context);
            });
        }

        [Test]
        public void TestImportThenDeleteFromStream()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var tempPath = TestResources.GetTestBeatmapForImport();

                Live<RealmBeatmapSet>? importedSet;

                using (var stream = File.OpenRead(tempPath))
                {
                    importedSet = await importer.Import(new ImportTask(stream, Path.GetFileName(tempPath)));
                    ensureLoaded(realmFactory.Context);
                }

                Assert.NotNull(importedSet);
                Debug.Assert(importedSet != null);

                Assert.IsTrue(File.Exists(tempPath), "Stream source file somehow went missing");
                File.Delete(tempPath);

                var imported = realmFactory.Context.All<RealmBeatmapSet>().First(beatmapSet => beatmapSet.ID == importedSet.ID);

                deleteBeatmapSet(imported, realmFactory.Context);
            });
        }

        [Test]
        public void TestImportThenImport()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var imported = await LoadOszIntoStore(importer, realmFactory.Context);
                var importedSecondTime = await LoadOszIntoStore(importer, realmFactory.Context);

                // check the newly "imported" beatmap is actually just the restored previous import. since it matches hash.
                Assert.IsTrue(imported.ID == importedSecondTime.ID);
                Assert.IsTrue(imported.Beatmaps.First().ID == importedSecondTime.Beatmaps.First().ID);

                checkBeatmapSetCount(realmFactory.Context, 1);
                checkSingleReferencedFileCount(realmFactory.Context, 18);
            });
        }

        [Test]
        public void TestImportThenImportWithReZip()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                Directory.CreateDirectory(extractedFolder);

                try
                {
                    var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                    string hashBefore = hashFile(temp);

                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(extractedFolder);

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    // zip files differ because different compression or encoder.
                    Assert.AreNotEqual(hashBefore, hashFile(temp));

                    var importedSecondTime = await importer.Import(new ImportTask(temp));

                    ensureLoaded(realmFactory.Context);

                    Assert.NotNull(importedSecondTime);
                    Debug.Assert(importedSecondTime != null);

                    // but contents doesn't, so existing should still be used.
                    Assert.IsTrue(imported.ID == importedSecondTime.ID);
                    Assert.IsTrue(imported.Beatmaps.First().ID == importedSecondTime.PerformRead(s => s.Beatmaps.First().ID));
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        public void TestImportThenImportWithChangedHashedFile()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                Directory.CreateDirectory(extractedFolder);

                try
                {
                    var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                    await createScoreForBeatmap(realmFactory.Context, imported.Beatmaps.First());

                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(extractedFolder);

                    // arbitrary write to hashed file
                    // this triggers the special BeatmapManager.PreImport deletion/replacement flow.
                    using (var sw = new FileInfo(Directory.GetFiles(extractedFolder, "*.osu").First()).AppendText())
                        await sw.WriteLineAsync("// changed");

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    var importedSecondTime = await importer.Import(new ImportTask(temp));

                    ensureLoaded(realmFactory.Context);

                    // check the newly "imported" beatmap is not the original.
                    Assert.NotNull(importedSecondTime);
                    Debug.Assert(importedSecondTime != null);

                    Assert.IsTrue(imported.ID != importedSecondTime.ID);
                    Assert.IsTrue(imported.Beatmaps.First().ID != importedSecondTime.PerformRead(s => s.Beatmaps.First().ID));
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        [Ignore("intentionally broken by import optimisations")]
        public void TestImportThenImportWithChangedFile()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                Directory.CreateDirectory(extractedFolder);

                try
                {
                    var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(extractedFolder);

                    // arbitrary write to non-hashed file
                    using (var sw = new FileInfo(Directory.GetFiles(extractedFolder, "*.mp3").First()).AppendText())
                        await sw.WriteLineAsync("text");

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    var importedSecondTime = await importer.Import(new ImportTask(temp));

                    ensureLoaded(realmFactory.Context);

                    Assert.NotNull(importedSecondTime);
                    Debug.Assert(importedSecondTime != null);

                    // check the newly "imported" beatmap is not the original.
                    Assert.IsTrue(imported.ID != importedSecondTime.ID);
                    Assert.IsTrue(imported.Beatmaps.First().ID != importedSecondTime.PerformRead(s => s.Beatmaps.First().ID));
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        public void TestImportThenImportWithDifferentFilename()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                Directory.CreateDirectory(extractedFolder);

                try
                {
                    var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(extractedFolder);

                    // change filename
                    var firstFile = new FileInfo(Directory.GetFiles(extractedFolder).First());
                    firstFile.MoveTo(Path.Combine(firstFile.DirectoryName.AsNonNull(), $"{firstFile.Name}-changed{firstFile.Extension}"));

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    var importedSecondTime = await importer.Import(new ImportTask(temp));

                    ensureLoaded(realmFactory.Context);

                    Assert.NotNull(importedSecondTime);
                    Debug.Assert(importedSecondTime != null);

                    // check the newly "imported" beatmap is not the original.
                    Assert.IsTrue(imported.ID != importedSecondTime.ID);
                    Assert.IsTrue(imported.Beatmaps.First().ID != importedSecondTime.PerformRead(s => s.Beatmaps.First().ID));
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        [Ignore("intentionally broken by import optimisations")]
        public void TestImportCorruptThenImport()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                var firstFile = imported.Files.First();

                long originalLength;
                using (var stream = storage.GetStream(firstFile.File.StoragePath))
                    originalLength = stream.Length;

                using (var stream = storage.GetStream(firstFile.File.StoragePath, FileAccess.Write, FileMode.Create))
                    stream.WriteByte(0);

                var importedSecondTime = await LoadOszIntoStore(importer, realmFactory.Context);

                using (var stream = storage.GetStream(firstFile.File.StoragePath))
                    Assert.AreEqual(stream.Length, originalLength, "Corruption was not fixed on second import");

                // check the newly "imported" beatmap is actually just the restored previous import. since it matches hash.
                Assert.IsTrue(imported.ID == importedSecondTime.ID);
                Assert.IsTrue(imported.Beatmaps.First().ID == importedSecondTime.Beatmaps.First().ID);

                checkBeatmapSetCount(realmFactory.Context, 1);
                checkSingleReferencedFileCount(realmFactory.Context, 18);
            });
        }

        [Test]
        public void TestRollbackOnFailure()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                int loggedExceptionCount = 0;

                Logger.NewEntry += l =>
                {
                    if (l.Target == LoggingTarget.Database && l.Exception != null)
                        Interlocked.Increment(ref loggedExceptionCount);
                };

                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                realmFactory.Context.Write(() => imported.Hash += "-changed");

                checkBeatmapSetCount(realmFactory.Context, 1);
                checkBeatmapCount(realmFactory.Context, 12);
                checkSingleReferencedFileCount(realmFactory.Context, 18);

                var brokenTempFilename = TestResources.GetTestBeatmapForImport();

                MemoryStream brokenOsu = new MemoryStream();
                MemoryStream brokenOsz = new MemoryStream(await File.ReadAllBytesAsync(brokenTempFilename));

                File.Delete(brokenTempFilename);

                using (var outStream = File.Open(brokenTempFilename, FileMode.CreateNew))
                using (var zip = ZipArchive.Open(brokenOsz))
                {
                    zip.AddEntry("broken.osu", brokenOsu, false);
                    zip.SaveTo(outStream, CompressionType.Deflate);
                }

                // this will trigger purging of the existing beatmap (online set id match) but should rollback due to broken osu.
                try
                {
                    await importer.Import(new ImportTask(brokenTempFilename));
                }
                catch
                {
                }

                checkBeatmapSetCount(realmFactory.Context, 1);
                checkBeatmapCount(realmFactory.Context, 12);

                checkSingleReferencedFileCount(realmFactory.Context, 18);

                Assert.AreEqual(1, loggedExceptionCount);

                File.Delete(brokenTempFilename);
            });
        }

        [Test]
        public void TestImportThenDeleteThenImport()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                deleteBeatmapSet(imported, realmFactory.Context);

                var importedSecondTime = await LoadOszIntoStore(importer, realmFactory.Context);

                // check the newly "imported" beatmap is actually just the restored previous import. since it matches hash.
                Assert.IsTrue(imported.ID == importedSecondTime.ID);
                Assert.IsTrue(imported.Beatmaps.First().ID == importedSecondTime.Beatmaps.First().ID);
            });
        }

        [Test]
        public void TestImportThenDeleteThenImportWithOnlineIDsMissing()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var imported = await LoadOszIntoStore(importer, realmFactory.Context);

                realmFactory.Context.Write(() =>
                {
                    foreach (var b in imported.Beatmaps)
                        b.OnlineID = null;
                });

                deleteBeatmapSet(imported, realmFactory.Context);

                var importedSecondTime = await LoadOszIntoStore(importer, realmFactory.Context);

                // check the newly "imported" beatmap has been reimported due to mismatch (even though hashes matched)
                Assert.IsTrue(imported.ID != importedSecondTime.ID);
                Assert.IsTrue(imported.Beatmaps.First().ID != importedSecondTime.Beatmaps.First().ID);
            });
        }

        [Test]
        public void TestImportWithDuplicateBeatmapIDs()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var metadata = new RealmBeatmapMetadata
                {
                    Artist = "SomeArtist",
                    Author = "SomeAuthor"
                };

                var difficulty = new RealmBeatmapDifficulty();

                var ruleset = new RealmRuleset(0, "test!", "test", true);

                var toImport = new RealmBeatmapSet
                {
                    OnlineID = 1,
                    Beatmaps =
                    {
                        new RealmBeatmap(ruleset, difficulty, metadata)
                        {
                            OnlineID = 2,
                        },
                        new RealmBeatmap(ruleset, difficulty, metadata)
                        {
                            OnlineID = 2,
                            Status = BeatmapSetOnlineStatus.Loved,
                        }
                    }
                };

                var imported = await importer.Import(toImport);

                Assert.NotNull(imported);
                Debug.Assert(imported != null);

                Assert.AreEqual(null, imported.PerformRead(s => s.Beatmaps[0].OnlineID));
                Assert.AreEqual(null, imported.PerformRead(s => s.Beatmaps[1].OnlineID));
            });
        }

        [Test]
        public void TestImportWhenFileOpen()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();
                using (File.OpenRead(temp))
                    await importer.Import(temp);
                ensureLoaded(realmFactory.Context);
                File.Delete(temp);
                Assert.IsFalse(File.Exists(temp), "We likely held a read lock on the file when we shouldn't");
            });
        }

        [Test]
        public void TestImportWithDuplicateHashes()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                Directory.CreateDirectory(extractedFolder);

                try
                {
                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(extractedFolder);

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.AddEntry("duplicate.osu", Directory.GetFiles(extractedFolder, "*.osu").First());
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    await importer.Import(temp);

                    ensureLoaded(realmFactory.Context);
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        public void TestImportNestedStructure()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                string subfolder = Path.Combine(extractedFolder, "subfolder");

                Directory.CreateDirectory(subfolder);

                try
                {
                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(subfolder);

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    var imported = await importer.Import(new ImportTask(temp));

                    Assert.NotNull(imported);
                    Debug.Assert(imported != null);

                    ensureLoaded(realmFactory.Context);

                    Assert.IsFalse(imported.PerformRead(s => s.Files.Any(f => f.Filename.Contains("subfolder"))), "Files contain common subfolder");
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        public void TestImportWithIgnoredDirectoryInArchive()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();

                string extractedFolder = $"{temp}_extracted";
                string dataFolder = Path.Combine(extractedFolder, "actual_data");
                string resourceForkFolder = Path.Combine(extractedFolder, "__MACOSX");
                string resourceForkFilePath = Path.Combine(resourceForkFolder, ".extracted");

                Directory.CreateDirectory(dataFolder);
                Directory.CreateDirectory(resourceForkFolder);

                using (var resourceForkFile = File.CreateText(resourceForkFilePath))
                {
                    await resourceForkFile.WriteLineAsync("adding content so that it's not empty");
                }

                try
                {
                    using (var zip = ZipArchive.Open(temp))
                        zip.WriteToDirectory(dataFolder);

                    using (var zip = ZipArchive.Create())
                    {
                        zip.AddAllFromDirectory(extractedFolder);
                        zip.SaveTo(temp, new ZipWriterOptions(CompressionType.Deflate));
                    }

                    var imported = await importer.Import(new ImportTask(temp));

                    Assert.NotNull(imported);
                    Debug.Assert(imported != null);

                    ensureLoaded(realmFactory.Context);

                    Assert.IsFalse(imported.PerformRead(s => s.Files.Any(f => f.Filename.Contains("__MACOSX"))), "Files contain resource fork folder, which should be ignored");
                    Assert.IsFalse(imported.PerformRead(s => s.Files.Any(f => f.Filename.Contains("actual_data"))), "Files contain common subfolder");
                }
                finally
                {
                    Directory.Delete(extractedFolder, true);
                }
            });
        }

        [Test]
        public void TestUpdateBeatmapInfo()
        {
            RunTestWithRealmAsync(async (realmFactory, storage) =>
            {
                using var importer = new BeatmapImporter(storage, realmFactory);
                using var store = new RulesetStore(realmFactory, storage);

                var temp = TestResources.GetTestBeatmapForImport();
                await importer.Import(temp);

                // Update via the beatmap, not the beatmap info, to ensure correct linking
                RealmBeatmapSet setToUpdate = realmFactory.Context.All<RealmBeatmapSet>().First();

                var beatmapToUpdate = setToUpdate.Beatmaps.First();

                realmFactory.Context.Write(() => beatmapToUpdate.DifficultyName = "updated");

                RealmBeatmap updatedInfo = realmFactory.Context.All<RealmBeatmap>().First(b => b.ID == beatmapToUpdate.ID);
                Assert.That(updatedInfo.DifficultyName, Is.EqualTo("updated"));
            });
        }

        [Test]
        public void TestUpdateBeatmapFile()
        {
            // TODO: reimplement after we can use BeatmapManager without a GameHost.
            // await RunTestWithRealmAsync(async (realmFactory, storage) =>
            // {
            //     var importer = new BeatmapImporter(storage, realmFactory);
            //
            //     var temp = TestResources.GetTestBeatmapForImport();
            //     await importer.Import(temp);
            //
            //     RealmBeatmapSet setToUpdate = realmFactory.Context.All<RealmBeatmapSet>().First();
            //
            //     var beatmapInfo = setToUpdate.Beatmaps.First(b => b.Ruleset.OnlineID == 0);
            //     Beatmap beatmapToUpdate = (Beatmap)manager.GetWorkingBeatmap(setToUpdate.Beatmaps.First(b => b.RulesetID == 0)).Beatmap;
            //     BeatmapSetFileInfo fileToUpdate = setToUpdate.Files.First(f => beatmapToUpdate.BeatmapInfo.Path.Contains(f.Filename));
            //
            //     string oldMd5Hash = beatmapToUpdate.BeatmapInfo.MD5Hash;
            //
            //     beatmapToUpdate.HitObjects.Clear();
            //     beatmapToUpdate.HitObjects.Add(new HitCircle { StartTime = 5000 });
            //
            //     manager.Save(beatmapInfo, beatmapToUpdate);
            //
            //     // Check that the old file reference has been removed
            //     Assert.That(manager.QueryBeatmapSet(s => s.ID == setToUpdate.ID).Files.All(f => f.ID != fileToUpdate.ID));
            //
            //     // Check that the new file is referenced correctly by attempting a retrieval
            //     Beatmap updatedBeatmap = (Beatmap)manager.GetWorkingBeatmap(manager.QueryBeatmap(b => b.ID == beatmapToUpdate.BeatmapInfo.ID)).Beatmap;
            //     Assert.That(updatedBeatmap.HitObjects.Count, Is.EqualTo(1));
            //     Assert.That(updatedBeatmap.HitObjects[0].StartTime, Is.EqualTo(5000));
            //     Assert.That(updatedBeatmap.BeatmapInfo.MD5Hash, Is.Not.EqualTo(oldMd5Hash));
            // });
        }

        [Test]
        public void TestCreateNewEmptyBeatmap()
        {
            // TODO: reimplement after we can use BeatmapManager without a GameHost.
            // RunTestWithRealm((realmFactory, storage) =>
            // {
            //     var importer = new BeatmapImporter(storage, realmFactory);
            //
            //     var working = manager.CreateNew(new OsuRuleset().RulesetInfo, User.SYSTEM_USER);
            //
            //     manager.Save(working.BeatmapInfo, working.Beatmap);
            //
            //     var retrievedSet = realm.All<RealmBeatmapSet>()[0];
            //
            //     // Check that the new file is referenced correctly by attempting a retrieval
            //     Beatmap updatedBeatmap = (Beatmap)manager.GetWorkingBeatmap(retrievedSet.Beatmaps[0]).Beatmap;
            //     Assert.That(updatedBeatmap.HitObjects.Count, Is.EqualTo(0));
            // });
        }

        [Test]
        public void TestCreateNewBeatmapWithObject()
        {
            // TODO: reimplement after we can use BeatmapManager without a GameHost.
            // await RunTestWithRealmAsync(async (realmFactory, storage) =>
            // {
            //     var importer = new BeatmapImporter(storage, realmFactory);
            //
            //     var working = manager.CreateNew(new OsuRuleset().RulesetInfo, User.SYSTEM_USER);
            //
            //     ((Beatmap)working.Beatmap).HitObjects.Add(new HitCircle { StartTime = 5000 });
            //
            //     manager.Save(working.BeatmapInfo, working.Beatmap);
            //
            //     var retrievedSet = realm.All<RealmBeatmapSet>()[0];
            //
            //     // Check that the new file is referenced correctly by attempting a retrieval
            //     Beatmap updatedBeatmap = (Beatmap)manager.GetWorkingBeatmap(retrievedSet.Beatmaps[0]).Beatmap;
            //     Assert.That(updatedBeatmap.HitObjects.Count, Is.EqualTo(1));
            //     Assert.That(updatedBeatmap.HitObjects[0].StartTime, Is.EqualTo(5000));
            // });
        }

        public static async Task<RealmBeatmapSet?> LoadQuickOszIntoOsu(BeatmapImporter importer, Realm realm)
        {
            var temp = TestResources.GetQuickTestBeatmapForImport();

            var importedSet = await importer.Import(new ImportTask(temp));

            Assert.NotNull(importedSet);

            ensureLoaded(realm);

            waitForOrAssert(() => !File.Exists(temp), "Temporary file still exists after standard import", 5000);

            return realm.All<RealmBeatmapSet>().FirstOrDefault(beatmapSet => beatmapSet.ID == importedSet!.ID);
        }

        public static async Task<RealmBeatmapSet> LoadOszIntoStore(BeatmapImporter importer, Realm realm, string? path = null, bool virtualTrack = false)
        {
            var temp = path ?? TestResources.GetTestBeatmapForImport(virtualTrack);

            var importedSet = await importer.Import(new ImportTask(temp));

            Assert.NotNull(importedSet);
            Debug.Assert(importedSet != null);

            ensureLoaded(realm);

            waitForOrAssert(() => !File.Exists(temp), "Temporary file still exists after standard import", 5000);

            return realm.All<RealmBeatmapSet>().First(beatmapSet => beatmapSet.ID == importedSet.ID);
        }

        private void deleteBeatmapSet(RealmBeatmapSet imported, Realm realm)
        {
            realm.Write(() => imported.DeletePending = true);

            checkBeatmapSetCount(realm, 0);
            checkBeatmapSetCount(realm, 1, true);

            Assert.IsTrue(realm.All<RealmBeatmapSet>().First(_ => true).DeletePending);
        }

        private static Task createScoreForBeatmap(Realm realm, RealmBeatmap beatmap)
        {
            // TODO: reimplement?
            // return ImportScoreTest.LoadScoreIntoOsu(osu, new ScoreInfo
            // {
            //     OnlineScoreID = 2,
            //     Beatmap = beatmap,
            //     BeatmapInfoID = beatmap.ID
            // }, new ImportScoreTest.TestArchiveReader());

            return Task.CompletedTask;
        }

        private static void checkBeatmapSetCount(Realm realm, int expected, bool includeDeletePending = false)
        {
            Assert.AreEqual(expected, includeDeletePending
                ? realm.All<RealmBeatmapSet>().Count()
                : realm.All<RealmBeatmapSet>().Count(s => !s.DeletePending));
        }

        private static string hashFile(string filename)
        {
            using (var s = File.OpenRead(filename))
                return s.ComputeMD5Hash();
        }

        private static void checkBeatmapCount(Realm realm, int expected)
        {
            Assert.AreEqual(expected, realm.All<RealmBeatmap>().Where(_ => true).ToList().Count);
        }

        private static void checkSingleReferencedFileCount(Realm realm, int expected)
        {
            int singleReferencedCount = 0;

            foreach (var f in realm.All<RealmFile>())
            {
                if (f.BacklinksCount == 1)
                    singleReferencedCount++;
            }

            Assert.AreEqual(expected, singleReferencedCount);
        }

        private static void ensureLoaded(Realm realm, int timeout = 600)
        {
            IQueryable<RealmBeatmapSet>? resultSets = null;

            waitForOrAssert(() => (resultSets = realm.All<RealmBeatmapSet>().Where(s => s.OnlineID == 241526)).Any(),
                @"BeatmapSet did not import to the database in allocated time.", timeout);

            // ensure we were stored to beatmap database backing...
            Assert.IsTrue(resultSets?.Count() == 1, $@"Incorrect result count found ({resultSets?.Count()} but should be 1).");
            IEnumerable<RealmBeatmapSet> queryBeatmapSets() => realm.All<RealmBeatmapSet>().Where(s => s.OnlineID == 241526);
            IEnumerable<RealmBeatmap> queryBeatmaps() => realm.All<RealmBeatmap>().Where(s => s.BeatmapSet != null);

            // if we don't re-check here, the set will be inserted but the beatmaps won't be present yet.
            waitForOrAssert(() => queryBeatmaps().Count() == 12,
                @"Beatmaps did not import to the database in allocated time", timeout);
            waitForOrAssert(() => queryBeatmapSets().Count() == 1,
                @"BeatmapSet did not import to the database in allocated time", timeout);
            int countBeatmapSetBeatmaps = 0;
            int countBeatmaps = 0;
            waitForOrAssert(() =>
                    (countBeatmapSetBeatmaps = queryBeatmapSets().First().Beatmaps.Count) ==
                    (countBeatmaps = queryBeatmaps().Count()),
                $@"Incorrect database beatmap count post-import ({countBeatmaps} but should be {countBeatmapSetBeatmaps}).", timeout);

            var set = queryBeatmapSets().First();
            foreach (RealmBeatmap b in set.Beatmaps)
                Assert.IsTrue(set.Beatmaps.Any(c => c.OnlineID == b.OnlineID));
            Assert.IsTrue(set.Beatmaps.Count > 0);

            // TODO: add back working beatmap checks.
            // var beatmap = store.GetWorkingBeatmap(set.Beatmaps.First(b => b.OnlineID == 0))?.Beatmap;
            // Assert.IsTrue(beatmap?.HitObjects.Any() == true);
            // beatmap = store.GetWorkingBeatmap(set.Beatmaps.First(b => b.OnlineID == 1))?.Beatmap;
            // Assert.IsTrue(beatmap?.HitObjects.Any() == true);
            // beatmap = store.GetWorkingBeatmap(set.Beatmaps.First(b => b.OnlineID == 2))?.Beatmap;
            // Assert.IsTrue(beatmap?.HitObjects.Any() == true);
            // beatmap = store.GetWorkingBeatmap(set.Beatmaps.First(b => b.OnlineID == 3))?.Beatmap;
            // Assert.IsTrue(beatmap?.HitObjects.Any() == true);
        }

        private static void waitForOrAssert(Func<bool> result, string failureMessage, int timeout = 60000)
        {
            const int sleep = 200;

            while (timeout > 0 && !result())
            {
                Thread.Sleep(sleep);
                timeout -= sleep;
            }
        }
    }
}
