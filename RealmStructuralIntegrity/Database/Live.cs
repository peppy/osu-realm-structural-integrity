// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Realms;

#nullable enable

namespace osu.Game.Database
{
    /// <summary>
    /// Provides a method of working with realm objects over longer application lifetimes.
    /// </summary>
    /// <typeparam name="T">The underlying object type.</typeparam>
    public class Live<T>
        where T : RealmObject, IHasGuidPrimaryKey
    {
        /// <summary>
        /// The original live data used to create this instance.
        /// </summary>
        private readonly T data;

        private readonly Guid id;

        private readonly IRealmFactory realm;

        /// <summary>
        /// Construct a new instance of live realm data.
        /// </summary>
        /// <param name="data">The realm data.</param>
        /// <param name="realm">A context factory to allow transfer and re-retrieval over thread contexts.</param>
        public Live(T data, IRealmFactory realm)
        {
            this.data = data;
            this.realm = realm;

            id = data.ID;
        }

        /// <summary>
        /// Perform a write operation on this live object.
        /// </summary>
        /// <param name="perform">The action to perform.</param>
        public TReturn PerformRead<TReturn>(Func<T, TReturn> perform)
        {
            if (data.IsValid && !data.Realm.IsClosed)
                return perform(data);

            using (var usage = realm.GetForRead())
                return perform(usage.Realm.Find<T>(id));
        }

        /// <summary>
        /// Perform a write operation on this live object.
        /// </summary>
        /// <param name="perform">The action to perform.</param>
        public void PerformUpdate(Action<T> perform)
        {
            using (var usage = realm.GetForWrite())
            {
                perform(usage.Realm.Find<T>(id));

                usage.Commit();
            }
        }
    }
}
