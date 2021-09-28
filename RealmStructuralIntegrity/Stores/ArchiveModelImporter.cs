// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using osu.Framework.Extensions;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Database;
using osu.Game.IO.Archives;
using osu.Game.Models;
using Realms;

namespace osu.Game.Stores
{
    /// <summary>
    /// Encapsulates a model store class to give it import functionality.
    /// Adds cross-functionality with <see cref="FileStore"/> to give access to the central file store for the provided model.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    public abstract class ArchiveModelImporter<TModel>
        where TModel : RealmObject, IHasRealmFiles, IHasGuidPrimaryKey, ISoftDelete
    {
        private const int import_queue_request_concurrency = 1;

        /// <summary>
        /// The size of a batch import operation before considering it a lower priority operation.
        /// </summary>
        private const int low_priority_import_batch_size = 1;

        /// <summary>
        /// A singleton scheduler shared by all <see cref="ArchiveModelImporter{TModel}"/>.
        /// </summary>
        /// <remarks>
        /// This scheduler generally performs IO and CPU intensive work so concurrency is limited harshly.
        /// It is mainly being used as a queue mechanism for large imports.
        /// </remarks>
        private static readonly ThreadedTaskScheduler import_scheduler = new ThreadedTaskScheduler(import_queue_request_concurrency, nameof(ArchiveModelImporter<TModel>));

        /// <summary>
        /// A second scheduler for lower priority imports.
        /// For simplicity, these will just run in parallel with normal priority imports, but a future refactor would see this implemented via a custom scheduler/queue.
        /// See https://gist.github.com/peppy/f0e118a14751fc832ca30dd48ba3876b for an incomplete version of this.
        /// </summary>
        private static readonly ThreadedTaskScheduler import_scheduler_low_priority = new ThreadedTaskScheduler(import_queue_request_concurrency, nameof(ArchiveModelImporter<TModel>));

        public virtual IEnumerable<string> HandledExtensions => new[] { @".zip" };

        protected readonly FileStore Files;

        protected readonly RealmContextFactory ContextFactory;

        /// <summary>
        /// Fired when the user requests to view the resulting import.
        /// </summary>
        public Action<IEnumerable<TModel>>? PresentImport;

        protected ArchiveModelImporter(Storage storage, RealmContextFactory contextFactory)
        {
            ContextFactory = contextFactory;

            Files = new FileStore(contextFactory, storage);
        }

        /// <summary>
        /// Silently import an item from an <see cref="ArchiveReader"/>.
        /// </summary>
        /// <param name="archive">The archive to be imported.</param>
        /// <param name="lowPriority">Whether this is a low priority import.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        public Task<TModel?> Import(ArchiveReader archive, bool lowPriority = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TModel? model = null;

            try
            {
                model = CreateModel(archive);

                if (model == null)
                    return Task.FromResult<TModel?>(null);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                LogForModel(model, @$"Model creation of {archive.Name} failed.", e);
                return Task.FromResult<TModel?>(null);
            }

            return Import(model, archive, lowPriority, cancellationToken);
        }

        /// <summary>
        /// Any file extensions which should be included in hash creation.
        /// Generally should include all file types which determine the file's uniqueness.
        /// Large files should be avoided if possible.
        /// </summary>
        /// <remarks>
        /// This is only used by the default hash implementation. If <see cref="ComputeHash"/> is overridden, it will not be used.
        /// </remarks>
        protected abstract string[] HashableFileTypes { get; }

        internal static void LogForModel(TModel? model, string message, Exception? e = null)
        {
            string prefix = $"[{(model?.Hash ?? "?????").Substring(0, 5)}]";

            if (e != null)
                Logger.Error(e, $"{prefix} {message}", LoggingTarget.Database);
            else
                Logger.Log($"{prefix} {message}", LoggingTarget.Database);
        }

        /// <summary>
        /// Whether the implementation overrides <see cref="ComputeHash"/> with a custom implementation.
        /// Custom hash implementations must bypass the early exit in the import flow (see <see cref="computeHashFast"/> usage).
        /// </summary>
        protected virtual bool HasCustomHashFunction => false;

