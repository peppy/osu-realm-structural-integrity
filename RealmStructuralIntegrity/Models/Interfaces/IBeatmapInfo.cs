// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;

namespace osu.Game.Models.Interfaces
{
    /// <summary>
    /// A single beatmap difficulty.
    /// </summary>
    public interface IBeatmapInfo
    {
        /// <summary>
        /// The user-specified name given to this beatmap.
        /// </summary>
        string DifficultyName { get; }

        /// <summary>
        /// The server-side `beatmap_id` representing this beatmap, if one exists.
        /// </summary>
        int? OnlineID { get; }

        /// <summary>
        /// The metadata representing this beatmap. May be shared between multiple beatmaps.
        /// </summary>
        IBeatmapMetadataInfo Metadata { get; }

        /// <summary>
        /// The difficulty settings for this beatmap.
        /// </summary>
        IBeatmapDifficultyInfo Difficulty { get; }

        /// <summary>
        /// The playable length in milliseconds of this beatmap.
        /// </summary>
        double Length { get; }

        /// <summary>
        /// The most common BPM of this beatmap.
        /// </summary>
        double BPM { get; }

        /// <summary>
        /// The SHA-256 hash representing this beatmap's contents.
        /// </summary>
        string Hash { get; }

        /// <summary>
        /// MD5 is kept for legacy support (matching against replays, osu-web-10 etc.).
        /// </summary>
        string MD5Hash { get; }

        /// <summary>
        /// The ruleset this beatmap was made for.
        /// </summary>
        IRulesetInfo Ruleset { get; }

        /// <summary>
        /// The basic star rating for this beatmap (with no mods applied).
        /// </summary>
        double StarRating { get; }

        string DisplayTitle => $"{Metadata} {versionString}".Trim();

        RomanisableString DisplayTitleRomanisable
        {
            get
            {
                var metadata = Metadata.DisplayTitleRomanisable;

                return new RomanisableString($"{metadata.GetPreferred(true)} {versionString}".Trim(), $"{metadata.GetPreferred(false)} {versionString}".Trim());
            }
        }

        private string versionString => string.IsNullOrEmpty(DifficultyName) ? string.Empty : $"[{DifficultyName}]";
    }
}
