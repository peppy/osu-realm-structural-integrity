// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Game.Models.Interfaces;
using Realms;

namespace osu.Game.Models
{
    [MapTo("File")]
    public class RealmFile : RealmObject, IFileInfo
    {
        /// <summary>
        /// SHA-256 hash of the file content.
        /// </summary>
        [PrimaryKey]
        public string Hash { get; set; } = String.Empty;

        [Backlink(nameof(RealmNamedFileUsage.File))]
        public IQueryable<RealmNamedFileUsage> Usages { get; } = null!; // TODO: check efficiency (ie. do we need to cache this to a count still?)

        public string StoragePath => Path.Combine(Hash.Remove(1), Hash.Remove(2), Hash);
    }
}
