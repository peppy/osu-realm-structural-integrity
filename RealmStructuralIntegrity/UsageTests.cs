using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Database;
using Xunit;
using Xunit.Abstractions;

namespace osu.Game
{
    public class UsageTests
    {
        private readonly ITestOutputHelper output;

        private static readonly Storage storage = new TemporaryNativeStorage("realm-test");

        public UsageTests(ITestOutputHelper output)
        {
            this.output = output;

            output.WriteLine($"Running tests at storage location {storage.GetFullPath(string.Empty)}");
        }

        [Fact]
        public void TestConstructRealm()
        {
            var realmFactory = new RealmContextFactory(storage);

            realmFactory.Context.Refresh();
        }
    }
}
