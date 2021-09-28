// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Models.Interfaces;
using osu.Game.Rulesets.Objects;
using osu.Game.Skinning;
using Realms;

namespace osu.Game.Stores
{
    /// <summary>
    /// Handles the storage and retrieval of Beatmaps/WorkingBeatmaps.
    /// </summary>
    [ExcludeFromDynamicCompile]
    public class BeatmapImporter : ArchiveModelImporter<RealmBeatmapSet>, IDisposable
    {
        public override IEnumerable<string> HandledExtensions => new[] { ".osz" };

        protected override string[] HashableFileTypes => new[] { ".osu" };

        // protected override string ImportFromStablePath => ".";
        //
        // protected override Storage PrepareStableStorage(StableStorage stableStorage) => stableStorage.GetSongStorage();
        //
        // protected override bool ShouldDeleteArchive(string path) => Path.GetExtension(path)?.ToLowerInvariant() == ".osz";
        //
        // protected override bool CheckLocalAvailability(RealmBeatmapSet model, System.Linq.IQueryable<RealmBeatmapSet> items)
        //     => base.CheckLocalAvailability(model, items) || (model.OnlineID != null && items.Any(b => b.OnlineID == model.OnlineID));

        private readonly dynamic? onlineLookupQueue = null; // todo: BeatmapOnlineLookupQueue is private

        public BeatmapImporter(Storage storage, RealmContextFactory contextFactory, bool performOnlineLookups = false)
            : base(storage, contextFactory)
        {
            if (performOnlineLookups)
            {
                // onlineLookupQueue = new BeatmapOnlineLookupQueue(api, storage);
            }
        }

        protected override async Task Populate(RealmBeatmapSet beatmapSet, ArchiveReader? archive, Realm realm, CancellationToken cancellationToken = default)
        {
            if (archive != null)
                beatmapSet.Beatmaps.AddRange(createBeatmapDifficulties(beatmapSet.Files, realm));

            foreach (RealmBeatmap b in beatmapSet.Beatmaps)
                b.BeatmapSet = beatmapSet;

            validateOnlineIds(beatmapSet, realm);

            bool hadOnlineBeatmapIDs = beatmapSet.Beatmaps.Any(b => b.OnlineID > 0);

            if (onlineLookupQueue != null)
                await onlineLookupQueue.UpdateAsync(beatmapSet, cancellationToken).ConfigureAwait(false);

            // ensure at least one beatmap was able to retrieve or keep an online ID, else drop the set ID.
            if (hadOnlineBeatmapIDs && !beatmapSet.Beatmaps.Any(b => b.OnlineID > 0))
            {
                if (beatmapSet.OnlineID != null)
                {
                    beatmapSet.OnlineID = null;
                    LogForModel(beatmapSet, "Disassociating beatmap set ID due to loss of all beatmap IDs");
                }
            }
        }

        protected override void PreImport(RealmBeatmapSet beatmapSet, Realm realm)
        {
            // if (beatmapSet.Beatmaps.Any(b => b.Difficulty == null))
            //     throw new InvalidOperationException($"Cannot import {nameof(IBeatmapInfo)} with null {nameof(IBeatmapInfo.Difficulty)}.");

            // check if a set already exists with the same online id, delete if it does.
            if (beatmapSet.OnlineID != null)
            {
                var existingOnlineId = realm.All<RealmBeatmapSet>().FirstOrDefault(b => b.OnlineID == beatmapSet.OnlineID);

                if (existingOnlineId != null)
                {
                    existingOnlineId.DeletePending = true;

                    // in order to avoid a unique key constraint, immediately remove the online ID from the previous set.
                    existingOnlineId.OnlineID = null;
                    foreach (var b in existingOnlineId.Beatmaps)
                        b.OnlineID = null;

                    LogForModel(beatmapSet, $"Found existing beatmap set with same OnlineID ({beatmapSet.OnlineID}). It has been deleted.");
                }
            }
        }

