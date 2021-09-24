// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Localisation;

namespace osu.Game.Models.Interfaces
{
    public interface IBeatmapMetadataInfo : IEquatable<IBeatmapMetadataInfo>
    {
        string Title { get; }

        string TitleUnicode { get; }

        string Artist { get; }

        string ArtistUnicode { get; }

        string Author { get; } // eventually should be linked to a persisted User.

        string Source { get; }

        string Tags { get; }

        /// <summary>
        /// The time in milliseconds to begin playing the track for preview purposes.
        /// If -1, the track should begin playing at 40% of its length.
        /// </summary>
        int PreviewTime { get; }

        /// <summary>
        /// The filename of the audio file consumed by this beatmap.
        /// </summary>
        string AudioFile { get; }

        /// <summary>
        /// The filename of the background image file consumed by this beatmap.
        /// </summary>
        string BackgroundFile { get; }

        string DisplayTitle
        {
            get
            {
                string author = Author == null ? string.Empty : $"({Author})";
                return $"{Artist} - {Title} {author}".Trim();
            }
        }

        RomanisableString DisplayTitleRomanisable
        {
            get
            {
                string author = Author == null ? string.Empty : $"({Author})";
                var artistUnicode = string.IsNullOrEmpty(ArtistUnicode) ? Artist : ArtistUnicode;
                var titleUnicode = string.IsNullOrEmpty(TitleUnicode) ? Title : TitleUnicode;

                return new RomanisableString($"{artistUnicode} - {titleUnicode} {author}".Trim(), $"{Artist} - {Title} {author}".Trim());
            }
        }

        string[] SearchableTerms => new[]
        {
            Author,
            Artist,
            ArtistUnicode,
            Title,
            TitleUnicode,
            Source,
            Tags
        }.Where(s => !string.IsNullOrEmpty(s)).ToArray();

        bool IEquatable<IBeatmapMetadataInfo>.Equals(IBeatmapMetadataInfo other)
        {
            if (other == null)
                return false;

            return Title == other.Title
                   && TitleUnicode == other.TitleUnicode
                   && Artist == other.Artist
                   && ArtistUnicode == other.ArtistUnicode
                   && Author == other.Author
                   && Source == other.Source
                   && Tags == other.Tags
                   && PreviewTime == other.PreviewTime
                   && AudioFile == other.AudioFile
                   && BackgroundFile == other.BackgroundFile;
        }
    }
}
