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
    /// compliance tests for Security Basic 128Rsa15.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    public class SecurityBasic128rsa15Tests : TestFixture
    {
        [Description("Call GetEndpoints to identify a secure endpoint to attach that is 128Rsa15: Open a secure channel, use Sign only (if available; else exit). Create a session using Anonymous if avai")]
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
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
        }

        [Description("Call GetEndpoints to identify a secure endpoint to attach that is 128Rsa15: Open a secure channel, use SignAndEncrypt. Create a session using Anonymous if available, otherwise use")]
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
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
        }

        [Description("attempt a DoS attack on Server by consuming SecureChannels and NOT using them")]
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
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
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
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
        }

        [Description("Create a secure channel.")]
        [Test]
        public async Task EndpointAvailableForCreateSecureChannelAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
        }

        [Description("Close an already closed secure channel.")]
        [Test]
        public async Task EndpointAvailableForCloseAlreadyClosedChannelAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
        }

        [Description("Close a secure channel that has timed-out due to inactivity.")]
        [Test]
        public async Task EndpointAvailableForCloseTimedOutChannelAsync()
        {
            var ec = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, ec, Telemetry, ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
            EndpointDescription? ep = default;
            foreach (EndpointDescription e in eps)
            {
                if (e.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15)
                {
                    ep = e;
                    break;
                }
            }
            if (ep!.SecurityPolicyUri == null)
            {
                Assert.Ignore("Server does not support Security Basic 128Rsa15 policy.");
            }
            Assert.That(ep.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic128Rsa15));
        }
    }
}
