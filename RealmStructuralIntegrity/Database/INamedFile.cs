﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Models;

namespace osu.Game.Database
{
    /// <summary>
    /// Represent a join model which gives a filename and scope to a <see cref="File"/>.
    /// </summary>
    public interface INamedFile
    {
        public string Filename { get; set; }

        public RealmFile File { get; set; }
    }
}
