// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Models.Interfaces
{
    /// <summary>
    /// A representation of a ruleset's metadata.
    /// </summary>
    public interface IRulesetInfo
    {
        /// <summary>
        /// The server-side `ruleset_id` representing this ruleset, if one exists.
        /// </summary>
        int? OnlineID { get; }

        /// <summary>
        /// The user-exposed name of this ruleset.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// An acronym defined by the ruleset that can be used as a permanent identifier.
        /// </summary>
        string ShortName { get; }

        /// <summary>
        /// A string representation of this ruleset, to be used with reflection to instantiate the ruleset represented by this metadata.
        /// </summary>
        string InstantiationInfo { get; }
    }
}
