// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Models.Interfaces
{
    /// <summary>
    /// A representation of a collection of beatmap difficulties, generally packaged as an ".osz" archive.
    /// </summary>
    public interface IBeatmapSetInfo
    {
        /// <summary>
        /// The server-side `beatmap_set_id` representing this set, if one exists.
        /// </summary>
        int? OnlineID { get; }

        /// <summary>
        /// The date when this beatmap was imported.
        /// </summary>
        DateTimeOffset DateAdded { get; }

        /// <summary>
        /// The metadata representing this set.
        /// </summary>
        IBeatmapMetadataInfo Metadata { get; }

        /// <summary>
        /// All beatmaps contained in this set.
        /// </summary>
        IEnumerable<IBeatmapInfo> Beatmaps { get; }

        /// <summary>
        /// All files used by this set.
        /// </summary>
        IEnumerable<INamedFileUsage> Files { get; }

        /// <summary>
        /// The maximum star difficulty of all beatmaps in this set.
        /// </summary>
        double MaxStarDifficulty { get; }

        /// <summary>
        /// The maximum playable length in milliseconds of all beatmaps in this set.
        /// </summary>
        double MaxLength { get; }

        /// <summary>
        /// The maximum BPM of all beatmaps in this set.
        /// </summary>
        double MaxBPM { get; }

        /// <summary>
        /// The filename for the storyboard.
        /// </summary>
        string StoryboardFile => Files.FirstOrDefault(f => f.Filename.EndsWith(".osb", StringComparison.OrdinalIgnoreCase))?.Filename;
    }
}
