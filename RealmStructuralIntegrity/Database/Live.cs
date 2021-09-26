// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
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

        private readonly SynchronizationContext? fetchedContext;
        private readonly int fetchedThreadId;

        /// <summary>
        /// Construct a new instance of live realm data.
        /// </summary>
        /// <param name="data">The realm data.</param>
        public Live(T data)
        {
            this.data = data;

            fetchedContext = SynchronizationContext.Current;
            fetchedThreadId = Thread.CurrentThread.ManagedThreadId;

            id = data.ID;
        }

        /// <summary>
        /// Perform a read operation on this live object.
        /// </summary>
        /// <param name="perform">The action to perform.</param>
        public void PerformRead(Action<T> perform)
        {
            if (isCorrectThread && data.IsValid && !data.Realm.IsClosed)
            {
                perform(data);
                return;
            }

            using (var realm = Realm.GetInstance(data.Realm.Config))
                perform(realm.Find<T>(id));
        }

        /// <summary>
        /// Perform a read operation on this live object.
        /// </summary>
        /// <param name="perform">The action to perform.</param>
        public TReturn PerformRead<TReturn>(Func<T, TReturn> perform)
        {
            if (typeof(TReturn).IsAssignableTo(typeof(RealmObjectBase)))
                throw new InvalidOperationException($"Realm live objects should not exit the scope of {nameof(PerformRead)}.");

            if (isCorrectThread && data.IsValid && !data.Realm.IsClosed)
                return perform(data);

            using (var realm = Realm.GetInstance(data.Realm.Config))
                return perform(realm.Find<T>(id));
        }

        /// <summary>
        /// Perform a write operation on this live object.
        /// </summary>
        /// <param name="perform">The action to perform.</param>
        public void PerformWrite(Action<T> perform) =>
            PerformRead(t =>
            {
                var transaction = t.Realm.BeginWrite();
                perform(t);
                transaction.Commit();
            });

        // this matches realm's internal thread validation (see https://github.com/realm/realm-dotnet/blob/903b4d0b304f887e37e2d905384fb572a6496e70/Realm/Realm/Native/SynchronizationContextScheduler.cs#L72)
        private bool isCorrectThread
            => (fetchedContext != null && SynchronizationContext.Current == fetchedContext) || fetchedThreadId == Thread.CurrentThread.ManagedThreadId;
    }
}