        private void validateOnlineIds(RealmBeatmapSet beatmapSet, Realm realm)
        {
            var beatmapIds = beatmapSet.Beatmaps.Where(b => b.OnlineID.HasValue).Select(b => b.OnlineID).ToList();

            // ensure all IDs are unique
            if (beatmapIds.GroupBy(b => b).Any(g => g.Count() > 1))
            {
                LogForModel(beatmapSet, "Found non-unique IDs, resetting...");
                resetIds();
                return;
            }

            // find any existing beatmaps in the database that have matching online ids
            List<RealmBeatmap> existingBeatmaps = new List<RealmBeatmap>();

            foreach (var id in beatmapIds)
                existingBeatmaps.AddRange(realm.All<RealmBeatmap>().Where(b => b.OnlineID == id));

            if (existingBeatmaps.Any())
            {
                // reset the import ids (to force a re-fetch) *unless* they match the candidate CheckForExisting set.
                // we can ignore the case where the new ids are contained by the CheckForExisting set as it will either be used (import skipped) or deleted.

                var existing = CheckForExisting(beatmapSet, realm);

                if (existing == null || existingBeatmaps.Any(b => !existing.Beatmaps.Contains(b)))
                {
                    LogForModel(beatmapSet, "Found existing import with online IDs already, resetting...");
                    resetIds();
                }
            }

            void resetIds() => beatmapSet.Beatmaps.ForEach(b => b.OnlineID = null);
        }

        protected override bool CanSkipImport(RealmBeatmapSet existing, RealmBeatmapSet import)
        {
            if (!base.CanSkipImport(existing, import))
                return false;

            return existing.Beatmaps.Any(b => b.OnlineID != null);
        }

        protected override bool CanReuseExisting(RealmBeatmapSet existing, RealmBeatmapSet import)
        {
            if (!base.CanReuseExisting(existing, import))
                return false;

            var existingIds = existing.Beatmaps.Select(b => b.OnlineID).OrderBy(i => i);
            var importIds = import.Beatmaps.Select(b => b.OnlineID).OrderBy(i => i);

            // force re-import if we are not in a sane state.
            return existing.OnlineID == import.OnlineID && existingIds.SequenceEqual(importIds);
        }

        protected override string HumanisedModelName => "beatmap";