        /// <summary>
        /// Create a SHA-2 hash from the provided archive based on file content of all files matching <see cref="HashableFileTypes"/>.
        /// </summary>
        /// <remarks>
        ///  In the case of no matching files, a hash will be generated from the passed archive's <see cref="ArchiveReader.Name"/>.
        /// </remarks>
        protected virtual string ComputeHash(TModel item, ArchiveReader? reader = null)
        {
            if (reader != null)
                // fast hashing for cases where the item's files may not be populated.
                return computeHashFast(reader);

            // for now, concatenate all hashable files in the set to create a unique hash.
            MemoryStream hashable = new MemoryStream();

            foreach (RealmNamedFileUsage file in item.Files.Where(f => HashableFileTypes.Any(ext => f.Filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).OrderBy(f => f.Filename))
            {
                using (Stream s = Files.Store.GetStream(file.File.StoragePath))
                    s.CopyTo(hashable);
            }

            if (hashable.Length > 0)
                return hashable.ComputeSHA2Hash();

            return item.Hash;
        }

        /// <summary>
        /// Silently import an item from a <typeparamref name="TModel"/>.
        /// </summary>
        /// <param name="item">The model to be imported.</param>
        /// <param name="archive">An optional archive to use for model population.</param>
        /// <param name="lowPriority">Whether this is a low priority import.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        public virtual async Task<TModel?> Import(TModel item, ArchiveReader? archive = null, bool lowPriority = false, CancellationToken cancellationToken = default) => await Task.Factory.StartNew(async () =>
        {
            using (var realm = ContextFactory.CreateContext())
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool checkedExisting = false;
                TModel? existing = null;

                if (archive != null && !HasCustomHashFunction)
                {
                    // this is a fast bail condition to improve large import performance.
                    item.Hash = computeHashFast(archive);

                    checkedExisting = true;
                    existing = CheckForExisting(item, realm);

                    if (existing != null)
                    {
                        // bare minimum comparisons
                        //
                        // note that this should really be checking filesizes on disk (of existing files) for some degree of sanity.
                        // or alternatively doing a faster hash check. either of these require database changes and reprocessing of existing files.
                        if (CanSkipImport(existing, item) &&
                            getFilenames(existing.Files).SequenceEqual(getShortenedFilenames(archive).Select(p => p.shortened).OrderBy(f => f)))
                        {
                            LogForModel(item, @$"Found existing (optimised) {HumanisedModelName} for {item} (ID {existing.ID}) – skipping import.");

                            using (var transaction = realm.BeginWrite())
                            {
                                existing.DeletePending = false;
                                transaction.Commit();
                            }

                            return existing;
                        }

                        LogForModel(item, @"Found existing (optimised) but failed pre-check.");
                    }
                }

                delayEvents();

                try
                {
                    LogForModel(item, @"Beginning import...");

                    // TODO: do we want to make the transaction this local? not 100% sure, will need further investigation.
                    using (var transaction = realm.BeginWrite())
                    {
                        if (archive != null)
                            // TODO: look into rollback of file additions (or delayed commit).
                            item.Files.AddRange(createFileInfos(archive, Files, realm));

                        item.Hash = ComputeHash(item, archive);

                        // TODO: we may want to run this outside of the transaction.
                        await Populate(item, archive, realm, cancellationToken).ConfigureAwait(false);

                        if (!checkedExisting)
                            existing = CheckForExisting(item, realm);

                        if (existing != null)
                        {
                            if (CanReuseExisting(existing, item))
                            {
                                LogForModel(item, @$"Found existing {HumanisedModelName} for {item} (ID {existing.ID}) – skipping import.");
                                existing.DeletePending = false;

                                flushEvents(true);
                                return existing;
                            }

                            LogForModel(item, @"Found existing but failed re-use check.");

                            existing.DeletePending = true;

                            // todo: actually delete? i don't think this is required...
                            // ModelStore.PurgeDeletable(s => s.ID == existing.ID);
                        }

                        PreImport(item, realm);

                        // import to store
                        realm.Add(item);

                        transaction.Commit();
                    }

                    LogForModel(item, @"Import successfully completed!");
                }
                catch (Exception e)
                {
                    if (!(e is TaskCanceledException))
                        LogForModel(item, @"Database import or population failed and has been rolled back.", e);

                    flushEvents(false);
                    throw;
                }

                flushEvents(true);
                return item;
            }
        }, cancellationToken, TaskCreationOptions.HideScheduler, lowPriority ? import_scheduler_low_priority : import_scheduler).Unwrap().ConfigureAwait(false);

        private string computeHashFast(ArchiveReader reader)
        {
            MemoryStream hashable = new MemoryStream();

            foreach (var file in reader.Filenames.Where(f => HashableFileTypes.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).OrderBy(f => f))
            {
                using (Stream s = reader.GetStream(file))
                    s.CopyTo(hashable);
            }

            if (hashable.Length > 0)
                return hashable.ComputeSHA2Hash();

            return reader.Name.ComputeSHA2Hash();
        }

        /// <summary>
        /// Create all required <see cref="File"/>s for the provided archive, adding them to the global file store.
        /// </summary>
        private List<RealmNamedFileUsage> createFileInfos(ArchiveReader reader, FileStore files, Realm realm)
        {
            var fileInfos = new List<RealmNamedFileUsage>();

            // import files to manager
            foreach (var filenames in getShortenedFilenames(reader))
            {
                using (Stream s = reader.GetStream(filenames.original))
                {
                    var item = new RealmNamedFileUsage(files.Add(s, realm), filenames.shortened);
                    fileInfos.Add(item);
                }
            }

            return fileInfos;
        }

        private IEnumerable<(string original, string shortened)> getShortenedFilenames(ArchiveReader reader)
        {
            string prefix = reader.Filenames.GetCommonPrefix();
            if (!(prefix.EndsWith('/') || prefix.EndsWith('\\')))
                prefix = string.Empty;

            // import files to manager
            foreach (string file in reader.Filenames)
                yield return (file, file.Substring(prefix.Length).ToStandardisedPath());
        }

        /// <summary>
        /// Create a barebones model from the provided archive.
        /// Actual expensive population should be done in <see cref="Populate"/>; this should just prepare for duplicate checking.
        /// </summary>
        /// <param name="archive">The archive to create the model for.</param>
        /// <returns>A model populated with minimal information. Returning a null will abort importing silently.</returns>
        protected abstract TModel? CreateModel(ArchiveReader archive);

        /// <summary>
        /// Populate the provided model completely from the given archive.
        /// After this method, the model should be in a state ready to commit to a store.
        /// </summary>
        /// <param name="model">The model to populate.</param>
        /// <param name="archive">The archive to use as a reference for population. May be null.</param>
        /// <param name="realm">The current realm context.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        protected abstract Task Populate(TModel model, ArchiveReader? archive, Realm realm, CancellationToken cancellationToken = default);

        /// <summary>
        /// Perform any final actions before the import to database executes.
        /// </summary>
        /// <param name="model">The model prepared for import.</param>
        /// <param name="realm">The current realm context.</param>
        protected virtual void PreImport(TModel model, Realm realm)
        {
        }

        /// <summary>
        /// Check whether an existing model already exists for a new import item.
        /// </summary>
        /// <param name="model">The new model proposed for import.</param>
        /// <param name="realm">The current realm context.</param>
        /// <returns>An existing model which matches the criteria to skip importing, else null.</returns>
        protected TModel? CheckForExisting(TModel model, Realm realm) => string.IsNullOrEmpty(model.Hash) ? null : realm.All<TModel>().FirstOrDefault(b => b.Hash == model.Hash);

        /// <summary>
        /// Whether import can be skipped after finding an existing import early in the process.
        /// Only valid when <see cref="ComputeHash"/> is not overridden.
        /// </summary>
        /// <param name="existing">The existing model.</param>
        /// <param name="import">The newly imported model.</param>
        /// <returns>Whether to skip this import completely.</returns>
        protected virtual bool CanSkipImport(TModel existing, TModel import) => true;

        /// <summary>
        /// After an existing <typeparamref name="TModel"/> is found during an import process, the default behaviour is to use/restore the existing
        /// item and skip the import. This method allows changing that behaviour.
        /// </summary>
        /// <param name="existing">The existing model.</param>
        /// <param name="import">The newly imported model.</param>
        /// <returns>Whether the existing model should be restored and used. Returning false will delete the existing and force a re-import.</returns>
        protected virtual bool CanReuseExisting(TModel existing, TModel import) =>
            // for the best or worst, we copy and import files of a new import before checking whether
            // it is a duplicate. so to check if anything has changed, we can just compare all File IDs.
            getIDs(existing.Files).SequenceEqual(getIDs(import.Files)) &&
            getFilenames(existing.Files).SequenceEqual(getFilenames(import.Files));

        private IEnumerable<string> getIDs(IEnumerable<INamedFile> files)
        {
            foreach (var f in files.OrderBy(f => f.Filename))
                yield return f.File.Hash;
        }

        private IEnumerable<string> getFilenames(IEnumerable<INamedFile> files)
        {
            foreach (var f in files.OrderBy(f => f.Filename))
                yield return f.Filename;
        }

        protected virtual string HumanisedModelName => $"{typeof(TModel).Name.Replace(@"Info", "").ToLower()}";

        #region Event handling / delaying

        private readonly List<Action> queuedEvents = new List<Action>();

        /// <summary>
        /// Allows delaying of outwards events until an operation is confirmed (at a database level).
        /// </summary>
        private bool delayingEvents;

        /// <summary>
        /// Begin delaying outwards events.
        /// </summary>
        private void delayEvents() => delayingEvents = true;

        /// <summary>
        /// Flush delayed events and disable delaying.
        /// </summary>
        /// <param name="perform">Whether the flushed events should be performed.</param>
        private void flushEvents(bool perform)
        {
            Action[] events;

            lock (queuedEvents)
            {
                events = queuedEvents.ToArray();
                queuedEvents.Clear();
            }

            if (perform)
            {
                foreach (var a in events)
                    a.Invoke();
            }

            delayingEvents = false;
        }

        private void handleEvent(Action a)
        {
            if (delayingEvents)
            {
                lock (queuedEvents)
                    queuedEvents.Add(a);
            }
            else
                a.Invoke();
        }

        #endregion

        private string getValidFilename(string filename)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                filename = filename.Replace(c, '_');
            return filename;
        }
    }
}
