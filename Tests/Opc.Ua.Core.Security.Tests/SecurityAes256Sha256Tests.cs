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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for Security Aes256 Sha256.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    public class SecurityAes256Sha256Tests : TestFixture
    {
        [Description("Call GetEndpoints to identify a secure endpoint to attach that is Aes256-Sha256-RsaPss: Open a secure channel, use Sign only (if available; else exit). Create a session using Anony")]
        [Test]
        public async Task EndpointsAdvertiseSignSecurityModeAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Description("Call GetEndpoints to identify a secure endpoint to attach that is 256Sha256: Open a secure channel, use SignAndEncrypt. Create a session using Anonymous if available, otherwise use")]
        [Test]
        public async Task EndpointsAdvertiseSignAndEncryptSecurityModeAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Test]
        public async Task EndpointAvailableForUnusedChannelDosAttackAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Test]
        public async Task EndpointAvailableForPartiallyUsedChannelDosAttackAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Description("Create a secure channel.")]
        [Test]
        public async Task EndpointAvailableForCreateSecureChannelAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Description("Close an already closed secure channel.")]
        [Test]
        public async Task EndpointAvailableForCloseAlreadyClosedChannelAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        [Description("Close a secure channel that has timed-out due to inactivity.")]
        [Test]
        public async Task EndpointAvailableForCloseTimedOutChannelAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Aes256 Sha256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes256_Sha256_RsaPss));
        }
    }
}
