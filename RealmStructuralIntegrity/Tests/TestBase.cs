// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Nito.AsyncEx;
using osu.Framework.Extensions;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Models;
using Xunit.Abstractions;

namespace osu.Game.Tests
{
    public abstract class TestBase
    {
        protected readonly ITestOutputHelper Logger;

        private static readonly TemporaryNativeStorage storage;

        static TestBase()
        {
            storage = new TemporaryNativeStorage("realm-test");
            storage.DeleteDirectory(string.Empty);
        }

        protected TestBase(ITestOutputHelper logger)
        {
            Logger = logger;
        }

        protected void RunTestWithRealm(Action<RealmContextFactory> testAction, [CallerMemberName] string caller = "")
        {
            AsyncContext.Run(() =>
            {
                using (var realmFactory = new RealmContextFactory(storage, caller))
                {
                    Logger.WriteLine($"Running test using realm file {storage.GetFullPath(realmFactory.Filename)}");
                    testAction(realmFactory);

                    realmFactory.Dispose();
                    Logger.WriteLine($"Final database size: {storage.GetStream(realmFactory.Filename)?.Length ?? 0}");

                    realmFactory.Compact();
                    Logger.WriteLine($"Final database size after compact: {storage.GetStream(realmFactory.Filename)?.Length ?? 0}");
                }
            });
        }

        protected static RealmBeatmapSet CreateBeatmapSet(RealmRuleset ruleset)
        {
            RealmFile createRealmFile() => new RealmFile { Hash = Guid.NewGuid().ToString().ComputeSHA2Hash() };

            var metadata = new RealmBeatmapMetadata
            {
                Title = "My Love",
                Artist = "Kuba Oms"
            };

            var beatmapSet = new RealmBeatmapSet
            {
                Beatmaps =
                {
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Easy", },
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Normal", },
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Hard", },
                    new RealmBeatmap(ruleset, new RealmBeatmapDifficulty(), metadata) { DifficultyName = "Insane", }
                },
                Files =
                {
                    new RealmNamedFileUsage(createRealmFile(), "test [easy].osu"),
                    new RealmNamedFileUsage(createRealmFile(), "test [normal].osu"),
                    new RealmNamedFileUsage(createRealmFile(), "test [hard].osu"),
                    new RealmNamedFileUsage(createRealmFile(), "test [insane].osu"),
                }
            };

            for (int i = 0; i < 8; i++)
                beatmapSet.Files.Add(new RealmNamedFileUsage(createRealmFile(), $"hitsound{i}.mp3"));

            foreach (var b in beatmapSet.Beatmaps)
                b.BeatmapSet = beatmapSet;

            return beatmapSet;
        }

        protected static RealmRuleset CreateRuleset() =>
            new RealmRuleset(0, "osu!", "osu", true);

        public class LocalSyncContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                SetSynchronizationContext(this);
                d(state);
            }
        }
    }
}
