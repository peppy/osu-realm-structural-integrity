// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Models;
using Realms;

namespace osu.Game.Stores
{
    /// <summary>
    /// Handles the Store and retrieval of Files/FileSets to the database backing
    /// </summary>
    public class FileStore
    {
        private readonly RealmContextFactory realmFactory;
        public readonly IResourceStore<byte[]> Store;

        public Storage Storage;

        public FileStore(RealmContextFactory realmFactory, Storage storage)
        {
            this.realmFactory = realmFactory;
            Storage = storage.GetStorageForDirectory(@"files");
            Store = new StorageBackedResourceStore(Storage);
        }

        /// <summary>
        /// Add a new file to the game-wide database, copying it to permanent storage if not already present.
        /// </summary>
        /// <param name="data">The file data stream.</param>
        /// <param name="realm">The realm instance to add to. Should already be in a transaction.</param>
        /// <returns></returns>
        public RealmFile Add(Stream data, Realm realm)
        {
            string hash = data.ComputeSHA2Hash();

            var existing = realm.Find<RealmFile>(hash);

            var file = existing ?? new RealmFile { Hash = hash };

            string path = file.StoragePath;

            // we may be re-adding a file to fix missing store entries.
            bool requiresCopy = !Storage.Exists(path);

            if (!requiresCopy)
            {
                // even if the file already exists, check the existing checksum for safety.
                using (var stream = Storage.GetStream(path))
                    requiresCopy |= stream.ComputeSHA2Hash() != hash;
            }

            if (requiresCopy)
            {
                data.Seek(0, SeekOrigin.Begin);

                using (var output = Storage.GetStream(path, FileAccess.Write))
                    data.CopyTo(output);

                data.Seek(0, SeekOrigin.Begin);
            }

            if (!file.IsManaged)
                realm.Add(file);

            return file;
        }

        public void Cleanup()
        {
            var realm = realmFactory.Context;

            using (var transaction = realm.BeginWrite())
            {
                var unreferencedFiles = realm.All<RealmFile>().ToList();

                foreach (var file in unreferencedFiles)
                {
                    if (file.BacklinksCount > 0)
                        continue;

                    try
                    {
                        Storage.Delete(file.StoragePath);
                        realm.Remove(file);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $@"Could not delete databased file {file.Hash}");
                    }
                }

                transaction.Commit();
            }
        }
    }
}
