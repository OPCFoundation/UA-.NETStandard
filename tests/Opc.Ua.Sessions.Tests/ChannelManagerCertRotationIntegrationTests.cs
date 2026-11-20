// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2007, CA2016

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Live-server tests for channel-manager client certificate rotation.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("ChannelManager")]
    [Category("Certificates")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ChannelManagerCertRotationIntegrationTests
        : ChannelManagerIntegrationTestBase
    {
        [Test]
        [CancelAfter(180_000)]
        public async Task ApplicationCertificateRotationRehandshakesOpenManagedChannelsAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 3
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            ManagedSessionType? first = null;
            ManagedSessionType? second = null;
            Certificate? newCertificate = null;

            try
            {
                first = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ApplicationCertificateRotationRehandshakesOpenManagedChannelsAsync) + "1",
                    ct).ConfigureAwait(false);
                second = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ApplicationCertificateRotationRehandshakesOpenManagedChannelsAsync) + "2",
                    ct).ConfigureAwait(false);
                NodeId firstSessionId = first.SessionId;
                NodeId secondSessionId = second.SessionId;

                var firstStates = new ConcurrentQueue<ChannelState>();
                var secondStates = new ConcurrentQueue<ChannelState>();
                first.ChannelStateChanged += (_, e) => firstStates.Enqueue(e.NewState);
                second.ChannelStateChanged += (_, e) => secondStates.Enqueue(e.NewState);

                ICertificateManager certificateManager = ClientFixture.Config.CertificateManager
                    ?? throw new AssertionException("Client configuration must expose a certificate manager.");
                newCertificate = CreateReplacementApplicationCertificate();

                await certificateManager.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    newCertificate,
                    issuerChain: null,
                    ct).ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                        () => SawReconnectCycle(firstStates) && SawReconnectCycle(secondStates),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Both managed-session leases should observe the certificate-rotation reconnect cycle.");

                Assert.That(
                    await WaitForAsync(
                        () => HasClientChannelCertificate(first, newCertificate.Thumbprint) &&
                            HasClientChannelCertificate(second, newCertificate.Thumbprint),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The rehandshaken secure channel should present the replacement client certificate.");

                Assert.That(first.SessionId, Is.Not.EqualTo(firstSessionId));
                Assert.That(second.SessionId, Is.Not.EqualTo(secondSessionId));
                await AssertReadServerStatusAsync(first, ct).ConfigureAwait(false);
                await AssertReadServerStatusAsync(second, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(second).ConfigureAwait(false);
                await CloseAndDisposeAsync(first).ConfigureAwait(false);
                newCertificate?.Dispose();
            }
        }

        private Certificate CreateReplacementApplicationCertificate()
        {
            string applicationName = ClientFixture.Config.ApplicationName
                ?? nameof(ChannelManagerCertRotationIntegrationTests);
            string applicationUri = ClientFixture.Config.ApplicationUri
                ?? $"urn:localhost:{applicationName}";
            string subjectName = $"CN={applicationName}, O=OPC Foundation, DC=localhost";

            return DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    applicationUri,
                    applicationName,
                    subjectName,
                    ["localhost"])
                .SetLifeTime(12)
                .CreateForRSA();
        }

        private bool HasClientChannelCertificate(
            ManagedSessionType session,
            string expectedThumbprint)
        {
            byte[] rawCertificate = GetManagedChannel(session).ClientChannelCertificate;
            if (rawCertificate.Length == 0)
            {
                return false;
            }

            using Certificate certificate = Utils.ParseCertificateBlob(
                rawCertificate,
                Telemetry);
            return string.Equals(
                certificate.Thumbprint,
                expectedThumbprint,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool SawReconnectCycle(
            ConcurrentQueue<ChannelState> states)
        {
            return states.Contains(ChannelState.TransportReconnecting) &&
                states.Contains(ChannelState.TransportConnectedSessionReactivating) &&
                states.Contains(ChannelState.Ready);
        }
    }
}
