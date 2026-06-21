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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;

// CA2000: contract-test disposables are released by test cleanup paths.
// CA2007: NUnit invokes test code without requiring ConfigureAwait on framework calls.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Layer-1 certificate-rotation contracts for the stress-suite channel-manager fakes.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [Category("ChannelManager")]
    [Category("Certificates")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CertRotationContractTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        [Description("L1-CERT1: certificate rotation reconnects every shared entry across scaled leases.")]
        public async Task L1Cert1ApplicationCertificateUpdateReconnectsEverySharedEntryAsync(
            CancellationToken ct)
        {
            using Certificate oldCertificate = CreateCertificate("l1-cert1-old-client");
            using Certificate newCertificate = CreateCertificate("l1-cert1-new-client");
            await using ContractTestEnvironment environment = CreateEnvironment(oldCertificate);
            FakeParticipant[] participants =
            [
                new(CreateEndpoint("l1-cert1-a")),
                new(CreateEndpoint("l1-cert1-a")),
                new(CreateEndpoint("l1-cert1-b")),
                new(CreateEndpoint("l1-cert1-b")),
                new(CreateEndpoint("l1-cert1-c"))
            ];
            IReadOnlyList<IManagedTransportChannel> leases = [];

            try
            {
                leases = await OpenLeasesAsync(environment.Manager, participants, ct).ConfigureAwait(false);

                Assert.That(environment.CertificateChanges.ObserverCount, Is.EqualTo(1));
                environment.CertificateManagerMock.VerifyGet(
                    manager => manager.CertificateChanges,
                    Times.Once);
                AssertScaledReadyDiagnostics(environment.Manager.GetChannelDiagnostics());
                Assert.That(GetClientCertificateVersion(environment.Manager), Is.EqualTo(1));
                Assert.That(environment.Bindings.Created, Has.Count.EqualTo(3));
                Assert.That(
                    environment.Bindings.Created.Select(transport => transport.ClientCertificateThumbprint),
                    Is.All.EqualTo(oldCertificate.Thumbprint));

                environment.CertificateChanges.Raise(
                    CreateApplicationCertificateUpdatedEvent(oldCertificate, newCertificate));

                await WaitUntilAsync(
                    () => GetClientCertificateVersion(environment.Manager) == 2 &&
                        environment.Bindings.Created.Count == 6 &&
                        participants.All(participant => participant.NotificationCount == 1),
                    "The certificate rotation reconnect did not update all shared entries.",
                    ct).ConfigureAwait(false);
                await WaitUntilAsync(
                    () => GetCertificateRotationTask(environment.Manager) == null,
                    "The certificate rotation worker did not complete.",
                    ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(
                    environment.Manager,
                    DefaultWait,
                    ct: ct).ConfigureAwait(false);

                AssertScaledReadyDiagnostics(environment.Manager.GetChannelDiagnostics());
                Assert.That(
                    participants.Select(participant => participant.NotificationCount),
                    Is.All.EqualTo(1));
                Assert.That(
                    environment.Bindings.Created.Count(
                        transport => transport.ClientCertificateThumbprint == newCertificate.Thumbprint),
                    Is.EqualTo(3));
                await AssertLeasesHealthyAsync(leases, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseLeasesAsync(leases).ConfigureAwait(false);
            }

            await WaitUntilAsync(
                () => environment.Manager.GetChannelDiagnostics().Count == 0,
                "All managed-channel entries should be removed after lease cleanup.",
                ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30_000)]
        [Description("L1-CERT2: certificate rotation during an active reconnect coalesces with the in-flight cycle.")]
        public async Task L1Cert2ApplicationCertificateUpdateDuringReconnectCoalescesAsync(
            CancellationToken ct)
        {
            using Certificate oldCertificate = CreateCertificate("l1-cert2-old-client");
            using Certificate newCertificate = CreateCertificate("l1-cert2-new-client");
            var reconnectBarrier = new ChaosBarrier(expectedParticipants: 2);
            await using ContractTestEnvironment environment = CreateEnvironment(
                oldCertificate,
                uriScheme =>
                {
                    _ = uriScheme;
                    return new FakeTransport { ReconnectBarrier = reconnectBarrier };
                });
            FakeParticipant[] participants =
            [
                new(CreateEndpoint("l1-cert2")),
                new(CreateEndpoint("l1-cert2")),
                new(CreateEndpoint("l1-cert2"))
            ];
            IReadOnlyList<IManagedTransportChannel> leases = [];

            try
            {
                leases = await OpenLeasesAsync(environment.Manager, participants, ct).ConfigureAwait(false);
                FakeTransport transport = environment.Bindings.Created.Single();
                AssertSingleReadyDiagnostic(environment.Manager.GetChannelDiagnostics(), expectedRefcount: 3);

                Task reconnectTask = environment.Manager.ReconnectAllAsync(ct).AsTask();
                await WaitUntilAsync(
                    () => reconnectBarrier.ArrivedCount == 1 &&
                        environment.Manager.GetChannelDiagnostics()
                            .Single().State == ChannelState.TransportReconnecting,
                    "The explicit reconnect did not block on the fake transport barrier.",
                    ct).ConfigureAwait(false);

                environment.CertificateChanges.Raise(
                    CreateApplicationCertificateUpdatedEvent(oldCertificate, newCertificate));
                await WaitUntilAsync(
                    () => GetClientCertificateVersion(environment.Manager) == 2 &&
                        GetCertificateRotationTask(environment.Manager) is { IsCompleted: false },
                    "The certificate rotation did not join the in-flight reconnect.",
                    ct).ConfigureAwait(false);

                await reconnectBarrier.SignalAndWaitAsync(ct).ConfigureAwait(false);
                await reconnectTask.WaitAsync(DefaultWait, ct).ConfigureAwait(false);
                await WaitUntilAsync(
                    () => GetCertificateRotationTask(environment.Manager) == null,
                    "The certificate rotation worker did not complete after the reconnect barrier released.",
                    ct).ConfigureAwait(false);
                await WaitForQuiescence.ForManagerAsync(
                    environment.Manager,
                    DefaultWait,
                    ct: ct).ConfigureAwait(false);

                Assert.That(environment.Bindings.Created, Has.Count.EqualTo(1));
                Assert.That(transport.ReconnectCount, Is.EqualTo(1));
                Assert.That(transport.CloseCount, Is.Zero);
                Assert.That(transport.DisposeCount, Is.Zero);
                Assert.That(
                    participants.Select(participant => participant.NotificationCount),
                    Is.All.EqualTo(1));
                AssertSingleReadyDiagnostic(environment.Manager.GetChannelDiagnostics(), expectedRefcount: 3);
                await AssertLeasesHealthyAsync(leases, ct).ConfigureAwait(false);

                await CloseLeasesAsync(leases).ConfigureAwait(false);
                leases = [];
                await WaitUntilAsync(
                    () => environment.Manager.GetChannelDiagnostics().Count == 0,
                    "The shared managed-channel entry should be removed after all leases are closed.",
                    ct).ConfigureAwait(false);

                Assert.That(transport.CloseCount, Is.EqualTo(1));
                Assert.That(transport.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await CloseLeasesAsync(leases).ConfigureAwait(false);
            }
        }

        private static void AssertScaledReadyDiagnostics(
            IReadOnlyList<ManagedChannelDiagnostic> diagnostics)
        {
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics, Has.Count.EqualTo(3));
                Assert.That(
                    diagnostics.Select(diagnostic => diagnostic.Refcount).OrderBy(count => count),
                    Is.EqualTo(s_scaledReadyRefcounts));
                Assert.That(
                    diagnostics.Select(diagnostic => diagnostic.ParticipantCount).OrderBy(count => count),
                    Is.EqualTo(s_scaledReadyParticipantCounts));
                Assert.That(
                    diagnostics.Select(diagnostic => diagnostic.State),
                    Is.All.EqualTo(ChannelState.Ready));
            });
        }

        private static void AssertSingleReadyDiagnostic(
            IReadOnlyList<ManagedChannelDiagnostic> diagnostics,
            int expectedRefcount)
        {
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            ManagedChannelDiagnostic diagnostic = diagnostics.Single();
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Refcount, Is.EqualTo(expectedRefcount));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(expectedRefcount));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
            });
        }

        private static async Task AssertLeasesHealthyAsync(
            IReadOnlyList<IManagedTransportChannel> leases,
            CancellationToken ct)
        {
            for (int index = 0; index < leases.Count; index++)
            {
                IManagedTransportChannel lease = leases[index];
                Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));

                IServiceResponse response = await lease.SendRequestAsync(
                    CreateReadServerStatusRequest(),
                    ct).ConfigureAwait(false);
                Assert.That(response, Is.TypeOf<ReadResponse>());
                var readResponse = (ReadResponse)response;
                Assert.That(
                    StatusCode.IsGood(readResponse.ResponseHeader.ServiceResult),
                    Is.True,
                    $"Lease {index} returned {readResponse.ResponseHeader.ServiceResult}.");
                Assert.That(readResponse.Results, Has.Count.EqualTo(1));
                Assert.That(readResponse.Results[0].IsNull, Is.False);
                Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            }
        }

        private static ReadRequest CreateReadServerStatusRequest()
        {
            return new ReadRequest
            {
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                ],
                TimestampsToReturn = TimestampsToReturn.Neither
            };
        }

        private static readonly int[] s_scaledReadyRefcounts = [1, 2, 2];
        private static readonly int[] s_scaledReadyParticipantCounts = [1, 2, 2];
    }
}
