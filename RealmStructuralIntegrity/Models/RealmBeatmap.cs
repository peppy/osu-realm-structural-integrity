// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Game.Database;
using Realms;

namespace osu.Game.Models
{
    [ExcludeFromDynamicCompile]
    [Serializable]
    [MapTo("Beatmap")]
    public class RealmBeatmap : RealmObject, IEquatable<RealmBeatmap>, IHasGuidPrimaryKey
    {
        [PrimaryKey]
        public Guid ID { get; set; } = Guid.NewGuid();

        public string Version { get; set; }

        [JsonProperty("id")]
        public int? OnlineBeatmapID { get; set; }

        public RealmBeatmapSet BeatmapSet { get; set; }

        public RealmBeatmapMetadata Metadata { get; set; }

        public RealmBeatmapDifficulty Difficulty { get; set; }

        [NotMapped]
        public int? MaxCombo { get; set; }

        /// <summary>
        /// The playable length in milliseconds of this beatmap.
        /// </summary>
        public double Length { get; set; }

        /// <summary>
        /// The most common BPM of this beatmap.
        /// </summary>
        public double BPM { get; set; }

        public string Path { get; set; }

        [JsonProperty("file_sha2")]
        public string Hash { get; set; }

        public RealmRuleset Ruleset { get; set; }

        [JsonIgnore]
        public bool Hidden { get; set; }

        [JsonProperty("difficulty_rating")]
        public double StarDifficulty { get; set; }

        /// <summary>
        /// MD5 is kept for legacy support (matching against replays, osu-web-10 etc.).
        /// </summary>
        [JsonProperty("file_md5")]
        public string MD5Hash { get; set; }

        // public List<ScoreInfo> LocalScores { get; set; }

        #region Properties we may not want persisted (but also maybe no harm?)

        // General
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

        private string versionString => string.IsNullOrEmpty(Version) ? string.Empty : $"[{Version}]";

        public override string ToString() => $"{Metadata ?? BeatmapSet?.Metadata} {versionString}".Trim();

        public RomanisableString ToRomanisableString()
        {
            var metadata = (Metadata ?? BeatmapSet?.Metadata)?.ToRomanisableString() ?? new RomanisableString(null, null);
            return new RomanisableString($"{metadata.GetPreferred(true)} {versionString}".Trim(), $"{metadata.GetPreferred(false)} {versionString}".Trim());
        }

        public bool Equals(RealmBeatmap other) => ID == other?.ID;

        public bool AudioEquals(RealmBeatmap other) => other != null && BeatmapSet != null && other.BeatmapSet != null &&
                                                      BeatmapSet.Hash == other.BeatmapSet.Hash &&
                                                      (Metadata ?? BeatmapSet.Metadata).AudioFile == (other.Metadata ?? other.BeatmapSet.Metadata).AudioFile;

        public bool BackgroundEquals(RealmBeatmap other) => other != null && BeatmapSet != null && other.BeatmapSet != null &&
                                                           BeatmapSet.Hash == other.BeatmapSet.Hash &&
                                                           (Metadata ?? BeatmapSet.Metadata).BackgroundFile == (other.Metadata ?? other.BeatmapSet.Metadata).BackgroundFile;

        /// <summary>
        /// Returns a shallow-clone of this <see cref="RealmBeatmap"/>.
        /// </summary>
        public RealmBeatmap Clone() => (RealmBeatmap)MemberwiseClone();
    }
}
