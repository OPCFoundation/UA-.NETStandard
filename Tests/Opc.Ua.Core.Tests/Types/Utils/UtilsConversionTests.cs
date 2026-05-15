/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml;
using NUnit.Framework;

#pragma warning disable IDE0004 // Remove Unnecessary Cast

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsConversionTests
    {
        [Test]
        public void ToHexStringWithEmptyArray()
        {
            string result = Utils.ToHexString([]);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToHexStringWithSingleByte()
        {
            string result = Utils.ToHexString([0xAB]);
            Assert.That(result, Is.EqualTo("AB"));
        }

        [Test]
        public void ToHexStringWithMultipleBytes()
        {
            string result = Utils.ToHexString([0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]);
            Assert.That(result, Is.EqualTo("0123456789ABCDEF"));
        }

        [Test]
        public void ToHexStringInvertEndian()
        {
            string result = Utils.ToHexString([0x01, 0x02, 0x03], invertEndian: true);
            Assert.That(result, Is.EqualTo("030201"));
        }

        [Test]
        public void FromHexStringRoundTrip()
        {
            byte[] original = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
            string hex = Utils.ToHexString(original);
            byte[] result = Utils.FromHexString(hex);
            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void FromHexStringEmpty()
        {
            byte[] result = Utils.FromHexString(string.Empty);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ToInt32WithSmallValue()
        {
            int result = Utils.ToInt32(42u);
            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void ToInt32WithMaxInt()
        {
            int result = Utils.ToInt32((uint)int.MaxValue);
            Assert.That(result, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ToInt32WithLargeUint()
        {
            int result = Utils.ToInt32(uint.MaxValue);
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void ToUInt32WithPositiveInt()
        {
            uint result = Utils.ToUInt32(42);
            Assert.That(result, Is.EqualTo(42u));
        }

        [Test]
        public void ToUInt32WithZero()
        {
            uint result = Utils.ToUInt32(0);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ToUInt32WithNegativeInt()
        {
            uint result = Utils.ToUInt32(-1);
            Assert.That(result, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void ToUInt32RoundTrip()
        {
            const uint original = 0x80000001u;
            int signed = Utils.ToInt32(original);
            uint roundTripped = Utils.ToUInt32(signed);
            Assert.That(roundTripped, Is.EqualTo(original));
        }

        [Test]
        public void IncrementIdentifierUint()
        {
            uint id = 5;
            uint result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(6u));
            Assert.That(id, Is.EqualTo(6u));
        }

        [Test]
        public void IncrementIdentifierInt()
        {
            int id = 5;
            int result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(6));
            Assert.That(id, Is.EqualTo(6));
        }

        [Test]
        public void IncrementIdentifierSkipsZeroUint()
        {
            uint id = uint.MaxValue;
            uint result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public void IncrementIdentifierSkipsZeroInt()
        {
            int id = -1;
            int result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public void SetIdentifierToAtLeastWhenBelow()
        {
            uint id = 5;
            uint oldValue = Utils.SetIdentifierToAtLeast(ref id, 10);
            Assert.That(id, Is.EqualTo(10u));
            Assert.That(oldValue, Is.EqualTo(5u));
        }

        [Test]
        public void SetIdentifierToAtLeastWhenAbove()
        {
            uint id = 20;
            uint oldValue = Utils.SetIdentifierToAtLeast(ref id, 10);
            Assert.That(id, Is.EqualTo(20u));
            Assert.That(oldValue, Is.EqualTo(20u));
        }

        [Test]
        public void SetIdentifier()
        {
            uint id = 5;
            uint oldValue = Utils.SetIdentifier(ref id, 42);
            Assert.That(oldValue, Is.EqualTo(5u));
            Assert.That(id, Is.EqualTo(42u));
        }

        [Test]
        public void IsValidLocaleIdWithValidLocale()
        {
            Assert.That(Utils.IsValidLocaleId("en-US"), Is.True);
            Assert.That(Utils.IsValidLocaleId("de-DE"), Is.True);
            Assert.That(Utils.IsValidLocaleId("en"), Is.True);
        }

        [Test]
        public void IsValidLocaleIdWithNull()
        {
            Assert.That(Utils.IsValidLocaleId(null), Is.False);
        }

        [Test]
        public void IsValidLocaleIdWithEmpty()
        {
            Assert.That(Utils.IsValidLocaleId(string.Empty), Is.False);
        }

        [Test]
        public void IsValidLocaleIdWithInvalidLocale()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Any locale id is valid on non-Windows platforms");
            }
            Assert.That(Utils.IsValidLocaleId("xx-INVALID-ZZ"), Is.False);
        }

        [Test]
        public void GetLanguageIdWithNull()
        {
            string result = Utils.GetLanguageId(null);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetLanguageIdWithLanguageOnly()
        {
            string result = Utils.GetLanguageId("en");
            Assert.That(result, Is.EqualTo("en"));
        }

        [Test]
        public void GetLanguageIdWithLanguageAndRegion()
        {
            string result = Utils.GetLanguageId("en-US");
            Assert.That(result, Is.EqualTo("en"));
        }

        [Test]
        public void SelectLocalizedTextExactMatch()
        {
            var locales = new List<string> { "de-DE", "en-US" };
            var names = new List<LocalizedText>
            {
                new("en-US", "Hello"),
                new("de-DE", "Hallo")
            };
            var defaultName = new LocalizedText("en", "Default");

            LocalizedText result = Utils.SelectLocalizedText(locales, names, defaultName);
            Assert.That(result.Text, Is.EqualTo("Hallo"));
        }

        [Test]
        public void SelectLocalizedTextLanguageMatch()
        {
            var locales = new List<string> { "en" };
            var names = new List<LocalizedText>
            {
                new("en-US", "Hello"),
                new("de-DE", "Hallo")
            };
            var defaultName = new LocalizedText("en", "Default");

            LocalizedText result = Utils.SelectLocalizedText(locales, names, defaultName);
            Assert.That(result.Text, Is.EqualTo("Hello"));
        }

        [Test]
        public void SelectLocalizedTextNoMatch()
        {
            var locales = new List<string> { "fr-FR" };
            var names = new List<LocalizedText>
            {
                new("en-US", "Hello"),
                new("de-DE", "Hallo")
            };
            var defaultName = new LocalizedText("en", "Default");

            LocalizedText result = Utils.SelectLocalizedText(locales, names, defaultName);
            Assert.That(result.Text, Is.EqualTo("Default"));
        }

        [Test]
        public void SelectLocalizedTextNullLocales()
        {
            var names = new List<LocalizedText>
            {
                new("en-US", "Hello")
            };
            var defaultName = new LocalizedText("en", "Default");

            LocalizedText result = Utils.SelectLocalizedText(null, names, defaultName);
            Assert.That(result.Text, Is.EqualTo("Default"));
        }

        [Test]
        public void SelectLocalizedTextNullNames()
        {
            var locales = new List<string> { "en-US" };
            var defaultName = new LocalizedText("en", "Default");

            LocalizedText result = Utils.SelectLocalizedText(locales, null, defaultName);
            Assert.That(result.Text, Is.EqualTo("Default"));
        }

        [Test]
        public void IsUriHttpsSchemeTrue()
        {
            Assert.That(Utils.IsUriHttpsScheme("https://localhost:4840"), Is.True);
        }

        [Test]
        public void IsUriHttpsSchemeFalse()
        {
            Assert.That(Utils.IsUriHttpsScheme("opc.tcp://localhost:4840"), Is.False);
        }

        [Test]
        public void IsUriHttpRelatedSchemeWithHttp()
        {
            Assert.That(Utils.IsUriHttpRelatedScheme("https://localhost:4840"), Is.True);
        }

        [Test]
        public void IsUriHttpRelatedSchemeWithHttps()
        {
            Assert.That(Utils.IsUriHttpRelatedScheme("https://localhost:4840"), Is.True);
        }

        [Test]
        public void IsUriHttpRelatedSchemeWithOpcTcp()
        {
            Assert.That(Utils.IsUriHttpRelatedScheme("opc.tcp://localhost:4840"), Is.False);
        }

        [Test]
        public void AreDomainsEqualSameDomain()
        {
            Assert.That(Utils.AreDomainsEqual("localhost", "localhost"), Is.True);
        }

        [Test]
        public void AreDomainsEqualDifferentDomain()
        {
            Assert.That(Utils.AreDomainsEqual("host1", "host2"), Is.False);
        }

        [Test]
        public void AreDomainsEqualCaseInsensitive()
        {
            Assert.That(Utils.AreDomainsEqual("LOCALHOST", "localhost"), Is.True);
        }

        [Test]
        public void AreDomainsEqualNullReturnsFalse()
        {
            Assert.That(Utils.AreDomainsEqual((string)null, "localhost"), Is.False);
            Assert.That(Utils.AreDomainsEqual("localhost", (string)null), Is.False);
        }

        [Test]
        public void AreDomainsEqualEmptyReturnsFalse()
        {
            Assert.That(Utils.AreDomainsEqual(string.Empty, "localhost"), Is.False);
        }

        [Test]
        public void AreDomainsEqualWithUris()
        {
            var uri1 = new Uri("opc.tcp://localhost:4840");
            var uri2 = new Uri("opc.tcp://localhost:4841");
            Assert.That(Utils.AreDomainsEqual(uri1, uri2), Is.True);
        }

        [Test]
        public void AreDomainsEqualWithDifferentUris()
        {
            var uri1 = new Uri("opc.tcp://host1:4840");
            var uri2 = new Uri("opc.tcp://host2:4840");
            Assert.That(Utils.AreDomainsEqual(uri1, uri2), Is.False);
        }

        [Test]
        public void AreDomainsEqualWithNullUri()
        {
            Assert.That(Utils.AreDomainsEqual((Uri)null, new Uri("opc.tcp://localhost:4840")), Is.False);
            Assert.That(Utils.AreDomainsEqual(new Uri("opc.tcp://localhost:4840"), (Uri)null), Is.False);
        }

        [Test]
        public void NormalizedIPAddressIPv4()
        {
            string result = Utils.NormalizedIPAddress("127.0.0.1");
            Assert.That(result, Is.EqualTo("127.0.0.1"));
        }

        [Test]
        public void NormalizedIPAddressInvalidReturnsOriginal()
        {
            string result = Utils.NormalizedIPAddress("not-an-ip");
            Assert.That(result, Is.EqualTo("not-an-ip"));
        }

        [Test]
        public void FindStringIgnoreCaseFound()
        {
            var list = new List<string> { "Alpha", "Beta", "Gamma" };
            Assert.That(Utils.FindStringIgnoreCase(list, "beta"), Is.True);
        }

        [Test]
        public void FindStringIgnoreCaseNotFound()
        {
            var list = new List<string> { "Alpha", "Beta", "Gamma" };
            Assert.That(Utils.FindStringIgnoreCase(list, "Delta"), Is.False);
        }

        [Test]
        public void FindStringIgnoreCaseNullList()
        {
            Assert.That(Utils.FindStringIgnoreCase(null, "test"), Is.False);
        }

        [Test]
        public void FindStringIgnoreCaseEmptyList()
        {
            Assert.That(Utils.FindStringIgnoreCase([], "test"), Is.False);
        }

        [Test]
        public void AppendByteArrays()
        {
            byte[] a = [1, 2, 3];
            byte[] b = [4, 5];
            byte[] c = [6];
            byte[] result = Utils.Append(a, b, c);
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void AppendWithNull()
        {
            byte[] result = Utils.Append(null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void AppendWithNullEntries()
        {
            byte[] a = [1, 2];
            byte[] result = Utils.Append(a, null, [3]);
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void AppendSingleArray()
        {
            byte[] a = [1, 2, 3];
            byte[] result = Utils.Append(a);
            Assert.That(result, Is.EqualTo(a));
        }

        [Test]
        public void GetDeadlineNormalTimeSpan()
        {
            DateTime deadline = Utils.GetDeadline(TimeSpan.FromMinutes(5));
            Assert.That(deadline, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(deadline, Is.LessThan(DateTime.UtcNow.AddMinutes(6)));
        }

        [Test]
        public void GetDeadlineMaxTimeSpan()
        {
            DateTime deadline = Utils.GetDeadline(TimeSpan.MaxValue);
            Assert.That(deadline, Is.EqualTo(DateTime.MaxValue));
        }

        [Test]
        public void GetTimeoutNormalTimeSpan()
        {
            int timeout = Utils.GetTimeout(TimeSpan.FromSeconds(30));
            Assert.That(timeout, Is.EqualTo(30000));
        }

        [Test]
        public void GetTimeoutNegativeTimeSpan()
        {
            int timeout = Utils.GetTimeout(TimeSpan.FromSeconds(-1));
            Assert.That(timeout, Is.Zero);
        }

        [Test]
        public void GetTimeoutVeryLargeTimeSpan()
        {
            int timeout = Utils.GetTimeout(TimeSpan.MaxValue);
            Assert.That(timeout, Is.EqualTo(-1));
        }

        [Test]
        public void GetFilePathDisplayNameShortPath()
        {
            string result = Utils.GetFilePathDisplayName("short.txt", 100);
            Assert.That(result, Is.EqualTo("short.txt"));
        }

        [Test]
        public void GetFilePathDisplayNameNull()
        {
            string result = Utils.GetFilePathDisplayName(null, 100);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReplaceLocalhostWithHostname()
        {
            string result = Utils.ReplaceLocalhost("opc.tcp://localhost:4840", "myhost");
            Assert.That(result, Does.Contain("myhost"));
            Assert.That(result, Does.Not.Contain("localhost"));
        }

        [Test]
        public void ReplaceLocalhostWithNullReturnsInput()
        {
            string result = Utils.ReplaceLocalhost(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReplaceLocalhostWithEmptyReturnsEmpty()
        {
            string result = Utils.ReplaceLocalhost(string.Empty);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ReplaceLocalhostCaseInsensitive()
        {
            string result = Utils.ReplaceLocalhost("opc.tcp://LOCALHOST:4840", "myhost");
            Assert.That(result, Does.Contain("myhost"));
        }

        [Test]
        public void ReplaceLocalhostNoLocalhostUnchanged()
        {
            string result = Utils.ReplaceLocalhost("opc.tcp://remotehost:4840", "myhost");
            Assert.That(result, Is.EqualTo("opc.tcp://remotehost:4840"));
        }

        [Test]
        public void ReplaceDCLocalhostWithHostname()
        {
            string result = Utils.ReplaceDCLocalhost("DC=localhost", "myhost");
            Assert.That(result, Does.Contain("myhost"));
        }

        [Test]
        public void ReplaceDCLocalhostWithNullReturnsInput()
        {
            string result = Utils.ReplaceDCLocalhost(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReplaceDCLocalhostNoLocalhostUnchanged()
        {
            string result = Utils.ReplaceDCLocalhost("DC=remotehost", "myhost");
            Assert.That(result, Is.EqualTo("DC=remotehost"));
        }

        [Test]
        public void IsEqualWithEqualObjects()
        {
            Assert.That(Utils.IsEqual((object)42, (object)42), Is.True);
        }

        [Test]
        public void IsEqualWithDifferentObjects()
        {
            Assert.That(Utils.IsEqual((object)42, (object)99), Is.False);
        }

        [Test]
        public void IsEqualWithBothNull()
        {
            Assert.That(Utils.IsEqual((object)null, (object)null), Is.True);
        }

        [Test]
        public void IsEqualWithOneNull()
        {
            Assert.That(Utils.IsEqual((object)42, (object)null), Is.False);
        }

        [Test]
        public void IsEqualGenericInts()
        {
            Assert.That(Utils.IsEqual(42, 42), Is.True);
            Assert.That(Utils.IsEqual(42, 99), Is.False);
        }

        [Test]
        public void IsEqualReadOnlySpanEqual()
        {
            ReadOnlySpan<byte> a = [1, 2, 3];
            ReadOnlySpan<byte> b = [1, 2, 3];
            Assert.That(Utils.IsEqual(a, b), Is.True);
        }

        [Test]
        public void IsEqualReadOnlySpanDifferent()
        {
            ReadOnlySpan<byte> a = [1, 2, 3];
            ReadOnlySpan<byte> b = [1, 2, 4];
            Assert.That(Utils.IsEqual(a, b), Is.False);
        }

        [Test]
        public void IsEqualReadOnlySpanDifferentLength()
        {
            ReadOnlySpan<byte> a = [1, 2, 3];
            ReadOnlySpan<byte> b = [1, 2];
            Assert.That(Utils.IsEqual(a, b), Is.False);
        }

        [Test]
        public void IsEqualReadOnlySpanBothEmpty()
        {
#pragma warning disable IDE0301 // Simplify collection initialization
            Assert.That(Utils.IsEqual(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty), Is.True);
#pragma warning restore IDE0301 // Simplify collection initialization
        }

        [Test]
        public void IsEqualArrays()
        {
            byte[] a = [1, 2, 3];
            byte[] b = [1, 2, 3];
            Assert.That(Utils.IsEqual(a, b), Is.True);
        }

        [Test]
        public void IsEqualArraysDifferent()
        {
            byte[] a = [1, 2, 3];
            byte[] b = [4, 5, 6];
            Assert.That(Utils.IsEqual(a, b), Is.False);
        }

        [Test]
        public void FormatBasicString()
        {
            string result = Utils.Format("Hello {0}", "World");
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        [Test]
        public void FormatWithNoArgs()
        {
            string result = Utils.Format("No args");
            Assert.That(result, Is.EqualTo("No args"));
        }

        [Test]
        public void ToOpcUaUniversalTimeConvertsLocal()
        {
            var localTime = new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Local);
            DateTime result = Utils.ToOpcUaUniversalTime(localTime);
            Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void ToOpcUaUniversalTimeWithMinValue()
        {
            DateTime result = Utils.ToOpcUaUniversalTime(DateTime.MinValue);
            Assert.That(result, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void ParseUriValid()
        {
            Uri uri = Utils.ParseUri("opc.tcp://localhost:4840");
            Assert.That(uri, Is.Not.Null);
            Assert.That(uri.Host, Is.EqualTo("localhost"));
        }

        [Test]
        public void ParseUriNull()
        {
            Uri uri = Utils.ParseUri(null);
            Assert.That(uri, Is.Null);
        }

        [Test]
        public void ParseUriInvalid()
        {
            Uri uri = Utils.ParseUri("not a valid uri :::");
            Assert.That(uri, Is.Null);
        }

        [Test]
        public void ParseUriEmpty()
        {
            Uri uri = Utils.ParseUri(string.Empty);
            Assert.That(uri, Is.Null);
        }

        [Test]
        public void PSHANullHmacThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Utils.PSHA(null, "label", [1], 0, 32));
        }

        [Test]
        public void PSHA1NullSecretThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Utils.PSHA1((byte[])null, "label", [1], 0, 32));
        }

        [Test]
        public void PSHA256NullSecretThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Utils.PSHA256((byte[])null, "label", [1], 0, 32));
        }

        [Test]
        public void PSHA1ProducesOutput()
        {
            byte[] secret = [0x01, 0x02, 0x03, 0x04];
            byte[] data = [0x05, 0x06, 0x07, 0x08];
            byte[] result = Utils.PSHA1(secret, "test", data, 0, 32);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Length.EqualTo(32));
        }

        [Test]
        public void PSHA256ProducesOutput()
        {
            byte[] secret = [0x01, 0x02, 0x03, 0x04];
            byte[] data = [0x05, 0x06, 0x07, 0x08];
            byte[] result = Utils.PSHA256(secret, "test", data, 0, 32);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Length.EqualTo(32));
        }

        [Test]
        public void PSHA1DeterministicOutput()
        {
            byte[] secret = [0x01, 0x02, 0x03, 0x04];
            byte[] data = [0x05, 0x06, 0x07, 0x08];
            byte[] result1 = Utils.PSHA1(secret, "test", data, 0, 32);
            byte[] result2 = Utils.PSHA1(secret, "test", data, 0, 32);
            Assert.That(result1, Is.EqualTo(result2));
        }

        [Test]
        public void PSHAWithNegativeOffsetThrows()
        {
            using var hmac = new HMACSHA256([1, 2, 3, 4]);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Utils.PSHA(hmac, "label", [1], -1, 32));
        }

        [Test]
        public void PSHAWithNegativeLengthThrows()
        {
            using var hmac = new HMACSHA256([1, 2, 3, 4]);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Utils.PSHA(hmac, "label", [1], 0, -1));
        }

        [Test]
        public void CreateHMACWithSHA1()
        {
            using HMAC hmac = Utils.CreateHMAC(HashAlgorithmName.SHA1, [1, 2, 3]);
            Assert.That(hmac, Is.Not.Null);
            Assert.That(hmac, Is.InstanceOf<HMACSHA1>());
        }

        [Test]
        public void CreateHMACWithSHA256()
        {
            using HMAC hmac = Utils.CreateHMAC(HashAlgorithmName.SHA256, [1, 2, 3]);
            Assert.That(hmac, Is.Not.Null);
            Assert.That(hmac, Is.InstanceOf<HMACSHA256>());
        }

        [Test]
        public void DefaultUriSchemesNotEmpty()
        {
            Assert.That(Utils.DefaultUriSchemes, Is.Not.Null);
            Assert.That(Utils.DefaultUriSchemes, Is.Not.Empty);
        }

        [Test]
        public void DefaultBindingsNotEmpty()
        {
            Assert.That(Utils.DefaultBindings, Is.Not.Null);
            Assert.That(Utils.DefaultBindings, Is.Not.Empty);
        }

        [Test]
        public void TimeBaseIsNotMinValue()
        {
            Assert.That(Utils.TimeBase, Is.Not.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void GetHostNameReturnsNonEmpty()
        {
            string hostname = Utils.GetHostName();
            Assert.That(hostname, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetFullQualifiedDomainNameReturnsNonEmpty()
        {
            string fqdn = Utils.GetFullQualifiedDomainName();
            Assert.That(fqdn, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void DefaultXmlReaderSettingsReturnsNonNull()
        {
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            Assert.That(settings, Is.Not.Null);
        }

        [Test]
        public void DefaultXmlWriterSettingsReturnsNonNull()
        {
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            Assert.That(settings, Is.Not.Null);
        }

        [Test]
        public void GetAssemblySoftwareVersionReturnsNonNull()
        {
            string version = Utils.GetAssemblySoftwareVersion();
            Assert.That(version, Is.Not.Null);
        }

        [Test]
        public void GetAssemblyBuildNumberReturnsNonNull()
        {
            string build = Utils.GetAssemblyBuildNumber();
            Assert.That(build, Is.Not.Null);
        }

        [Test]
        public void IsPathRootedWithAbsolutePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.That(Utils.IsPathRooted("C:\\temp\\file.txt"), Is.True);
            }
            else
            {
                Assert.That(Utils.IsPathRooted("/temp/file.txt"), Is.True);
            }
        }

        [Test]
        public void IsPathRootedWithRelativePath()
        {
            Assert.That(Utils.IsPathRooted("relative\\path"), Is.False);
        }

        [Test]
        public void GetVersionTimeReturnsNonZero()
        {
            uint version = Utils.GetVersionTime();
            Assert.That(version, Is.GreaterThan(0u));
        }

        [Test]
        public void PSHAWithHmacOverload()
        {
            using var hmac = new HMACSHA256([1, 2, 3, 4]);
            byte[] data = [5, 6, 7, 8];
            byte[] result = Utils.PSHA(hmac, "test", data, 0, 64);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Length.EqualTo(64));
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5350:Do Not Use Weak Cryptographic Algorithms",
            Justification = "Testing existing API")]
        public void PSHA1WithHmacOverload()
        {
            using var hmac = new HMACSHA1([1, 2, 3, 4]);
            byte[] data = [5, 6, 7, 8];
            byte[] result = Utils.PSHA1(hmac, "test", data, 0, 32);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Length.EqualTo(32));
        }

        [Test]
        public void PSHA256WithHmacOverload()
        {
            using var hmac = new HMACSHA256([1, 2, 3, 4]);
            byte[] data = [5, 6, 7, 8];
            byte[] result = Utils.PSHA256(hmac, "test", data, 0, 32);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Length.EqualTo(32));
        }

        [Test]
        public void IsEqualEnumerableEqual()
        {
            var list1 = new List<int> { 1, 2, 3 };
            var list2 = new List<int> { 1, 2, 3 };
            Assert.That(Utils.IsEqual(list1, list2), Is.True);
        }

        [Test]
        public void IsEqualEnumerableDifferent()
        {
            var list1 = new List<int> { 1, 2, 3 };
            var list2 = new List<int> { 1, 2, 4 };
            Assert.That(Utils.IsEqual(list1, list2), Is.False);
        }
    }
}
