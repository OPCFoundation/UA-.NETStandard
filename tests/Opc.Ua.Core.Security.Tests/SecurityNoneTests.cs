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
    /// compliance tests for Security None.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    public class SecurityNoneTests : TestFixture
    {
        [Description("Attempt to open an insecure channel while providing certificates and nonces.")]
        [Test]
        public async Task InsecureEndpointAdvertisedWithCertsAndNoncesAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Description("Attempt to open an insecure channel while providing a ClientNonce, but do not pass any certificates.")]
        [Test]
        public async Task InsecureEndpointAdvertisedWithNonceOnlyAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Description("Attempt to open an insecure channel while providing client certificates, but do not pass a ClientNonce.")]
        [Test]
        public async Task InsecureEndpointAdvertisedWithClientCertOnlyAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Description("Attempt to open an insecure channel, omitting a ClientNonce and client certificates.")]
        [Test]
        public async Task InsecureEndpointAdvertisedWithoutNonceOrCertAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Description("Attempt to open an insecure channel while providing an invalid certificate.")]
        [Test]
        public async Task InsecureEndpointAdvertisedWithInvalidCertificateAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Description("per Errata 1.02.2: attempt a DoS attack on Server by consuming SecureChannels and NOT using them! When creating a valid/real SecureChannel, prior [unused] channels should be clobbe")]
        [Test]
        public async Task InsecureEndpointAvailableForUnusedChannelDosAttackAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Description("per Errata 1.02.2: attempt a DoS attack on Server by consuming SecureChannels and using only SOME of them! When creating a valid/real SecureChannel, prior [unused] channels should")]
        [Test]
        public async Task InsecureEndpointAvailableForPartiallyUsedChannelDosAttackAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.None)
                {
                    ep = e;
                    break;
                }
            }
            if (ep.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security None policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }
    }
}
