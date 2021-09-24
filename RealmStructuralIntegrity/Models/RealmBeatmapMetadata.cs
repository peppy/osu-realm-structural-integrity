// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Framework.Testing;
using osu.Game.Models.Interfaces;
using Realms;

namespace osu.Game.Models
{
    [ExcludeFromDynamicCompile]
    [Serializable]
    [MapTo("BeatmapMetadata")]
    public class RealmBeatmapMetadata : RealmObject, IBeatmapMetadataInfo
    {
        public string Title { get; set; }

        [JsonProperty("title_unicode")]
        public string TitleUnicode { get; set; }

        public string Artist { get; set; }

        [JsonProperty("artist_unicode")]
        public string ArtistUnicode { get; set; }

        public string Author { get; set; } // eventually should be linked to a persisted User.

        public string Source { get; set; }

        [JsonProperty(@"tags")]
        public string Tags { get; set; }

        /// <summary>
        /// The time in milliseconds to begin playing the track for preview purposes.
        /// If -1, the track should begin playing at 40% of its length.
        /// </summary>
        public int PreviewTime { get; set; }

        public string AudioFile { get; set; }
        public string BackgroundFile { get; set; }
    }
}
