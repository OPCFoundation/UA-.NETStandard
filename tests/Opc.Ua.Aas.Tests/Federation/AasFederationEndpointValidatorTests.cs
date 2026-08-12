/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Net;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Federation;

namespace Opc.Ua.Aas.Tests.Federation
{
    /// <summary>
    /// Egress guards applied to every federation target before any byte is read.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasFederationEndpointValidatorTests
    {
        [Test]
        public void ValidateUriAcceptsAPublicHttpsHostThatStillNeedsDnsResolution()
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult result = validator.ValidateUri(new Uri("https://registry.example.com/aas"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Message, Is.Empty);
                Assert.That(result.Content.Length, Is.Zero);
            });
        }

        [Test]
        public void ValidateUriRejectsARelativeTarget()
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult result = validator.ValidateUri(new Uri("/aas", UriKind.Relative));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Is.EqualTo("Federation target must be an absolute URI."));
            });
        }

        [Test]
        public void ValidateUriRejectsNull()
        {
            var validator = new AasFederationEndpointValidator();

            Assert.That(
                () => validator.ValidateUri(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("uri"));
        }

        [TestCase("http://registry.example.com/aas", TestName = "PlainHttpIsNotAllowedByDefault")]
        [TestCase("file:///c:/secrets.txt", TestName = "FileSchemeIsNotAllowedByDefault")]
        [TestCase("ftp://registry.example.com/aas", TestName = "FtpSchemeIsNotAllowedByDefault")]
        public void ValidateUriRejectsASchemeOutsideThePolicy(string uri)
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult result = validator.ValidateUri(new Uri(uri));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Is.EqualTo("Federation target scheme is not allowed."));
            });
        }

        [Test]
        public void ValidateUriAcceptsOpcTcpWhichIsInTheDefaultSchemeAllowList()
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult result = validator.ValidateUri(
                new Uri("opc.tcp://registry.example.com:4840/UA"));

            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public void ValidateUriEnforcesTheHostAllowListWhenItIsNotEmpty()
        {
            var policy = new AasFederationEgressPolicy();
            policy.AllowedHosts.Add("registry.example.com");
            var validator = new AasFederationEndpointValidator(policy);

            AasFederationResolutionResult allowed = validator.ValidateUri(
                new Uri("https://registry.example.com/aas"));
            AasFederationResolutionResult blocked = validator.ValidateUri(new Uri("https://evil.example.com/aas"));

            Assert.Multiple(() =>
            {
                Assert.That(allowed.Succeeded, Is.True);
                Assert.That(blocked.Succeeded, Is.False);
                Assert.That(blocked.Message, Is.EqualTo("Federation target host is not allowed."));
            });
        }

        [Test]
        public void ValidateUriComparesTheDefaultPortAgainstThePortAllowList()
        {
            var policy = new AasFederationEgressPolicy();
            policy.AllowedPorts.Add(443);
            var validator = new AasFederationEndpointValidator(policy);

            AasFederationResolutionResult implicitDefault = validator.ValidateUri(
                new Uri("https://registry.example.com/aas"));
            AasFederationResolutionResult explicitOther = validator.ValidateUri(
                new Uri("https://registry.example.com:8443/aas"));

            Assert.Multiple(() =>
            {
                Assert.That(implicitDefault.Succeeded, Is.True);
                Assert.That(explicitOther.Succeeded, Is.False);
                Assert.That(explicitOther.Message, Is.EqualTo("Federation target port is not allowed."));
            });
        }

        [Test]
        public void ValidateUriHasNoDefaultPortForOpcTcpSoAPortlessTargetFailsThePortAllowList()
        {
            var policy = new AasFederationEgressPolicy();
            policy.AllowedPorts.Add(4840);
            var validator = new AasFederationEndpointValidator(policy);

            AasFederationResolutionResult withPort = validator.ValidateUri(
                new Uri("opc.tcp://registry.example.com:4840/UA"));
            AasFederationResolutionResult withoutPort = validator.ValidateUri(
                new Uri("opc.tcp://registry.example.com/UA"));

            Assert.Multiple(() =>
            {
                Assert.That(withPort.Succeeded, Is.True);
                Assert.That(withoutPort.Succeeded, Is.False);
                Assert.That(withoutPort.Message, Is.EqualTo("Federation target port is not allowed."));
            });
        }

        [TestCase("localhost", TestName = "LocalhostAliasIsBlocked")]
        [TestCase("LOCALHOST", TestName = "LocalhostAliasIsBlockedCaseInsensitively")]
        [TestCase("ip6-localhost", TestName = "Ip6LocalhostAliasIsBlocked")]
        [TestCase("ip6-loopback", TestName = "Ip6LoopbackAliasIsBlocked")]
        public void ValidateUriRejectsLoopbackHostNamesBeforeAnyDnsLookup(string host)
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult result = validator.ValidateUri(new Uri("https://" + host + "/aas"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Is.EqualTo("Federation target host is a localhost alias."));
            });
        }

        [Test]
        public void ValidateUriAllowsALoopbackHostNameThatTheOperatorExplicitlyTrusted()
        {
            var policy = new AasFederationEgressPolicy();
            policy.TrustedRestrictedHosts.Add("localhost");
            var validator = new AasFederationEndpointValidator(policy);

            AasFederationResolutionResult result = validator.ValidateUri(new Uri("https://localhost/aas"));

            Assert.That(result.Succeeded, Is.True);
        }

        [TestCase("10.0.0.1", TestName = "PrivateClassAIsBlocked")]
        [TestCase("172.16.0.1", TestName = "PrivateClassBLowerBoundIsBlocked")]
        [TestCase("172.31.255.254", TestName = "PrivateClassBUpperBoundIsBlocked")]
        [TestCase("192.168.1.1", TestName = "PrivateClassCIsBlocked")]
        [TestCase("169.254.169.254", TestName = "LinkLocalMetadataEndpointIsBlocked")]
        [TestCase("100.64.0.1", TestName = "CarrierGradeNatIsBlocked")]
        [TestCase("127.0.0.1", TestName = "LoopbackLiteralIsBlocked")]
        [TestCase("0.0.0.0", TestName = "UnspecifiedAddressIsBlocked")]
        [TestCase("255.255.255.255", TestName = "BroadcastAddressIsBlocked")]
        [TestCase("224.0.0.1", TestName = "MulticastIsBlocked")]
        [TestCase("240.0.0.1", TestName = "ReservedClassEIsBlocked")]
        [TestCase("192.0.2.5", TestName = "TestNet1IsBlocked")]
        [TestCase("198.18.0.1", TestName = "BenchmarkRangeIsBlocked")]
        [TestCase("198.51.100.5", TestName = "TestNet2IsBlocked")]
        [TestCase("203.0.113.5", TestName = "TestNet3IsBlocked")]
        [TestCase("::1", TestName = "IPv6LoopbackIsBlocked")]
        [TestCase("fe80::1", TestName = "IPv6LinkLocalIsBlocked")]
        [TestCase("ff02::1", TestName = "IPv6MulticastIsBlocked")]
        [TestCase("fc00::1", TestName = "IPv6UniqueLocalIsBlocked")]
        [TestCase("fd12:3456::1", TestName = "IPv6UniqueLocalWithLocalBitIsBlocked")]
        [TestCase("2001:db8::1", TestName = "IPv6DocumentationRangeIsBlocked")]
        [TestCase("::ffff:10.0.0.1", TestName = "IPv4MappedPrivateAddressIsBlocked")]
        public void ValidateUriRejectsARestrictedAddressLiteral(string host)
        {
            var validator = new AasFederationEndpointValidator();
            string authority = host.Contains(':', StringComparison.Ordinal) ? "[" + host + "]" : host;

            AasFederationResolutionResult result = validator.ValidateUri(new Uri("https://" + authority + "/aas"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo("Federation target address is restricted and is not explicitly trusted."));
            });
        }

        [TestCase("8.8.8.8", TestName = "PublicIPv4LiteralIsAllowed")]
        [TestCase("172.32.0.1", TestName = "AddressJustAbovePrivateClassBIsAllowed")]
        [TestCase("172.15.255.254", TestName = "AddressJustBelowPrivateClassBIsAllowed")]
        [TestCase("100.128.0.1", TestName = "AddressJustAboveCarrierGradeNatIsAllowed")]
        [TestCase("2606:4700::1111", TestName = "PublicIPv6LiteralIsAllowed")]
        public void ValidateUriAcceptsAPublicAddressLiteral(string host)
        {
            var validator = new AasFederationEndpointValidator();
            string authority = host.Contains(':', StringComparison.Ordinal) ? "[" + host + "]" : host;

            AasFederationResolutionResult result = validator.ValidateUri(new Uri("https://" + authority + "/aas"));

            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public void ValidateAddressBlocksADnsAnswerThatRebindsAPublicNameToAPrivateAddress()
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult result = validator.ValidateAddress(
                "registry.example.com",
                IPAddress.Parse("169.254.169.254"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo("Federation target address is restricted and is not explicitly trusted."));
            });
        }

        [Test]
        public void ValidateAddressUnwrapsAnIPv4MappedIPv6AnswerBeforeApplyingTheRangeRules()
        {
            var validator = new AasFederationEndpointValidator();

            AasFederationResolutionResult mappedPrivate = validator.ValidateAddress(
                "registry.example.com",
                IPAddress.Parse("::ffff:192.168.0.5"));
            AasFederationResolutionResult mappedPublic = validator.ValidateAddress(
                "registry.example.com",
                IPAddress.Parse("::ffff:8.8.4.4"));

            Assert.Multiple(() =>
            {
                Assert.That(mappedPrivate.Succeeded, Is.False);
                Assert.That(mappedPublic.Succeeded, Is.True);
            });
        }

        [Test]
        public void ValidateAddressHonoursTheTrustedRestrictedHostListForTheOriginalHostName()
        {
            var policy = new AasFederationEgressPolicy();
            policy.TrustedRestrictedHosts.Add("gateway.internal");
            var validator = new AasFederationEndpointValidator(policy);

            AasFederationResolutionResult trusted = validator.ValidateAddress(
                "gateway.internal",
                IPAddress.Parse("10.1.2.3"));
            AasFederationResolutionResult untrusted = validator.ValidateAddress(
                "other.internal",
                IPAddress.Parse("10.1.2.3"));

            Assert.Multiple(() =>
            {
                Assert.That(trusted.Succeeded, Is.True);
                Assert.That(untrusted.Succeeded, Is.False);
            });
        }

        [Test]
        public void ValidateAddressRejectsNullArguments()
        {
            var validator = new AasFederationEndpointValidator();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => validator.ValidateAddress(null!, IPAddress.Loopback),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("host"));
                Assert.That(
                    () => validator.ValidateAddress("registry.example.com", null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("address"));
            });
        }

        [Test]
        public void DefaultPolicyIsUsedWhenNoneIsSupplied()
        {
            var validator = new AasFederationEndpointValidator();

            Assert.Multiple(() =>
            {
                Assert.That(validator.Policy, Is.Not.Null);
                Assert.That(validator.Policy.AllowedSchemes, Does.Contain("https"));
                Assert.That(validator.Policy.AllowedHosts, Is.Empty);
                Assert.That(validator.Policy.AllowedPorts, Is.Empty);
                Assert.That(validator.Policy.TrustedRestrictedHosts, Is.Empty);
                Assert.That(validator.Policy.MaxRedirects, Is.EqualTo(5));
                Assert.That(validator.Policy.MaxDecompressedBytes, Is.EqualTo(16 * 1024 * 1024));
            });
        }

        [Test]
        public void SuppliedPolicyIsRetainedAndEnforced()
        {
            var policy = new AasFederationEgressPolicy();
            policy.AllowedSchemes.Clear();
            policy.AllowedSchemes.Add("https");
            var validator = new AasFederationEndpointValidator(policy);

            AasFederationResolutionResult result = validator.ValidateUri(
                new Uri("opc.tcp://registry.example.com:4840/UA"));

            Assert.Multiple(() =>
            {
                Assert.That(validator.Policy, Is.SameAs(policy));
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Is.EqualTo("Federation target scheme is not allowed."));
            });
        }

        [Test]
        public void ResolutionResultsCompareOnOutcomeContentAndMessage()
        {
            AasFederationResolutionResult success = AasFederationResolutionResult.Success(
                ByteString.From(s_payload));
            AasFederationResolutionResult sameSuccess = AasFederationResolutionResult.Success(
                ByteString.From(s_payload));
            AasFederationResolutionResult otherContent = AasFederationResolutionResult.Success(
                ByteString.From(s_otherPayload));
            AasFederationResolutionResult failure = AasFederationResolutionResult.Fail("blocked");
            AasFederationResolutionResult sameFailure = AasFederationResolutionResult.Fail("blocked");

            bool equalSuccesses = success == sameSuccess;
            bool contentMatters = success != otherContent;
            bool equalFailures = failure == sameFailure;
            bool outcomesDiffer = success != failure;
            bool boxedEquals = failure.Equals((object)sameFailure);
            bool foreignTypeEquals = failure.Equals("blocked");

            Assert.Multiple(() =>
            {
                Assert.That(equalSuccesses, Is.True);
                Assert.That(contentMatters, Is.True);
                Assert.That(equalFailures, Is.True);
                Assert.That(outcomesDiffer, Is.True);
                Assert.That(boxedEquals, Is.True);
                Assert.That(foreignTypeEquals, Is.False);
                Assert.That(success.GetHashCode(), Is.EqualTo(sameSuccess.GetHashCode()));
                Assert.That(failure.Content.Length, Is.Zero);
                Assert.That(failure.Message, Is.EqualTo("blocked"));
            });
        }

        private static readonly byte[] s_payload = [1, 2, 3];
        private static readonly byte[] s_otherPayload = [4, 5, 6];
    }
}
