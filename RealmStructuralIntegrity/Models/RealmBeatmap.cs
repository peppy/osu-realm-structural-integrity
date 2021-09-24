// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Models.Interfaces;
using Realms;

namespace osu.Game.Models
{
    /// <summary>
    /// A single beatmap difficulty.
    /// </summary>
    [ExcludeFromDynamicCompile]
    [Serializable]
    [MapTo("Beatmap")]
    public class RealmBeatmap : RealmObject, IHasGuidPrimaryKey, IBeatmapInfo
    {
        [PrimaryKey]
        public Guid ID { get; set; } = Guid.NewGuid();

        public string DifficultyName { get; set; }

        public int? OnlineID { get; set; }

        public RealmBeatmapSet BeatmapSet { get; set; }

        public RealmBeatmapDifficulty Difficulty { get; set; }

        public RealmBeatmapMetadata Metadata { get; set; }

        public double Length { get; set; }

        public double BPM { get; set; }

        public string Hash { get; set; }

        public RealmRuleset Ruleset { get; set; }

        public double StarRating { get; set; }

        public string MD5Hash { get; set; }

        [JsonIgnore]
        public bool Hidden { get; set; }

        #region Properties we may not want persisted (but also maybe no harm?)

        public double AudioLeadIn { get; set; }

        public float StackLeniency { get; set; } = 0.7f;

        public bool SpecialStyle { get; set; }

        public bool LetterboxInBreaks { get; set; }

        public bool WidescreenStoryboard { get; set; }

        public bool EpilepsyWarning { get; set; }

        public bool SamplesMatchPlaybackRate { get; set; }

        public double DistanceSpacing { get; set; }

        public int BeatDivisor { get; set; }

        public int GridSize { get; set; }

        public double TimelineZoom { get; set; }

        #endregion

        /// <summary>
        /// Returns a shallow-clone of this <see cref="RealmBeatmap"/>.
        /// </summary>
        public RealmBeatmap Clone() => (RealmBeatmap)MemberwiseClone();

        public bool AudioEquals(RealmBeatmap other) => other != null
                                                       && BeatmapSet != null
                                                       && other.BeatmapSet != null
                                                       && BeatmapSet.Hash == other.BeatmapSet.Hash
                                                       && (Metadata ?? BeatmapSet.Metadata).AudioFile == (other.Metadata ?? other.BeatmapSet.Metadata).AudioFile;

        public bool BackgroundEquals(RealmBeatmap other) => other != null
                                                            && BeatmapSet != null
                                                            && other.BeatmapSet != null
                                                            && BeatmapSet.Hash == other.BeatmapSet.Hash
                                                            && (Metadata ?? BeatmapSet.Metadata).BackgroundFile == (other.Metadata ?? other.BeatmapSet.Metadata).BackgroundFile;

        IBeatmapMetadataInfo IBeatmapInfo.Metadata => Metadata;
        IRulesetInfo IBeatmapInfo.Ruleset => Ruleset;
        IBeatmapDifficultyInfo IBeatmapInfo.Difficulty => Difficulty;
    }
}
