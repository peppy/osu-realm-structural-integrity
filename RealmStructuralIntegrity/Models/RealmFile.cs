// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using Realms;

namespace osu.Game.Models
{
    public class RealmFile : RealmObject
    {
        /// <summary>
        /// SHA-256 hash of the file content.
        /// </summary>
        [PrimaryKey]
        public string Hash { get; set; }

        /// <summary>
        /// The number of times this file is referenced across all usages.
        /// </summary>
        public int ReferenceCount { get; set; }

        public string StoragePath => Path.Combine(Hash.Remove(1), Hash.Remove(2), Hash);
    }
}
