// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets;

namespace osu.Game.Models.Interfaces
{
    /// <summary>
    /// A representation of a ruleset's metadata.
    /// </summary>
    public interface IRulesetInfo : IHasOnlineID
    {
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

        public Ruleset? CreateInstance()
        {
            var type = Type.GetType(InstantiationInfo);

            if (type == null)
                return null;

            var ruleset = Activator.CreateInstance(type) as Ruleset;

            // overwrite the pre-populated RulesetInfo with a potentially database attached copy.
            // ruleset.RulesetInfo = this;

            return ruleset;
        }
    }
}
