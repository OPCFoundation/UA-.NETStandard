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
