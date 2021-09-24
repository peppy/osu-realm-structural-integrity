// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Models.Interfaces;
using Realms;

namespace osu.Game.Models
{
    [ExcludeFromDynamicCompile]
    [MapTo("BeatmapSet")]
    public class RealmBeatmapSet : RealmObject, IHasGuidPrimaryKey, IHasFiles<RealmNamedFileUsage>, ISoftDelete, IEquatable<RealmBeatmapSet>, IBeatmapSetInfo
    {
        public Guid ID { get; set; } = Guid.NewGuid();

        public int? OnlineID { get; set; }

        public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.Now;

        public IBeatmapMetadataInfo Metadata => Beatmaps.First().Metadata;

        public IList<RealmBeatmap> Beatmaps { get; } = new List<RealmBeatmap>();

        [NotNull]
        public IList<RealmNamedFileUsage> Files { get; } = new List<RealmNamedFileUsage>();

        public double MaxStarDifficulty => Beatmaps?.Max(b => b.StarRating) ?? 0;

        public double MaxLength => Beatmaps?.Max(b => b.Length) ?? 0;

        public double MaxBPM => Beatmaps?.Max(b => b.BPM) ?? 0;

        public bool DeletePending { get; set; }

        public string Hash { get; set; }

        public bool Protected { get; set; }

        /// <summary>
        /// Returns the storage path for the file in this beatmapset with the given filename, if any exists, otherwise null.
        /// The path returned is relative to the user file storage.
        /// </summary>
        /// <param name="filename">The name of the file to get the storage path of.</param>
        public string GetPathForFile(string filename) => Files.SingleOrDefault(f => string.Equals(f.Filename, filename, StringComparison.OrdinalIgnoreCase))?.File.StoragePath;

        public override string ToString() => Metadata?.ToString() ?? base.ToString();

        public bool Equals(RealmBeatmapSet other)
        {
            if (other == null)
                return false;

            if (IsManaged && other.IsManaged)
                return ID == other.ID;

            if (OnlineID.HasValue && other.OnlineID.HasValue)
                return OnlineID == other.OnlineID;

            if (!string.IsNullOrEmpty(Hash) && !string.IsNullOrEmpty(other.Hash))
                return Hash == other.Hash;

            return ReferenceEquals(this, other);
        }

        IEnumerable<IBeatmapInfo> IBeatmapSetInfo.Beatmaps => Beatmaps;

        IEnumerable<INamedFileUsage> IBeatmapSetInfo.Files => Files;
    }
}
