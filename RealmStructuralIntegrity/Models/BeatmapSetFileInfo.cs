// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Database;
using Realms;

namespace osu.Game.Models
{
    public class BeatmapSetFileInfo : EmbeddedObject, INamedFileInfo
    {
        public FileInfo FileInfo { get; set; }

        public string Filename { get; set; }
    }
}
