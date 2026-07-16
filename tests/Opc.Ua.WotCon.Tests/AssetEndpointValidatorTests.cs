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
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Table-driven coverage of <see cref="AssetEndpointValidator"/>
    /// against the safe-default policy plus the explicit-override
    /// matrices required for operational use.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AssetEndpointValidatorTests
    {
        [TestCase("https://example.com/td")]
        [TestCase("http://example.com/td")]
        [TestCase("opc.tcp://device:4840")]
        [TestCase("opc.tcp://203.0.113.5:4840")]   // TEST-NET-3 (RFC5737), routable
        [TestCase("https://[2001:db8::1]/td")]     // documentation prefix (RFC3849)
        public void DefaultPolicy_AcceptsRoutableSchemes(string endpoint)
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(endpoint, policy, out Uri? uri);

            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"'{endpoint}' should pass; got {result}");
            Assert.That(uri, Is.Not.Null);
            Assert.That(uri!.IsAbsoluteUri, Is.True);
        }

        [TestCase("file:///etc/passwd")]
        [TestCase("gopher://x/")]
        [TestCase("ftp://example.com/x")]
        [TestCase("javascript:alert(1)")]
        [TestCase("data:text/plain,hi")]
        public void DefaultPolicy_RejectsSchemeNotInAllowList(string endpoint)
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(endpoint, policy, out Uri? _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not a url")]
        public void InvalidUriSyntax_ReturnsBadInvalidArgument(string? endpoint)
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(endpoint, policy, out Uri? _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("http://127.0.0.1/")]
        [TestCase("http://127.10.0.1/")]
        [TestCase("https://[::1]/td")]
        [TestCase("opc.tcp://localhost:4840")]
        [TestCase("opc.tcp://LocalHost:4840")]      // case-insensitive
        [TestCase("http://ip6-localhost/")]
        public void DefaultPolicy_RejectsLoopback(string endpoint)
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(endpoint, policy, out Uri? _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed),
                $"Loopback '{endpoint}' should be rejected by default");
        }

        [TestCase("http://10.0.0.1/")]              // RFC1918 10/8
        [TestCase("http://10.255.255.255/")]
        [TestCase("http://172.16.0.1/")]            // RFC1918 172.16/12 low
        [TestCase("http://172.31.255.255/")]        // RFC1918 172.16/12 high
        [TestCase("http://192.168.1.1/")]           // RFC1918 192.168/16
        [TestCase("http://169.254.0.1/")]           // RFC3927 link-local
        [TestCase("http://169.254.169.254/latest/meta-data/")] // AWS/Azure IMDS
        [TestCase("http://[fc00::1]/")]             // RFC4193 ULA
        [TestCase("http://[fd00::1]/")]             // RFC4193 ULA
        [TestCase("http://[fe80::1]/")]             // IPv6 link-local
        public void DefaultPolicy_RejectsPrivateAddresses_IncludingImds(string endpoint)
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(endpoint, policy, out Uri? _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed),
                $"Private address '{endpoint}' should be rejected by default");
        }

        [TestCase("http://172.15.0.1/")]            // 172.15.x.x — public, just below RFC1918
        [TestCase("http://172.32.0.1/")]            // 172.32.x.x — public, just above RFC1918
        [TestCase("http://11.0.0.1/")]              // 11.x.x.x — public
        [TestCase("http://192.169.0.1/")]           // 192.169.x.x — public
        [TestCase("http://[2001:db8::1]/")]         // 2001:db8::/32 — public docs
        public void DefaultPolicy_DoesNotOverReachAdjacentPublicAddresses(string endpoint)
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(endpoint, policy, out Uri? uri);

            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"'{endpoint}' is public and must pass; got {result}");
            Assert.That(uri, Is.Not.Null);
        }

        [Test]
        public void AllowLoopback_PermitsLoopbackWhenSet()
        {
            var policy = new AssetEndpointPolicy { AllowLoopback = true };
            ServiceResult result = AssetEndpointValidator.Validate(
                "http://127.0.0.1/", policy, out Uri? _);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AllowPrivateAddresses_PermitsImdsWhenSet()
        {
            var policy = new AssetEndpointPolicy { AllowPrivateAddresses = true };
            ServiceResult result = AssetEndpointValidator.Validate(
                "http://169.254.169.254/", policy, out Uri? _);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AllowedHosts_ActAsExclusiveAllowList()
        {
            var policy = new AssetEndpointPolicy();
            policy.AllowedHosts.Add("device.local");

            ServiceResult ok = AssetEndpointValidator.Validate(
                "https://device.local/td", policy, out Uri? _);
            ServiceResult denied = AssetEndpointValidator.Validate(
                "https://example.com/td", policy, out Uri? _);

            Assert.That(ServiceResult.IsGood(ok), Is.True);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void BlockedHosts_DenyEvenWhenSchemeAndIpPass()
        {
            var policy = new AssetEndpointPolicy();
            policy.BlockedHosts.Add("bad.example.com");

            ServiceResult result = AssetEndpointValidator.Validate(
                "https://bad.example.com/td", policy, out Uri? _);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void BlockedHosts_LayerOnTopOfAllowedHosts()
        {
            var policy = new AssetEndpointPolicy();
            policy.AllowedHosts.Add("device.local");
            policy.AllowedHosts.Add("bad.example.com");
            policy.BlockedHosts.Add("bad.example.com");

            ServiceResult ok = AssetEndpointValidator.Validate(
                "https://device.local/td", policy, out Uri? _);
            ServiceResult denied = AssetEndpointValidator.Validate(
                "https://bad.example.com/td", policy, out Uri? _);

            Assert.That(ServiceResult.IsGood(ok), Is.True);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void NullPolicy_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AssetEndpointValidator.Validate("https://example.com/", null!, out _));
        }

        [Test]
        public void NormalizedUri_IsAbsoluteAndPreservesScheme()
        {
            var policy = new AssetEndpointPolicy();
            ServiceResult result = AssetEndpointValidator.Validate(
                "OPC.TCP://Device:4840/path", policy, out Uri? uri);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(uri, Is.Not.Null);
            Assert.That(uri!.Scheme, Is.EqualTo("opc.tcp"));
            Assert.That(uri.Host, Is.EqualTo("device"));
        }
    }
}
