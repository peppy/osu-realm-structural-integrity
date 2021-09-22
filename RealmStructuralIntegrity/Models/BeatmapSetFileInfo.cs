// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Database;
using Realms;

namespace osu.Game.Models
{
    public class BeatmapSetFileInfo : RealmObject, INamedFileInfo, IHasGuidPrimaryKey
    {
        public Guid ID { get; set; }

        public FileInfo FileInfo { get; set; }

        public string Filename { get; set; }
    }
}
