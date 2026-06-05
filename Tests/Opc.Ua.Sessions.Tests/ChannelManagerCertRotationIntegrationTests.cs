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

#nullable enable

// CA2000: ownership of the rotated certificate copy is transferred to CertificateManager.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

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
        [Order(100)]
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

                var firstStates = new ConcurrentQueue<ChannelState>();
                var secondStates = new ConcurrentQueue<ChannelState>();
                first.ChannelStateChanged += (_, e) => firstStates.Enqueue(e.NewState);
                second.ChannelStateChanged += (_, e) => secondStates.Enqueue(e.NewState);

                ICertificateManager certificateManager = ClientFixture.Config.CertificateManager
                    ?? throw new AssertionException("Client configuration must expose a certificate manager.");
                newCertificate = CreateReplacementApplicationCertificate();

                await certificateManager.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    newCertificate.AddRef(),
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
