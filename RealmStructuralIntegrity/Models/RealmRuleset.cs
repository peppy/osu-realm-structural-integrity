// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using osu.Framework.Testing;
using osu.Game.Models.Interfaces;
using Realms;

namespace osu.Game.Models
{
    [ExcludeFromDynamicCompile]
    [MapTo("Ruleset")]
    public class RealmRuleset : RealmObject, IEquatable<RealmRuleset>, IRulesetInfo
    {
        [PrimaryKey]
        public int? OnlineID { get; set; }

        public string Name { get; set; }

        public string ShortName { get; set; }

        public string InstantiationInfo { get; set; }

        [JsonIgnore]
        public bool Available { get; set; }

        public bool Equals(RealmRuleset other) => other != null && OnlineID == other.OnlineID && Available == other.Available && Name == other.Name && InstantiationInfo == other.InstantiationInfo;

        public override bool Equals(object obj) => obj is RealmRuleset rulesetInfo && Equals(rulesetInfo);

        [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = OnlineID.HasValue ? OnlineID.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (InstantiationInfo != null ? InstantiationInfo.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Available.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString() => Name ?? $"{Name} ({ShortName}) ID: {OnlineID}";

        public RealmRuleset Clone() => new RealmRuleset
        {
            OnlineID = OnlineID,
            Name = Name,
            ShortName = ShortName,
            InstantiationInfo = InstantiationInfo,
            Available = Available
        };
    }
}
