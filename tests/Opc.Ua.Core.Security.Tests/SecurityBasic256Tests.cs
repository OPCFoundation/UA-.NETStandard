// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for Security Basic 256.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    public class SecurityBasic256Tests : TestFixture
    {
        [Description("Call GetEndpoints to identify a secure endpoint to attach that is 256: Open a secure channel, use Sign only (if available; else exit). Create a session using Anonymous if available")]
        [Test]
        public async Task EndpointsAdvertiseSignSecurityModeAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic256)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256));
        }

        [Description("Call GetEndpoints to identify a secure endpoint to attach that is 256: Open a secure channel, use SignAndEncrypt. Create a session using Anonymous if available, otherwise use UserN")]
        [Test]
        public async Task EndpointsAdvertiseSignAndEncryptSecurityModeAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic256)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256));
        }

        [Description("Attempt a DoS attack on Server by consuming SecureChannels and NOT using them")]
        [Test]
        public async Task EndpointAvailableForUnusedChannelDosAttackAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic256)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256));
        }

        [Description("Attempt a DoS attack on Server by consuming SecureChannels and using only SOME of them")]
        [Test]
        public async Task EndpointAvailableForPartiallyUsedChannelDosAttackAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic256)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 256 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256));
        }
    }
}
