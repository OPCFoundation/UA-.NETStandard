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
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsAdditionalTests
    {
        [Test]
        public void ReplaceDCLocalhostNullReturnsNull()
        {
            Assert.That(Utils.ReplaceDCLocalhost(null), Is.Null);
        }

        [Test]
        public void ReplaceDCLocalhostEmptyReturnsEmpty()
        {
            Assert.That(Utils.ReplaceDCLocalhost(string.Empty), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ReplaceDCLocalhostNoDCReturnsOriginal()
        {
            const string subject = "CN=TestApp,O=OPCFoundation";
            Assert.That(Utils.ReplaceDCLocalhost(subject), Is.EqualTo(subject));
        }

        [Test]
        public void ReplaceDCLocalhostReplacesWithHostname()
        {
            const string subject = "CN=TestApp,DC=localhost,O=OPC";
            string result = Utils.ReplaceDCLocalhost(subject, "myhost.example.com");
            Assert.That(result, Does.Contain("DC=myhost.example.com"));
            Assert.That(result, Does.Not.Contain("DC=localhost"));
        }

        [Test]
        public void ReplaceDCLocalhostIPv6HostnameGetsBrackets()
        {
            const string subject = "CN=TestApp,DC=localhost";
            string result = Utils.ReplaceDCLocalhost(subject, "fe80::1");
            Assert.That(result, Does.Contain("DC=[fe80::1]"));
        }

        [Test]
        public void ReplaceDCLocalhostDefaultUsesGetHostName()
        {
            const string subject = "CN=TestApp,DC=localhost";
            string result = Utils.ReplaceDCLocalhost(subject);
            Assert.That(result, Does.Not.Contain("DC=localhost"));
            Assert.That(result, Does.Contain("DC="));
        }

        [Test]
        public void EscapeUriNullReturnsEmpty()
        {
            Assert.That(Utils.EscapeUri(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void EscapeUriRoundTrip()
        {
            const string uri = "opc.tcp://host/path%20with%20spaces";
            string unescaped = Utils.UnescapeUri(uri);
            Assert.That(unescaped, Does.Contain("path with spaces"));
            string escaped = Utils.EscapeUri(unescaped);
            Assert.That(escaped, Is.Not.Null);
        }

        [Test]
        public void UnescapeUriSpanOverload()
        {
            const string uri = "opc.tcp://host/path%20with%20spaces";
            string result = Utils.UnescapeUri(uri.AsSpan());
            Assert.That(result, Does.Contain("path with spaces"));
        }

        [Test]
        public void UnescapeUriStringOverload()
        {
            const string uri = "opc.tcp://host/path%20test";
            string result = Utils.UnescapeUri(uri);
            Assert.That(result, Does.Contain("path test"));
        }

        [Test]
        public void UpdateInstanceUriNullCreatesHttps()
        {
            string result = Utils.UpdateInstanceUri(null);
            Assert.That(result, Does.StartWith("https://"));
        }

        [Test]
        public void UpdateInstanceUriEmptyCreatesHttps()
        {
            string result = Utils.UpdateInstanceUri(string.Empty);
            Assert.That(result, Does.StartWith("https://"));
        }

        [Test]
        public void UpdateInstanceUriNonHttpsPrefixesHost()
        {
            string result = Utils.UpdateInstanceUri("myapp:instance1");
            Assert.That(result, Does.StartWith("https://"));
        }

        [Test]
        public void UpdateInstanceUriLocalhostReplacedWithHost()
        {
            string result = Utils.UpdateInstanceUri("https://localhost/myapp");
            Assert.That(result, Does.Not.Contain("localhost"));
            Assert.That(result, Does.StartWith("https://"));
        }

        [Test]
        public void UpdateInstanceUriPreservesNonLocalhost()
        {
            const string uri = "https://specific.host.com/myapp";
            string result = Utils.UpdateInstanceUri(uri);
            Assert.That(result, Is.EqualTo(uri));
        }

        [Test]
        public void SetIdentifierToAtLeastIncreasesValue()
        {
            uint id = 5;
            uint result = Utils.SetIdentifierToAtLeast(ref id, 10);
            Assert.That(result, Is.EqualTo(5));
            Assert.That(id, Is.EqualTo(10));
        }

        [Test]
        public void SetIdentifierToAtLeastNoChangeWhenAbove()
        {
            uint id = 20;
            uint result = Utils.SetIdentifierToAtLeast(ref id, 10);
            Assert.That(result, Is.EqualTo(20));
            Assert.That(id, Is.EqualTo(20));
        }

        [Test]
        public void SetIdentifierExchanges()
        {
            uint id = 5;
            uint old = Utils.SetIdentifier(ref id, 42);
            Assert.That(old, Is.EqualTo(5));
            Assert.That(id, Is.EqualTo(42));
        }

        [Test]
        public void IncrementIdentifierUintIncrementsAndSkipsZero()
        {
            uint id = 1;
            uint result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(2));

            id = uint.MaxValue;
            result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public void IncrementIdentifierIntIncrementsAndSkipsZero()
        {
            int id = 1;
            int result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(2));

            id = -1;
            result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public void ToInt32SmallValue()
        {
            Assert.That(Utils.ToInt32(42u), Is.EqualTo(42));
        }

        [Test]
        public void ToInt32MaxInt()
        {
            Assert.That(Utils.ToInt32((uint)int.MaxValue), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ToInt32LargeUintReturnsNegative()
        {
            int result = Utils.ToInt32(uint.MaxValue);
            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void ToUInt32PositiveValue()
        {
            Assert.That(Utils.ToUInt32(42), Is.EqualTo(42u));
        }

        [Test]
        public void ToUInt32NegativeValue()
        {
            uint result = Utils.ToUInt32(-1);
            Assert.That(result, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void ToInt32ToUInt32RoundTrip()
        {
            const uint original = 3000000000u;
            int signed = Utils.ToInt32(original);
            uint roundTripped = Utils.ToUInt32(signed);
            Assert.That(roundTripped, Is.EqualTo(original));
        }

        [Test]
        public void GetVersionTimeReturnsNonZero()
        {
            uint vt = Utils.GetVersionTime();
            Assert.That(vt, Is.GreaterThan(0u));
        }

        [Test]
        public void GetAssemblyTimestampReturnsReasonableDate()
        {
            DateTime ts = Utils.GetAssemblyTimestamp();
            Assert.That(ts, Is.GreaterThan(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void GetAssemblySoftwareVersionReturnsNonEmpty()
        {
            string version = Utils.GetAssemblySoftwareVersion();
            Assert.That(version, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetAssemblyBuildNumberReturnsNonEmpty()
        {
            string build = Utils.GetAssemblyBuildNumber();
            Assert.That(build, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ParseCertificateBlobValidCert()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate cert = CertificateBuilder
                .Create("CN=TestCert")
                .CreateForRSA();
            byte[] raw = cert.RawData;

            using Certificate parsed = Utils.ParseCertificateBlob(raw, telemetry);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.Subject, Does.Contain("TestCert"));
        }

        [Test]
        public void ParseCertificateBlobInvalidThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            byte[] badData = [0x01, 0x02, 0x03];

            Assert.Throws<ServiceResultException>(
                () => Utils.ParseCertificateBlob(badData, telemetry));
        }

        [Test]
        public void ParseCertificateChainBlobSingleCert()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate cert = CertificateBuilder
                .Create("CN=ChainTest")
                .CreateForRSA();
            byte[] raw = cert.RawData;

            CertificateCollection chain = Utils.ParseCertificateChainBlob(raw, telemetry);
            Assert.That(chain, Has.Count.EqualTo(1));
            Assert.That(chain[0].Subject, Does.Contain("ChainTest"));
        }

        [Test]
        public void ParseCertificateChainBlobInvalidThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            byte[] badData = [0xFF, 0xFE, 0xFD];

            Assert.Throws<ServiceResultException>(
                () => Utils.ParseCertificateChainBlob(badData, telemetry));
        }

        [Test]
        public void IsEqualUserIdentityX509TokensSameDataEqual()
        {
            byte[] certData = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(certData);
            }

            var token1 = new X509IdentityToken { CertificateData = (ByteString)certData };
            var token2 = new X509IdentityToken
            {
                CertificateData = (ByteString)(byte[])certData.Clone()
            };

            Assert.That(Utils.IsEqualUserIdentity(token1, token2), Is.True);
        }

        [Test]
        public void IsEqualUserIdentityX509TokensDifferentDataNotEqual()
        {
            byte[] data1 = new byte[32];
            byte[] data2 = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data1);
                rng.GetBytes(data2);
            }

            var token1 = new X509IdentityToken { CertificateData = (ByteString)data1 };
            var token2 = new X509IdentityToken { CertificateData = (ByteString)data2 };

            Assert.That(Utils.IsEqualUserIdentity(token1, token2), Is.False);
        }

        [Test]
        public void IsEqualUserIdentityIssuedTokensSameDataEqual()
        {
            byte[] tokenData = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenData);
            }

            var token1 = new IssuedIdentityToken { TokenData = (ByteString)tokenData };
            var token2 = new IssuedIdentityToken
            {
                TokenData = (ByteString)(byte[])tokenData.Clone()
            };

            Assert.That(Utils.IsEqualUserIdentity(token1, token2), Is.True);
        }

        [Test]
        public void IsEqualUserIdentityIssuedTokensDifferentDataNotEqual()
        {
            byte[] data1 = new byte[16];
            byte[] data2 = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data1);
                rng.GetBytes(data2);
            }

            var token1 = new IssuedIdentityToken { TokenData = (ByteString)data1 };
            var token2 = new IssuedIdentityToken { TokenData = (ByteString)data2 };

            Assert.That(Utils.IsEqualUserIdentity(token1, token2), Is.False);
        }

        [Test]
        public void IsEqualUserIdentityMixedTypesNotEqual()
        {
            var anon = new AnonymousIdentityToken();
            var user = new UserNameIdentityToken { UserName = "admin" };

            Assert.That(Utils.IsEqualUserIdentity(anon, user), Is.False);
        }
    }
}
