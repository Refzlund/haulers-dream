using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class ProfileCodecTests
    {
        private static Dictionary<string, string> RoundTrip(string version, string name, Dictionary<string, string> changed)
        {
            string token = ProfileCodec.Encode(version, name, changed);
            Assert.That(ProfileCodec.TryDecode(token, out var data), Is.True, "token should decode");
            Assert.That(data.Version, Is.EqualTo(version));
            Assert.That(data.Name, Is.EqualTo(name));
            return data.Changed;
        }

        [Test]
        public void RoundTrips_EmptyChanges()
        {
            var c = RoundTrip("1.2.0", "MyProfile", new Dictionary<string, string>());
            Assert.That(c.Count, Is.EqualTo(0));
        }

        [Test]
        public void RoundTrips_ScalarChanges()
        {
            var changed = new Dictionary<string, string>
            {
                { "carryLimitFraction", "0.75" },
                { "overloadLevel", "3" },
                { "pickupMode", "DirectToInventory" },
                { "strictCarryWeight", "1" },
            };
            var c = RoundTrip("1.2.0", "Hauler build", changed);
            Assert.That(c.Count, Is.EqualTo(changed.Count));
            foreach (var kv in changed)
                Assert.That(c[kv.Key], Is.EqualTo(kv.Value), kv.Key);
        }

        [Test]
        public void RoundTrips_NameWithSpecialChars()
        {
            // tabs/newlines/backslashes in the name must survive the line-oriented framing
            var c = RoundTrip("1.2.0", "weird\tname\\with\nbreaks", new Dictionary<string, string> { { "a", "1" } });
            Assert.That(c["a"], Is.EqualTo("1"));
        }

        [Test]
        public void RoundTrips_ValuesWithSeparatorChars()
        {
            // collection encodings use U+001F / U+001E internally — they must survive framing
            var changed = new Dictionary<string, string>
            {
                { "itemUnloadRules", "Steel|0|0Gold|1|50" },
                { "storageBuildingFilter", "shelfAshelfBshelfC" },
            };
            var c = RoundTrip("1.2.0", "n", changed);
            Assert.That(c["itemUnloadRules"], Is.EqualTo(changed["itemUnloadRules"]));
            Assert.That(c["storageBuildingFilter"], Is.EqualTo(changed["storageBuildingFilter"]));
        }

        [Test]
        public void Compresses_LargePayload_StillRoundTrips()
        {
            var changed = new Dictionary<string, string>();
            for (int i = 0; i < 200; i++) changed["field_" + i] = "value_" + i + "_repeated_repeated_repeated";
            string token = ProfileCodec.Encode("1.2.0", "big", changed);
            Assert.That(token[ProfileCodec.Prefix.Length], Is.EqualTo('1'), "a large, repetitive payload should compress (flag '1')");
            Assert.That(ProfileCodec.TryDecode(token, out var data), Is.True);
            Assert.That(data.Changed.Count, Is.EqualTo(200));
            Assert.That(data.Changed["field_199"], Is.EqualTo("value_199_repeated_repeated_repeated"));
        }

        [Test]
        public void ShortPayload_PicksRawOverCompressed()
        {
            // a tiny payload should NOT be deflated (deflate+overhead would be larger)
            string token = ProfileCodec.Encode("1.2.0", "n", new Dictionary<string, string> { { "a", "1" } });
            Assert.That(token[ProfileCodec.Prefix.Length], Is.EqualTo('0'));
        }

        [Test]
        public void TryDecode_RejectsGarbage()
        {
            Assert.That(ProfileCodec.TryDecode(null, out _), Is.False);
            Assert.That(ProfileCodec.TryDecode("", out _), Is.False);
            Assert.That(ProfileCodec.TryDecode("hello world", out _), Is.False);
            Assert.That(ProfileCodec.TryDecode("HDP", out _), Is.False);
            Assert.That(ProfileCodec.TryDecode("HDP2abc", out _), Is.False);              // bad flag
            Assert.That(ProfileCodec.TryDecode("HDP1!!!notbase64!!!", out _), Is.False);  // bad base64
            Assert.That(ProfileCodec.TryDecode("HDP0" + System.Convert.ToBase64String(new byte[] { 1, 2, 3 }), out _), Is.False); // no name line
        }

        [Test]
        public void TryDecode_ToleratesLeadingTrailingWhitespace()
        {
            string token = ProfileCodec.Encode("1.2.0", "n", new Dictionary<string, string> { { "a", "1" } });
            Assert.That(ProfileCodec.TryDecode("  \n" + token + "  \n", out var data), Is.True);
            Assert.That(data.Changed["a"], Is.EqualTo("1"));
        }
    }
}
