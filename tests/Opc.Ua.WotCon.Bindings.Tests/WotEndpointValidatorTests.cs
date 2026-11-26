// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotEndpointValidator"/>.
    /// </summary>
    [TestFixture]
    public sealed class WotEndpointValidatorTests
    {
        [TestCase("http://169.254.169.254/latest/meta-data/")]
        [TestCase("http://[::ffff:169.254.169.254]/latest/meta-data/")]
        [TestCase("http://127.0.0.1/admin")]
        [TestCase("http://[::1]/admin")]
        [TestCase("http://10.0.0.1/device")]
        [TestCase("http://192.168.1.1/device")]
        [TestCase("http://172.16.0.1/device")]
        [TestCase("http://100.64.0.1/device")]
        [TestCase("http://[fc00::1]/device")]
        [TestCase("http://[fe80::1]/device")]
        public void ValidateRejectsLoopbackAndPrivateIpLiterals(string endpoint)
        {
            ServiceResult result = WotEndpointValidator.Validate(endpoint, new WotEndpointPolicy(), out Uri? normalized);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(normalized, Is.Null);
        }

        [Test]
        public void ValidateAcceptsPublicHost()
        {
            ServiceResult result = WotEndpointValidator.Validate(
                "https://example.com/api", new WotEndpointPolicy(), out Uri? normalized);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(normalized, Is.Not.Null);
            Assert.That(normalized!.Host, Is.EqualTo("example.com"));
        }

        /// <summary>
        /// Loopback opt-in never makes a missing network authority a valid endpoint.
        /// </summary>
        [TestCase("mqtt://", false)]
        [TestCase("mqtts://", true)]
        [TestCase("mqtt:///private/status", true)]
        [Category("SecurityRegression")]
        public void SecurityF28EndpointPolicyRejectsHostlessMqtt(string endpoint, bool allowLoopback)
        {
            var policy = new WotEndpointPolicy { AllowLoopback = allowLoopback };

            ServiceResult result = WotEndpointValidator.Validate(endpoint, policy, out Uri? normalized);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// Loopback opt-in never makes a missing Modbus authority a valid endpoint.
        /// </summary>
        [TestCase("modbus+tcp://", false)]
        [TestCase("modbus://", true)]
        [TestCase("modbus+tcp:///1", true)]
        [Category("SecurityRegression")]
        public void SecurityF45EndpointPolicyRejectsHostlessModbus(string endpoint, bool allowLoopback)
        {
            var policy = new WotEndpointPolicy { AllowLoopback = allowLoopback };

            ServiceResult result = WotEndpointValidator.Validate(endpoint, policy, out Uri? normalized);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(normalized, Is.Null);
        }

        [Test]
        public void ValidateAllowsLoopbackWhenPolicyOptsIn()
        {
            var policy = new WotEndpointPolicy { AllowLoopback = true };

            ServiceResult result = WotEndpointValidator.Validate("http://127.0.0.1/admin", policy, out Uri? normalized);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(normalized, Is.Not.Null);
        }

        [Test]
        public void ValidateAllowsPrivateAddressWhenPolicyOptsIn()
        {
            var policy = new WotEndpointPolicy { AllowPrivateAddresses = true };

            ServiceResult result = WotEndpointValidator.Validate("http://10.0.0.1/device", policy, out Uri? normalized);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(normalized, Is.Not.Null);
        }

        [Test]
        public void ValidateAllowedHostsIsExclusive()
        {
            var policy = new WotEndpointPolicy();
            policy.AllowedHosts.Add("allowed.example.com");

            ServiceResult accepted = WotEndpointValidator.Validate(
                "https://allowed.example.com/api", policy, out Uri? normalized);
            ServiceResult rejected = WotEndpointValidator.Validate(
                "https://other.example.com/api", policy, out _);

            Assert.That(ServiceResult.IsGood(accepted), Is.True);
            Assert.That(normalized, Is.Not.Null);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void ValidateBlockedHostsDenyHost()
        {
            var policy = new WotEndpointPolicy();
            policy.BlockedHosts.Add("blocked.example.com");

            ServiceResult result = WotEndpointValidator.Validate("https://blocked.example.com/api", policy, out _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase("relative/path")]
        [TestCase("://missing-scheme")]
        [TestCase("")]
        public void ValidateMalformedOrRelativeUriFailsClosed(string endpoint)
        {
            ServiceResult result = WotEndpointValidator.Validate(endpoint, new WotEndpointPolicy(), out Uri? normalized);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(normalized, Is.Null);
        }
    }
}