        protected override RealmBeatmapSet? CreateModel(ArchiveReader reader)
        {
            // let's make sure there are actually .osu files to import.
            string? mapName = reader.Filenames.FirstOrDefault(f => f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(mapName))
            {
                Logger.Log($"No beatmap files found in the beatmap archive ({reader.Name}).", LoggingTarget.Database);
                return null;
            }

            Beatmap beatmap;
            using (var stream = new LineBufferedReader(reader.GetStream(mapName)))
                beatmap = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

            return new RealmBeatmapSet
            {
                OnlineID = beatmap.BeatmapInfo.BeatmapSet?.OnlineBeatmapSetID,
                // Metadata = beatmap.Metadata,
                DateAdded = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Create all required <see cref="RealmBeatmap"/>s for the provided archive.
        /// </summary>
        private List<RealmBeatmap> createBeatmapDifficulties(IList<RealmNamedFileUsage> files, Realm realm)
        {
            var beatmaps = new List<RealmBeatmap>();

            foreach (var file in files.Where(f => f.Filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)))
            {
                using (var raw = Files.Store.GetStream(file.File.StoragePath))
                using (var ms = new MemoryStream()) // we need a memory stream so we can seek
                using (var sr = new LineBufferedReader(ms))
                {
                    raw.CopyTo(ms);
                    ms.Position = 0;

                    var decoder = Decoder.GetDecoder<Beatmap>(sr);
                    IBeatmap beatmap = decoder.Decode(sr);

                    string hash = ms.ComputeSHA2Hash();

                    if (beatmaps.Any(b => b.Hash == hash))
                        continue;

                    var beatmapInfo = beatmap.BeatmapInfo;

                    var ruleset = realm.All<RealmRuleset>().FirstOrDefault(r => r.OnlineID == beatmapInfo.RulesetID);
                    var rulesetInstance = (ruleset as IRulesetInfo)?.CreateInstance();

                    if (ruleset == null || rulesetInstance == null)
                    {
                        Logger.Log($"Skipping import due to missing local ruleset {beatmapInfo.RulesetID}.", LoggingTarget.Database);
                        continue;
                    }

                    beatmapInfo.Path = file.Filename;
                    beatmapInfo.Hash = hash;
                    beatmapInfo.MD5Hash = ms.ComputeMD5Hash();

                    // TODO: this should be done in a better place once we actually need to dynamically update it.
                    beatmap.BeatmapInfo.Ruleset = rulesetInstance.RulesetInfo;
                    beatmap.BeatmapInfo.StarDifficulty = rulesetInstance.CreateDifficultyCalculator(new DummyConversionBeatmap(beatmap)).Calculate().StarRating;
                    beatmap.BeatmapInfo.Length = calculateLength(beatmap);
                    beatmap.BeatmapInfo.BPM = 60000 / beatmap.GetMostCommonBeatLength();

                    var difficulty = new RealmBeatmapDifficulty
                    {
                        DrainRate = beatmapInfo.BaseDifficulty.DrainRate,
                        CircleSize = beatmapInfo.BaseDifficulty.CircleSize,
                        OverallDifficulty = beatmapInfo.BaseDifficulty.OverallDifficulty,
                        ApproachRate = beatmapInfo.BaseDifficulty.ApproachRate,
                        SliderMultiplier = beatmapInfo.BaseDifficulty.SliderMultiplier,
                        SliderTickRate = beatmapInfo.BaseDifficulty.SliderTickRate,
                    };

                    var metadata = new RealmBeatmapMetadata
                    {
                        Title = beatmap.Metadata.Title,
                        TitleUnicode = beatmap.Metadata.TitleUnicode,
                        Artist = beatmap.Metadata.Artist,
                        ArtistUnicode = beatmap.Metadata.ArtistUnicode,
                        Author = beatmap.Metadata.AuthorString,
                        Source = beatmap.Metadata.Source,
                        Tags = beatmap.Metadata.Tags,
                        PreviewTime = beatmap.Metadata.PreviewTime,
                        AudioFile = beatmap.Metadata.AudioFile,
                        BackgroundFile = beatmap.Metadata.BackgroundFile,
                    };

                    var realmBeatmap = new RealmBeatmap(ruleset, difficulty, metadata)
                    {
                        DifficultyName = beatmapInfo.Version,
                        OnlineID = beatmapInfo.OnlineBeatmapID,
                        Length = beatmapInfo.Length,
                        BPM = beatmapInfo.BPM,
                        Hash = beatmapInfo.Hash,
                        StarRating = beatmapInfo.StarDifficulty,
                        MD5Hash = beatmapInfo.MD5Hash,
                        Hidden = beatmapInfo.Hidden,
                        AudioLeadIn = beatmapInfo.AudioLeadIn,
                        StackLeniency = beatmapInfo.StackLeniency,
                        SpecialStyle = beatmapInfo.SpecialStyle,
                        LetterboxInBreaks = beatmapInfo.LetterboxInBreaks,
                        WidescreenStoryboard = beatmapInfo.WidescreenStoryboard,
                        EpilepsyWarning = beatmapInfo.EpilepsyWarning,
                        SamplesMatchPlaybackRate = beatmapInfo.SamplesMatchPlaybackRate,
                        DistanceSpacing = beatmapInfo.DistanceSpacing,
                        BeatDivisor = beatmapInfo.BeatDivisor,
                        GridSize = beatmapInfo.GridSize,
                        TimelineZoom = beatmapInfo.TimelineZoom,
                    };

                    // TODO: IBeatmap.BeatmapInfo needs to be updated to the new interface.
                    // beatmaps.Add(beatmap.BeatmapInfo);

                    beatmaps.Add(realmBeatmap);
                }
            }

            return beatmaps;
        }

        public void Dispose()
        {
            onlineLookupQueue?.Dispose();
        }

        private double calculateLength(IBeatmap b)
        {
            if (!b.HitObjects.Any())
                return 0;

            var lastObject = b.HitObjects.Last();

            //TODO: this isn't always correct (consider mania where a non-last object may last for longer than the last in the list).
            double endTime = lastObject.GetEndTime();
            double startTime = b.HitObjects.First().StartTime;

            return endTime - startTime;
        }

        /// <summary>
        /// A dummy WorkingBeatmap for the purpose of retrieving a beatmap for star difficulty calculation.
        /// </summary>
        private class DummyConversionBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap beatmap;

            public DummyConversionBeatmap(IBeatmap beatmap)
                : base(beatmap.BeatmapInfo, null)
            {
                this.beatmap = beatmap;
            }

            protected override IBeatmap GetBeatmap() => beatmap;
            protected override Texture? GetBackground() => null;
            protected override Track? GetBeatmapTrack() => null;
            protected override ISkin? GetSkin() => null;
            public override Stream? GetStream(string storagePath) => null;
        }
    }
}
