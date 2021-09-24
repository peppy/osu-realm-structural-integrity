// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Testing;
using osu.Game.Database;
using Realms;

namespace osu.Game.Models
{
    [ExcludeFromDynamicCompile]
    [MapTo("BeatmapSet")]
    public class RealmBeatmapSet : RealmObject, IHasGuidPrimaryKey, IHasFiles<RealmBeatmapSetFile>, ISoftDelete, IEquatable<RealmBeatmapSet>
    {
        public Guid ID { get; set; } = Guid.NewGuid();

        public int? OnlineBeatmapSetID { get; set; }

        public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.Now;

        public RealmBeatmapMetadata Metadata { get; set; }

        public IList<RealmBeatmap> Beatmaps { get; } = new List<RealmBeatmap>();

        [NotNull]
        public IList<RealmBeatmapSetFile> Files { get; } = new List<RealmBeatmapSetFile>();

        /// <summary>
        /// The maximum star difficulty of all beatmaps in this set.
        /// </summary>
        public double MaxStarDifficulty => Beatmaps?.Max(b => b.StarDifficulty) ?? 0;

        /// <summary>
        /// The maximum playable length in milliseconds of all beatmaps in this set.
        /// </summary>
        public double MaxLength => Beatmaps?.Max(b => b.Length) ?? 0;

        /// <summary>
        /// The maximum BPM of all beatmaps in this set.
        /// </summary>
        public double MaxBPM => Beatmaps?.Max(b => b.BPM) ?? 0;

        [NotMapped]
        public bool DeletePending { get; set; }

        public string Hash { get; set; }

        public string StoryboardFile => Files.FirstOrDefault(f => f.Filename.EndsWith(".osb", StringComparison.OrdinalIgnoreCase))?.Filename;

        /// <summary>
        /// Returns the storage path for the file in this beatmapset with the given filename, if any exists, otherwise null.
        /// The path returned is relative to the user file storage.
        /// </summary>
        /// <param name="filename">The name of the file to get the storage path of.</param>
        public string GetPathForFile(string filename) => Files.SingleOrDefault(f => string.Equals(f.Filename, filename, StringComparison.OrdinalIgnoreCase))?.File.StoragePath;

        public override string ToString() => Metadata?.ToString() ?? base.ToString();

        public bool Protected { get; set; }

        public bool Equals(RealmBeatmapSet other)
        {
            if (other == null)
                return false;

            if (IsManaged && other.IsManaged)
                return ID == other.ID;

            if (OnlineBeatmapSetID.HasValue && other.OnlineBeatmapSetID.HasValue)
                return OnlineBeatmapSetID == other.OnlineBeatmapSetID;

            if (!string.IsNullOrEmpty(Hash) && !string.IsNullOrEmpty(other.Hash))
                return Hash == other.Hash;

            return ReferenceEquals(this, other);
        }
    }
}
