using System;
using Realms;
using Xunit;

namespace osu.Game
{
    public class UsageTests
    {
        public class Test : RealmObject
        {
            public string TestString { get; set; }
        }

        [Fact]
        public void TestConstructRealm1()
        {
            var realm = Realm.GetInstance($"{Guid.NewGuid()}.realm");
            realm.Dispose();
        }
    }
}
