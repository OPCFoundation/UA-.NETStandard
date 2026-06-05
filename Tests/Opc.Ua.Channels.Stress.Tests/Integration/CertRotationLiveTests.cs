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

#nullable enable

// CA2000: ownership of the rotated certificate copy is transferred to CertificateManager.
// CA2016: outage reconnects intentionally mirror CertificateManager's uncancelled reconnect path.
#pragma warning disable CA2000, CA2007, CA2016

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Helpers;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Channels.Stress.Tests.Integration
{
    /// <summary>
    /// L2 live-server certificate-rotation tests for shared channel-manager sessions.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("ChannelManager")]
    [Category("Certificates")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CertRotationLiveTests : IntegrationTestBase
    {
        [Test]
        [Order(100)]
        [CancelAfter(180_000)]
        [Description("L2-CERT1: rotate the client application certificate while shared sessions stay active.")]
        public async Task L2Cert1RotateCertificateWhileSessionsActiveReconnectsSharedChannelAsync(
            CancellationToken ct)
        {
            using MetricsCollector metrics = new();
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 3
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            var sessions = new List<ManagedSessionType>(SessionCount);
            Certificate? newCertificate = null;

            try
            {
                sessions = await ConnectManagedSessionsAsync(
                    endpoint,
                    manager,
                    nameof(L2Cert1RotateCertificateWhileSessionsActiveReconnectsSharedChannelAsync),
                    SessionCount,
                    ct).ConfigureAwait(false);
                ManagedChannelKey key = AssertSharedReadyChannel(sessions, manager);
                newCertificate = CreateReplacementApplicationCertificate();

                await RotateApplicationCertificateAsync(newCertificate, ct).ConfigureAwait(false);

                await AssertReconnectAttemptRecordedAsync(metrics, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);
                AssertSessionsReadyAndShared(sessions, manager, key);
                await AssertSessionsCanReadServerStatusAsync(sessions, ct).ConfigureAwait(false);
                await AssertSessionsUseClientCertificateAsync(sessions, newCertificate.Thumbprint, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAllAsync(sessions).ConfigureAwait(false);
                newCertificate?.Dispose();
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(180_000)]
        [Description("L2-CERT2: rotate the client application certificate while server restart recovery is pending.")]
        public async Task L2Cert2RotateCertificateDuringServerRestartRecoversSharedChannelAsync(
            CancellationToken ct)
        {
            using MetricsCollector metrics = new();
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    MaxAttempts = 120
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            var sessions = new List<ManagedSessionType>(SessionCount);
            Certificate? newCertificate = null;
            int port = ServerFixturePort;
            bool serverStopped = false;

            try
            {
                sessions = await ConnectManagedSessionsAsync(
                    endpoint,
                    manager,
                    nameof(L2Cert2RotateCertificateDuringServerRestartRecoversSharedChannelAsync),
                    SessionCount,
                    ct).ConfigureAwait(false);
                ManagedChannelKey key = AssertSharedReadyChannel(sessions, manager);

                await ServerFixture.StopAsync().ConfigureAwait(false);
                serverStopped = true;

                Task reconnectTask = manager.ReconnectAllAsync(CancellationToken.None).AsTask();
                Assert.That(
                    await WaitForChannelStateAsync(
                        manager,
                        key,
                        ChannelState.TransportReconnecting,
                        TimeSpan.FromSeconds(10),
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The shared channel should enter the reconnect retry loop while the server is stopped.");

                newCertificate = CreateReplacementApplicationCertificate();
                await RotateApplicationCertificateAsync(newCertificate, ct).ConfigureAwait(false);
                manager.UpdateClientCertificate(newCertificate.AddRef(), clientCertificateChain: null);

                await RestartServerAsync(port).ConfigureAwait(false);
                serverStopped = false;
                await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);

                await AssertReconnectAttemptRecordedAsync(metrics, ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(manager, DefaultWait, ct: ct)
                    .ConfigureAwait(false);
                await RecreateSessionsInPlaceAsync(sessions, ct).ConfigureAwait(false);
                AssertSessionsReadyAndShared(sessions, manager, key);
                await AssertSessionsCanReadServerStatusAsync(sessions, ct).ConfigureAwait(false);
                await AssertSessionsUseClientCertificateAsync(sessions, newCertificate.Thumbprint, ct)
                    .ConfigureAwait(false);

                await CloseAndDisposeAllAsync(sessions).ConfigureAwait(false);
                Assert.That(
                    await WaitForQuiescence.EntryGoneAsync(manager, key, DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True,
                    "The recovered shared channel entry should be removed after all sessions are released.");
                Assert.That(
                    GetMetricTotal(metrics, ChannelCloseMetric),
                    Is.LessThanOrEqualTo(2.0d),
                    "Double perturbation (cert rotation + server restart) may cause at most one " +
                    "teardown from each event; observed teardown count should not exceed 2.");
            }
            finally
            {
                if (serverStopped)
                {
                    await RestartServerAsync(port).ConfigureAwait(false);
                }

                await CloseAndDisposeAllAsync(sessions).ConfigureAwait(false);
                newCertificate?.Dispose();
            }
        }

        private Certificate CreateReplacementApplicationCertificate()
        {
            string applicationName = ClientFixture.Config.ApplicationName
                ?? nameof(CertRotationLiveTests);
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

        private async Task RotateApplicationCertificateAsync(
            Certificate newCertificate,
            CancellationToken ct)
        {
            ICertificateManager certificateManager = ClientFixture.Config.CertificateManager
                ?? throw new AssertionException("Client configuration must expose a certificate manager.");

            await certificateManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                newCertificate.AddRef(),
                issuerChain: null,
                ct).ConfigureAwait(false);
        }

        private async Task RestartServerAsync(int port)
        {
            ReferenceServer = await ServerFixture.StartAsync(PkiRoot, port).ConfigureAwait(false);
            ReferenceServer.TokenValidator = TokenValidator;
            ServerFixturePort = ServerFixture.Port;
        }

        private async Task AssertSessionsUseClientCertificateAsync(
            List<ManagedSessionType> sessions,
            string expectedThumbprint,
            CancellationToken ct)
        {
            Assert.That(
                await WaitForAsync(
                    () => sessions.All(session => HasClientChannelCertificate(session, expectedThumbprint)),
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The rehandshaken secure channel should present the replacement client certificate.");
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

        private static ManagedChannelKey AssertSharedReadyChannel(
            List<ManagedSessionType> sessions,
            ClientChannelManager manager)
        {
            Assert.That(sessions, Has.Count.EqualTo(SessionCount));
            IManagedTransportChannel firstChannel = GetManagedChannel(sessions[0]);
            for (int i = 1; i < sessions.Count; i++)
            {
                Assert.That(GetManagedChannel(sessions[i]).Key, Is.EqualTo(firstChannel.Key));
            }

            ManagedChannelDiagnostic diagnostic = GetDiagnostic(manager, firstChannel.Key);
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Refcount, Is.EqualTo(SessionCount));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(SessionCount));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
            });

            return firstChannel.Key;
        }

        private static void AssertSessionsReadyAndShared(
            List<ManagedSessionType> sessions,
            ClientChannelManager manager,
            ManagedChannelKey key)
        {
            foreach (ManagedSessionType session in sessions)
            {
                IManagedTransportChannel channel = GetManagedChannel(session);
                Assert.Multiple(() =>
                {
                    Assert.That(channel.Key, Is.EqualTo(key));
                    Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                });
            }

            ManagedChannelDiagnostic diagnostic = GetDiagnostic(manager, key);
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Refcount, Is.EqualTo(SessionCount));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(SessionCount));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
            });
        }

        private static async Task AssertSessionsCanReadServerStatusAsync(
            List<ManagedSessionType> sessions,
            CancellationToken ct)
        {
            foreach (ManagedSessionType session in sessions)
            {
                DataValue value = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus, ct)
                    .ConfigureAwait(false);

                Assert.That(value.IsNull, Is.False);
                Assert.That(
                    StatusCode.IsGood(value.StatusCode),
                    Is.True,
                    $"ServerStatus read returned {value.StatusCode}.");
            }
        }

        private static async Task AssertReconnectAttemptRecordedAsync(
            MetricsCollector metrics,
            CancellationToken ct)
        {
            Assert.That(
                await WaitForAsync(
                    () => GetMetricTotal(metrics, ReconnectAttemptsMetric) >= 1.0d,
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                "The channel-manager reconnect-attempt counter should record certificate rotation recovery.");
        }

        private static async Task RecreateSessionsInPlaceAsync(
            List<ManagedSessionType> sessions,
            CancellationToken ct)
        {
            foreach (ManagedSessionType session in sessions)
            {
                object? task = s_recreateInPlaceAsync.Invoke(
                    GetInnerSession(session),
                    [null, null, GetManagedChannel(session), ct]);
                await ((Task)task!).ConfigureAwait(false);
            }
        }

        private static async Task CloseAndDisposeAllAsync(
            List<ManagedSessionType> sessions)
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                await CloseAndDisposeAsync(sessions[i]).ConfigureAwait(false);
            }

            sessions.Clear();
        }

        private static double GetMetricTotal(
            MetricsCollector metrics,
            string metricName)
        {
            return metrics.Measurements
                .Where(measurement => string.Equals(measurement.Name, metricName, StringComparison.Ordinal))
                .Sum(measurement => measurement.Value);
        }

        private const int SessionCount = 3;
        private const string ChannelCloseMetric = "opcua.channel.close";
        private const string ReconnectAttemptsMetric = "opcua.channel.reconnect.attempts";

        private static readonly MethodInfo s_recreateInPlaceAsync =
            typeof(Session).GetMethod(
                "RecreateInPlaceAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [
                    typeof(ConfiguredEndpoint),
                    typeof(ITransportWaitingConnection),
                    typeof(ITransportChannel),
                    typeof(CancellationToken)
                ],
                null) ??
            throw new InvalidOperationException("Session.RecreateInPlaceAsync reflection hook was not found.");
    }
}
