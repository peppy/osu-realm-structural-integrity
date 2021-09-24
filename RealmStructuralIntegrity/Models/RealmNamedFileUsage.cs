// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Database;
using osu.Game.Models.Interfaces;
using Realms;

namespace osu.Game.Models
{
    public class RealmNamedFileUsage : EmbeddedObject, INamedFile, INamedFileUsage
    {
        public RealmFile File { get; set; }

        public string Filename { get; set; }

        IFileInfo INamedFileUsage.File => File;
    }
}
