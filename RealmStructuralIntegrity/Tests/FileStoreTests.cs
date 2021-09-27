// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.IO;
using System.Linq;
using osu.Game.Models;
using osu.Game.Stores;
using Xunit;
using Xunit.Abstractions;

namespace osu.Game.Tests
{
    public class FileStoreTests : TestBase
    {
        public FileStoreTests(ITestOutputHelper logger)
            : base(logger)
        {
        }

        [Fact]
        public void TestImportFile()
        {
            RunTestWithRealm((realmFactory, storage) =>
            {
                var realm = realmFactory.Context;
                var files = new FileStore(realmFactory, storage);

                var testData = new MemoryStream(new byte[] { 0, 1, 2, 3 });

                realm.Write(() => files.Add(testData, realm));

                Assert.True(files.Storage.Exists("0/05/054edec1d0211f624fed0cbca9d4f9400b0e491c43742af2c5b0abebf0c990d8"));
                Assert.True(files.Storage.Exists(realm.All<RealmFile>().First().StoragePath));
            });
        }

        [Fact]
        public void TestImportSameFileTwice()
        {
            RunTestWithRealm((realmFactory, storage) =>
            {
                var realm = realmFactory.Context;
                var files = new FileStore(realmFactory, storage);

                var testData = new MemoryStream(new byte[] { 0, 1, 2, 3 });

                realm.Write(() => files.Add(testData, realm));
                realm.Write(() => files.Add(testData, realm));

                Assert.Equal(1, realm.All<RealmFile>().Count());
            });
        }

        [Fact]
        public void TestDontPurgeReferenced()
        {
            RunTestWithRealm((realmFactory, storage) =>
            {
                var realm = realmFactory.Context;
                var files = new FileStore(realmFactory, storage);

                var file = realm.Write(() => files.Add(new MemoryStream(new byte[] { 0, 1, 2, 3 }), realm));

                var timer = new Stopwatch();
                timer.Start();

                realm.Write(() =>
                {
                    // attach the file to an arbitrary beatmap
                    var beatmapSet = CreateBeatmapSet(CreateRuleset());

                    beatmapSet.Files.Add(new RealmNamedFileUsage(file, "arbitrary.resource"));

                    realm.Add(beatmapSet);
                });

                Logger.WriteLine($"Import complete at {timer.ElapsedMilliseconds}");

                string path = file.StoragePath;

                Assert.True(realm.All<RealmFile>().Any());
                Assert.True(files.Storage.Exists(path));

                files.Cleanup();
                Logger.WriteLine($"Cleanup complete at {timer.ElapsedMilliseconds}");

                Assert.True(realm.All<RealmFile>().Any());
                Assert.True(file.IsValid);
                Assert.True(files.Storage.Exists(path));
            });
        }

        [Fact]
        public void TestPurgeUnreferenced()
        {
            RunTestWithRealm((realmFactory, storage) =>
            {
                var realm = realmFactory.Context;
                var files = new FileStore(realmFactory, storage);

                var file = realm.Write(() => files.Add(new MemoryStream(new byte[] { 0, 1, 2, 3 }), realm));

                string path = file.StoragePath;

                Assert.True(realm.All<RealmFile>().Any());
                Assert.True(files.Storage.Exists(path));

                files.Cleanup();

                Assert.False(realm.All<RealmFile>().Any());
                Assert.False(file.IsValid);
                Assert.False(files.Storage.Exists(path));
            });
        }
    }
}