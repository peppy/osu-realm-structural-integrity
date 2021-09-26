// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Realms;

namespace osu.Game.Database
{
    public static class RealmObjectExtensions
    {
        public static Live<T> ToLive<T>(this T realmObject)
            where T : RealmObject, IHasGuidPrimaryKey
        {
            return new Live<T>(realmObject);
        }
    }
}
