// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Realms;

namespace osu.Game.Database
{
    public interface IRealmFactory
    {
        /// <summary>
        /// The main realm context, bound to the update thread.
        /// </summary>
        Realm Context { get; }

        /// <summary>
        /// Create a new realm context for use on an arbitrary thread.
        /// </summary>
        /// <remarks>
        /// This should not be used from the update thread. Use <see cref="Context"/> instead.
        /// </remarks>
        Realm CreateContext();
    }
}
