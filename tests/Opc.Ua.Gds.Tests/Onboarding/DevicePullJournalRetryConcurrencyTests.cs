// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises receipt retries against the real, locally leased pull journal.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullJournalRetryConcurrencyTests
    {
        /// <summary>
        /// Creates public request evidence from an actual RSA key, CSR and issuer chain.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                Path.GetTempPath(), "pull-journal-retry-" + Guid.NewGuid().ToString("N"));
            m_telemetry = NUnitTelemetryContext.Create();
            (m_binding, m_receipt) = CreateMaterial();
        }

        /// <summary>
        /// Removes only the directory belonging to this test, after writer leases have ended.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// A lost callback outcome is retried against the same request after recreating the transport and writer.
        /// </summary>
        [Test]
        public async Task ReceivingInterruptedCallbackCanRetrySameIdAfterRecreation()
        {
            await using var session = CreateSession();
            NodeId startMethod = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                session.MessageContext.NamespaceUris);
            NodeId finishMethod = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_FinishRequest,
                session.MessageContext.NamespaceUris);
            int starts = 0;
            int finishes = 0;
            int callbacks = 0;
            session.Handler = (request, _) =>
            {
                if (request is not CallRequest call || call.MethodsToCall.Count != 1)
                {
                    throw new AssertionException("Unexpected registrar transport request.");
                }
                CallMethodRequest method = call.MethodsToCall[0];
                Assert.That(method.ObjectId, Is.EqualTo(m_binding.Registrar.CertificateDirectoryId));
                if (method.MethodId == startMethod)
                {
                    Interlocked.Increment(ref starts);
                    Assert.That(method.InputArguments.Count, Is.EqualTo(4));
                    Assert.That(method.InputArguments[0].TryGetValue(out NodeId applicationId), Is.True);
                    Assert.That(applicationId, Is.EqualTo(m_binding.ApplicationId));
                    Assert.That(method.InputArguments[3].TryGetValue(out ByteString csr), Is.True);
                    Assert.That(csr.ToArray(), Is.EqualTo(m_binding.SigningRequest.ToArray()));
                    return Reply(Variant.From(m_binding.RequestId));
                }
                if (method.MethodId == finishMethod)
                {
                    Interlocked.Increment(ref finishes);
                    Assert.That(method.InputArguments.Count, Is.EqualTo(2));
                    Assert.That(method.InputArguments[0].TryGetValue(out NodeId applicationId), Is.True);
                    Assert.That(method.InputArguments[1].TryGetValue(out NodeId requestId), Is.True);
                    Assert.That(applicationId, Is.EqualTo(m_binding.ApplicationId));
                    Assert.That(requestId, Is.EqualTo(m_binding.RequestId));
                    return Reply(
                        Variant.From(m_receipt.Certificate),
                        Variant.From(ByteString.Empty),
                        Variant.From(m_receipt.IssuerCertificates));
                }
                throw new AssertionException("Unexpected certificate directory method.");
            };

            PullDcaSigningBinding retained;
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                var journal = CreateJournal(files);
                var connection = CreateConnection(session, journal);
                NodeId requestId = await connection.StartWithEvidenceAsync(
                    m_binding.ApplicationId, m_binding.Profile, m_binding.SigningRequest,
                    m_binding.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false);
                Assert.That(requestId, Is.EqualTo(m_binding.RequestId));
                DevicePullJournalCheckpoint? started = await journal.ReadAsync().ConfigureAwait(false);
                Assert.That(started, Is.Not.Null);
                retained = started!.Binding;
                Assert.That(retained.AttemptId, Is.Not.EqualTo(Guid.Empty));
                AssertPending(started, DevicePullJournalStage.Started, retained);
                Assert.That(retained.SigningRequest.ToArray(), Is.EqualTo(m_binding.SigningRequest.ToArray()));
                Assert.That(retained.ClientChannelCertificate.ToArray(),
                    Is.EqualTo(m_binding.ClientChannelCertificate.ToArray()));
                Assert.That(retained.RegistrarChannelCertificate.ToArray(),
                    Is.EqualTo(m_binding.RegistrarChannelCertificate.ToArray()));

                var entered = Barrier();
                var release = Barrier();
                var interrupted = new IOException("The Finish result was interrupted before journal publication.");
                Task<PullDcaCertificateReceipt> first = journal.GetOrCreateAsync(
                    retained.Registrar, retained.ApplicationId, retained.RequestId, retained.Profile,
                    async ct =>
                    {
                        Interlocked.Increment(ref callbacks);
                        var (certificate, issuers) = await connection.FinishAsync(
                            retained.ApplicationId, retained.RequestId, ct).ConfigureAwait(false);
                        AssertReceipt(new PullDcaCertificateReceipt(certificate, issuers), m_receipt);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(s_barrierTimeout, CancellationToken.None).ConfigureAwait(false);
                        throw interrupted;
                    }).AsTask();
                try
                {
                    await WaitForCallbackAsync(entered.Task, first).ConfigureAwait(false);
                    AssertPending(
                        await journal.ReadAsync().AsTask().WaitAsync(s_barrierTimeout).ConfigureAwait(false),
                        DevicePullJournalStage.Receiving, retained);
                    Assert.That(Volatile.Read(ref starts), Is.EqualTo(1));
                    Assert.That(Volatile.Read(ref finishes), Is.EqualTo(1));
                }
                finally
                {
                    release.TrySetResult(true);
                    await ObserveCompletionAsync(first).ConfigureAwait(false);
                }
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.ConfigureAwait(false));
                Assert.That(error, Is.SameAs(interrupted));
                AssertPending(
                    await journal.ReadAsync().ConfigureAwait(false),
                    DevicePullJournalStage.Receiving, retained);
            }

            using var reopened = new PushRegistrarCheckpointFiles(m_directory);
            var resumed = CreateJournal(reopened);
            await using var resumedSession = CreateSession();
            resumedSession.Handler = session.Handler;
            Assert.That(resumedSession.Channel.Object, Is.Not.SameAs(session.Channel.Object));
            var resumedConnection = CreateConnection(resumedSession, resumed);
            ServiceResultException? duplicateStart = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resumedConnection.StartWithEvidenceAsync(
                    retained.ApplicationId, retained.Profile, retained.SigningRequest,
                    retained.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));
            Assert.That(duplicateStart!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Volatile.Read(ref starts), Is.EqualTo(1));
            AssertPending(
                await resumed.ReadAsync().ConfigureAwait(false),
                DevicePullJournalStage.Receiving, retained);
            PullDcaSigningBinding resolved = await resumed.ResolveAsync(
                retained.Registrar, retained.ApplicationId, retained.RequestId, retained.NamespaceUris)
                .ConfigureAwait(false);
            Assert.That(resolved.AttemptId, Is.EqualTo(retained.AttemptId));
            Assert.That(resolved.RequestId, Is.EqualTo(retained.RequestId));

            PullDcaCertificateReceipt result = await resumed.GetOrCreateAsync(
                retained.Registrar, retained.ApplicationId, retained.RequestId, retained.Profile,
                async ct =>
                {
                    Interlocked.Increment(ref callbacks);
                    var (certificate, issuers) = await resumedConnection.FinishAsync(
                        retained.ApplicationId, retained.RequestId, ct).ConfigureAwait(false);
                    return new PullDcaCertificateReceipt(certificate, issuers);
                }).ConfigureAwait(false);

            AssertReceipt(result, m_receipt);
            AssertPublished(await resumed.ReadAsync().ConfigureAwait(false), retained, m_receipt);
            Assert.That(Volatile.Read(ref starts), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref finishes), Is.EqualTo(2));
            Assert.That(Volatile.Read(ref callbacks), Is.EqualTo(2));
        }

        /// <summary>
        /// A pending Finish rolls Receiving back to Started and the same request can be retried.
        /// </summary>
        [Test]
        public async Task ReceivingPendingRollsBackAndCanRetrySameRequest()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var journal = CreateJournal(files);
            await PrepareStartedAsync(journal, m_binding).ConfigureAwait(false);
            var entered = Barrier();
            var release = Barrier();
            int callbacks = 0;
            Task<PullDcaCertificateReceipt> pending = journal.GetOrCreateAsync(
                m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                async _ =>
                {
                    Interlocked.Increment(ref callbacks);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_barrierTimeout, CancellationToken.None).ConfigureAwait(false);
                    throw new PullDcaCertificatePendingException(m_binding.ApplicationId, m_binding.RequestId);
                }).AsTask();
            try
            {
                await WaitForCallbackAsync(entered.Task, pending).ConfigureAwait(false);
                AssertPending(
                    await journal.ReadAsync().AsTask().WaitAsync(s_barrierTimeout).ConfigureAwait(false),
                    DevicePullJournalStage.Receiving, m_binding);
            }
            finally
            {
                release.TrySetResult(true);
                await ObserveCompletionAsync(pending).ConfigureAwait(false);
            }
            PullDcaCertificatePendingException? error =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await pending.ConfigureAwait(false));
            Assert.That(error!.ApplicationId, Is.EqualTo(m_binding.ApplicationId));
            Assert.That(error.RequestId, Is.EqualTo(m_binding.RequestId));
            AssertPending(
                await journal.ReadAsync().ConfigureAwait(false),
                DevicePullJournalStage.Started, m_binding);

            PullDcaCertificateReceipt receipt = await journal.GetOrCreateAsync(
                m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                _ =>
                {
                    Interlocked.Increment(ref callbacks);
                    return new ValueTask<PullDcaCertificateReceipt>(m_receipt);
                }).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref callbacks), Is.EqualTo(2));
            AssertReceipt(receipt, m_receipt);
            AssertPublished(await journal.ReadAsync().ConfigureAwait(false), m_binding, m_receipt);
        }

        /// <summary>
        /// Cancellation after callback entry leaves Receiving, not Started, for an explicit retry.
        /// </summary>
        [Test]
        public async Task ReceivingCancellationRemainsRetryable()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var journal = CreateJournal(files);
            await PrepareStartedAsync(journal, m_binding).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var entered = Barrier();
            var release = Barrier();
            CancellationToken observedToken = default;
            int callbacks = 0;
            Task<PullDcaCertificateReceipt> cancelled = journal.GetOrCreateAsync(
                m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                async ct =>
                {
                    Interlocked.Increment(ref callbacks);
                    observedToken = ct;
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_barrierTimeout, CancellationToken.None).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    return m_receipt;
                }, cancellation.Token).AsTask();
            try
            {
                await WaitForCallbackAsync(entered.Task, cancelled).ConfigureAwait(false);
                Assert.That(observedToken, Is.EqualTo(cancellation.Token));
                AssertPending(
                    await journal.ReadAsync().AsTask().WaitAsync(s_barrierTimeout).ConfigureAwait(false),
                    DevicePullJournalStage.Receiving, m_binding);
                cancellation.Cancel();
            }
            finally
            {
                release.TrySetResult(true);
                await ObserveCompletionAsync(cancelled).ConfigureAwait(false);
            }
            OperationCanceledException? error = await Assert.CatchAsync<OperationCanceledException>(async () =>
                await cancelled.ConfigureAwait(false));
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            AssertPending(
                await journal.ReadAsync().ConfigureAwait(false),
                DevicePullJournalStage.Receiving, m_binding);

            PullDcaCertificateReceipt receipt = await journal.GetOrCreateAsync(
                m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                _ =>
                {
                    Interlocked.Increment(ref callbacks);
                    return new ValueTask<PullDcaCertificateReceipt>(m_receipt);
                }).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref callbacks), Is.EqualTo(2));
            AssertReceipt(receipt, m_receipt);
            AssertPublished(await journal.ReadAsync().ConfigureAwait(false), m_binding, m_receipt);
        }

        /// <summary>
        /// A reset completed after reservation rejects the old attempt before its Start reaches the registrar.
        /// </summary>
        [Test]
        public async Task ResetFencesReservedDispatchAndAllowsOnlyANewAttempt()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var journal = new DevicePullJournal(
                files, kApplicationUri, kRegistrarUri, m_telemetry, null, requireDispatchReservation: true);
            await using var session = CreateSession();
            int starts = 0;
            session.Handler = (request, _) =>
            {
                Assert.That(request, Is.TypeOf<CallRequest>());
                Interlocked.Increment(ref starts);
                return Reply(Variant.From(m_binding.RequestId));
            };
            var connection = CreateConnection(session, journal);
            ServiceResultException? missing = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.StartWithEvidenceAsync(
                    m_binding.ApplicationId, m_binding.Profile, m_binding.SigningRequest,
                    m_binding.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));
            Assert.That(missing!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            using (IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false))
            {
                await journal.BeginResetAsync().ConfigureAwait(false);
                await journal.CompleteResetAsync().ConfigureAwait(false);
                ServiceResultException? stale = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await connection.StartWithEvidenceAsync(
                        m_binding.ApplicationId, m_binding.Profile, m_binding.SigningRequest,
                        m_binding.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));
                Assert.That(stale!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(starts, Is.Zero);
                Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
            }

            using (IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false))
            {
                NodeId request = await connection.StartWithEvidenceAsync(
                    m_binding.ApplicationId, m_binding.Profile, m_binding.SigningRequest,
                    m_binding.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false);
                Assert.That(request, Is.EqualTo(m_binding.RequestId));
                DevicePullJournalCheckpoint? started = await journal.ReadAsync().ConfigureAwait(false);
                Assert.That(started!.Stage, Is.EqualTo(DevicePullJournalStage.Started));
                Assert.That(started.Binding.AttemptId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(starts, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Reset waits after a dispatch claim until the request identifier is published and Start has ended.
        /// </summary>
        [Test]
        public async Task ResetWaitsForClaimedStartAndRequestIdPublication()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var journal = new DevicePullJournal(
                files, kApplicationUri, kRegistrarUri, m_telemetry, null, requireDispatchReservation: true);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            await journal.RecordDispatchAsync(m_binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            Task resetting = journal.BeginResetAsync().AsTask();
            try
            {
                Assert.That(resetting.IsCompleted, Is.False);
                Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                    Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
                await journal.RecordStartedAsync(m_binding).ConfigureAwait(false);
                Assert.That(resetting.IsCompleted, Is.False);
                Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                    Is.EqualTo(DevicePullJournalStage.Started));
            }
            finally
            {
                await journal.OnStartCompletedAsync(m_binding.AttemptId).ConfigureAwait(false);
                await ObserveCompletionAsync(resetting).ConfigureAwait(false);
            }
            await resetting.ConfigureAwait(false);
            await journal.CompleteResetAsync().ConfigureAwait(false);
            ArrayOf<DevicePullJournalCheckpoint> retired = await journal.ReadRetiredAsync().ConfigureAwait(false);
            Assert.That(retired.Count, Is.EqualTo(1));
            Assert.That(retired[0].Stage, Is.EqualTo(DevicePullJournalStage.Started));
            Assert.That(retired[0].Binding.RequestId, Is.EqualTo(m_binding.RequestId));
        }

        /// <summary>
        /// A failed, cancelled, or unpublished Start releases reset without replacing uncertain evidence.
        /// </summary>
        [TestCase("transport")]
        [TestCase("cancel")]
        [TestCase("publication")]
        public async Task ResetContinuesAfterUnsuccessfulLocalStart(string failure)
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            int validations = 0;
            var journal = new DevicePullJournal(
                files, kApplicationUri, kRegistrarUri, m_telemetry,
                (_, _, _) =>
                {
                    if (Interlocked.Increment(ref validations) == 2 && failure == "publication")
                    {
                        return ValueTask.FromException(new IOException("Request ID publication failed."));
                    }
                    return ValueTask.CompletedTask;
                },
                requireDispatchReservation: true);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            await using var session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            var entered = Barrier();
            var release = Barrier();
            session.Handler = async (_, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                if (failure == "transport")
                {
                    throw new IOException("Start transport failed.");
                }
                return await Reply(Variant.From(m_binding.RequestId)).ConfigureAwait(false);
            };
            var connection = CreateConnection(session, journal);
            Task<NodeId> starting = connection.StartWithEvidenceAsync(
                m_binding.ApplicationId, m_binding.Profile, m_binding.SigningRequest,
                m_binding.SigningKeyCertificate, cancellation.Token).AsTask();
            Task? resetting = null;
            try
            {
                await WaitForCallbackAsync(entered.Task, starting).ConfigureAwait(false);
                resetting = journal.BeginResetAsync().AsTask();
                Assert.That(resetting.IsCompleted, Is.False);
                if (failure == "cancel")
                {
                    cancellation.Cancel();
                    await Assert.ThatAsync(async () => await starting.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                    await Assert.ThatAsync(async () => await starting.ConfigureAwait(false),
                        Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                await resetting.WaitAsync(s_barrierTimeout).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                cancellation.Cancel();
                await ObserveCompletionAsync(starting).ConfigureAwait(false);
                if (resetting != null)
                {
                    await ObserveCompletionAsync(resetting).ConfigureAwait(false);
                }
            }
            await journal.CompleteResetAsync().ConfigureAwait(false);
            ArrayOf<DevicePullJournalCheckpoint> retired = await journal.ReadRetiredAsync().ConfigureAwait(false);
            Assert.That(retired.Count, Is.EqualTo(1));
            Assert.That(retired[0].Stage, Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
            Assert.That(retired[0].Binding.RequestId.IsNull, Is.True);
        }

        /// <summary>
        /// The DCA uses the selected identity while later managed applications use the installed DCA channel.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SelectedIdentityMustMatchDcaStartChannelButNotLaterManagedStart(bool newKeyPair)
        {
            await using var session = CreateSession();
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            NodeId provideMethod = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_ProvideIdentities,
                session.MessageContext.NamespaceUris);
            NodeId startMethod = ExpandedNodeId.ToNodeId(
                newKeyPair
                    ? Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest
                    : Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                session.MessageContext.NamespaceUris);
            ByteString selected = m_binding.ClientChannelCertificate;
            var ticket = new DeviceIdentityTicketType { ProductInstanceUri = "urn:device" };
            var manager = new ManagerDescription { PurposeUri = "urn:software", EndpointUrls = [] };
            int starts = 0;
            session.Handler = (request, _) =>
            {
                var call = (CallRequest)request;
                NodeId method = call.MethodsToCall[0].MethodId;
                if (method == provideMethod)
                {
                    return Reply(
                        Variant.From(selected), Variant.FromStructure(ticket),
                        Variant.From(m_binding.ApplicationId), Variant.FromStructure(manager));
                }
                Assert.That(method, Is.EqualTo(startMethod));
                Interlocked.Increment(ref starts);
                return Reply(Variant.From(m_binding.RequestId));
            };
            var connection = new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default);
            ValueTask<NodeId> StartAsync(NodeId applicationId)
            {
                return newKeyPair
                    ? connection.StartNewKeyPairAsync(applicationId, m_binding.Profile, CancellationToken.None)
                    : connection.StartAsync(
                        applicationId, m_binding.Profile, m_binding.SigningRequest, CancellationToken.None);
            }
            await connection.ProvideIdentitiesAsync(
                new PullDcaIdentityOffer([selected], [], ["ticket"]), CancellationToken.None).ConfigureAwait(false);
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate)
                .Returns(m_binding.RegistrarChannelCertificate.ToArray());

            ServiceResultException? wrongChannel = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await StartAsync(m_binding.ApplicationId).ConfigureAwait(false));
            Assert.That(wrongChannel!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(starts, Is.Zero);

            session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(selected.ToArray());
            await StartAsync(m_binding.ApplicationId).ConfigureAwait(false);
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate)
                .Returns(m_binding.RegistrarChannelCertificate.ToArray());
            await StartAsync(new NodeId("managed", 1)).ConfigureAwait(false);

            Assert.That(starts, Is.EqualTo(2));
        }

        /// <summary>
        /// A caller cannot change the offered identity while the registrar is selecting it.
        /// </summary>
        [Test]
        public async Task IdentitySelectionUsesTheOfferCapturedBeforeDispatch()
        {
            await using var session = CreateSession();
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var entered = Barrier();
            var release = Barrier();
            ByteString[] identities = [m_binding.ClientChannelCertificate];
            var ticket = new DeviceIdentityTicketType { ProductInstanceUri = "urn:device" };
            session.Handler = async (_, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_barrierTimeout, CancellationToken.None).ConfigureAwait(false);
                return new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                Variant.From(m_binding.RegistrarChannelCertificate),
                                Variant.FromStructure(ticket),
                                Variant.From(m_binding.ApplicationId),
                                Variant.FromStructure(new ManagerDescription())
                            ]
                        }
                    ]
                };
            };
            var connection = new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default);
            Task<PullDcaIdentitySelection> selection = connection.ProvideIdentitiesAsync(
                new PullDcaIdentityOffer(identities, [], ["ticket"]), CancellationToken.None).AsTask();
            try
            {
                await WaitForCallbackAsync(entered.Task, selection).ConfigureAwait(false);
                identities[0] = m_binding.RegistrarChannelCertificate;
            }
            finally
            {
                release.TrySetResult(true);
                await ObserveCompletionAsync(selection).ConfigureAwait(false);
            }
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await selection.ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// Both fresh and resumed requests retrieve managers through the installed confidential channel.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InstalledManagerReadUsesTheRetainedRegistrarAndIssuedClientCertificate(bool resumed)
        {
            await using var session = CreateSession();
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            NodeId startMethod = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                session.MessageContext.NamespaceUris);
            NodeId managersMethod = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_GetManagers, session.MessageContext.NamespaceUris);
            int managerCalls = 0;
            session.Handler = (request, _) =>
            {
                if (request is not CallRequest call || call.MethodsToCall.Count != 1)
                {
                    throw new AssertionException("Unexpected registrar transport request.");
                }
                CallMethodRequest method = call.MethodsToCall[0];
                if (method.MethodId == startMethod)
                {
                    return Reply(Variant.From(m_binding.RequestId));
                }
                Assert.That(method.MethodId, Is.EqualTo(managersMethod));
                Assert.That(method.ObjectId, Is.EqualTo(m_binding.Registrar.RegistrarId));
                managerCalls++;
                return Reply(Variant.FromStructure(ArrayOf<ManagerDescription>.Empty));
            };
            var connection = new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default);
            if (!resumed)
            {
                await connection.StartWithEvidenceAsync(
                    m_binding.ApplicationId, m_binding.Profile, m_binding.SigningRequest,
                    m_binding.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false);
            }
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate)
                .Returns(m_receipt.Certificate.ToArray());

            ArrayOf<ManagerDescription> managers = await connection.GetManagersOnInstalledChannelAsync(
                resumed ? m_binding : null, m_receipt.Certificate, CancellationToken.None).ConfigureAwait(false);

            Assert.That(managers.IsEmpty, Is.True);
            Assert.That(managerCalls, Is.EqualTo(1));
            Assert.That(session.Requests.FindAll(static request => request is CallRequest),
                Has.Count.EqualTo(resumed ? 1 : 2));
        }

        /// <summary>
        /// Changed client, registrar, endpoint, or application URI prevents the manager request.
        /// </summary>
        [TestCase("client")]
        [TestCase("registrar")]
        [TestCase("endpoint")]
        [TestCase("uri")]
        [TestCase("transport-endpoint")]
        [TestCase("transport-uri")]
        public async Task InstalledManagerReadRejectsChangedChannelBeforeDispatch(string mismatch)
        {
            await using var session = CreateSession();
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(
                mismatch == "client" ? m_binding.ClientChannelCertificate.ToArray() : m_receipt.Certificate.ToArray());
            if (mismatch == "registrar")
            {
                session.Channel.SetupGet(channel => channel.ServerChannelCertificate)
                    .Returns(m_binding.ClientChannelCertificate.ToArray());
            }
            else if (mismatch == "endpoint")
            {
                session.Channel.Object.EndpointDescription.EndpointUrl = "opc.tcp://localhost:4841";
            }
            else if (mismatch == "uri")
            {
                session.Channel.Object.EndpointDescription.Server.ApplicationUri = kApplicationUri;
            }
            else if (mismatch is "transport-endpoint" or "transport-uri")
            {
                session.Channel.SetupGet(channel => channel.EndpointDescription).Returns(new EndpointDescription
                {
                    EndpointUrl = mismatch == "transport-endpoint"
                        ? "opc.tcp://localhost:4841"
                        : m_binding.Registrar.EndpointUrl.AbsoluteUri,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    Server = new ApplicationDescription
                    {
                        ApplicationUri = mismatch == "transport-uri" ? kApplicationUri : kRegistrarUri
                    }
                });
            }
            var connection = new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.GetManagersOnInstalledChannelAsync(
                    m_binding, m_receipt.Certificate, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(session.Requests.FindAll(static request => request is CallRequest), Is.Empty);
        }

        /// <summary>
        /// A recreated session invalidates the captured manager channel before dispatch.
        /// </summary>
        [Test]
        public async Task InstalledManagerReadRejectsSessionReplacementBeforeDispatch()
        {
            await using var session = CreateSession();
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            bool recreated = false;
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(() =>
            {
                if (!recreated)
                {
                    recreated = true;
                    session.Recreate();
                }
                return m_receipt.Certificate.ToArray();
            });
            var connection = new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default);

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.GetManagersOnInstalledChannelAsync(
                    m_binding, m_receipt.Certificate, CancellationToken.None).ConfigureAwait(false));

            Assert.That(recreated, Is.True);
            Assert.That(session.Requests.FindAll(static request => request is CallRequest), Is.Empty);
        }

        /// <summary>
        /// A manager request cannot infer a missing original signing channel from the new session.
        /// </summary>
        [Test]
        public async Task InstalledManagerReadRequiresRetainedBinding()
        {
            await using var session = CreateSession();
            var connection = new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.GetManagersOnInstalledChannelAsync(
                    null, m_receipt.Certificate, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(session.Requests.FindAll(static request => request is CallRequest), Is.Empty);
        }

        /// <summary>
        /// Creates a scripted confidential session with the retained channel identities.
        /// </summary>
        private SessionRequestContextTestContext CreateSession()
        {
            var session = new SessionRequestContextTestContext(
                telemetry: m_telemetry, securityMode: MessageSecurityMode.SignAndEncrypt);
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate)
                .Returns(m_binding.ClientChannelCertificate.ToArray());
            session.Channel.SetupGet(channel => channel.ServerChannelCertificate)
                .Returns(m_binding.RegistrarChannelCertificate.ToArray());
            session.Channel.Object.EndpointDescription.Server.ApplicationUri = kRegistrarUri;
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            return session;
        }

        /// <summary>
        /// Creates a journal over the supplied real checkpoint writer.
        /// </summary>
        private DevicePullJournal CreateJournal(PushRegistrarCheckpointFiles files)
        {
            return new DevicePullJournal(files, kApplicationUri, kRegistrarUri, m_telemetry);
        }

        /// <summary>
        /// Creates a connection using the scripted session and durable journal.
        /// </summary>
        private PullDcaConnection CreateConnection(
            SessionRequestContextTestContext session,
            DevicePullJournal journal)
        {
            return new PullDcaConnection(
                session.InnerSession, m_binding.Registrar, m_telemetry, 1024, _ => default, journal);
        }

        /// <summary>
        /// Publishes a dispatch claim and its returned request identifier.
        /// </summary>
        private static async Task PrepareStartedAsync(DevicePullJournal journal, PullDcaSigningBinding binding)
        {
            await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            await journal.RecordStartedAsync(binding).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a successful scripted certificate-directory method response.
        /// </summary>
        private static ValueTask<IServiceResponse> Reply(params Variant[] outputs)
        {
            return new ValueTask<IServiceResponse>(new CallResponse
            {
                Results = [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs }]
            });
        }

        /// <summary>
        /// Creates a barrier whose continuations do not run inline with its signal.
        /// </summary>
        private static TaskCompletionSource<bool> Barrier()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Waits for callback entry or reports an early operation failure, with a bounded wait.
        /// </summary>
        private static async Task WaitForCallbackAsync(Task entered, Task operation)
        {
            Task completed = await Task.WhenAny(entered, operation)
                .WaitAsync(s_barrierTimeout).ConfigureAwait(false);
            if (completed == operation)
            {
                await operation.ConfigureAwait(false);
                throw new AssertionException("The receive operation completed before entering its callback.");
            }
            await entered.ConfigureAwait(false);
        }

        /// <summary>
        /// Observes an expected callback failure during cleanup without waiting indefinitely.
        /// </summary>
        private static async Task ObserveCompletionAsync(Task operation)
        {
            try
            {
                await operation.WaitAsync(s_barrierTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception)
            {
                // Cleanup observes the task; the test subsequently asserts its actual outcome.
            }
        }

        /// <summary>
        /// Verifies a retained attempt has the expected stage and no received material.
        /// </summary>
        private static void AssertPending(
            DevicePullJournalCheckpoint? checkpoint,
            DevicePullJournalStage stage,
            PullDcaSigningBinding binding)
        {
            AssertBinding(checkpoint, stage, binding);
            Assert.That(checkpoint!.Receipt, Is.Null);
            Assert.That(checkpoint.Credentials, Is.Null);
        }

        /// <summary>
        /// Verifies the journal published the expected certificate and issuer chain.
        /// </summary>
        private static void AssertPublished(
            DevicePullJournalCheckpoint? checkpoint,
            PullDcaSigningBinding binding,
            PullDcaCertificateReceipt receipt)
        {
            AssertBinding(checkpoint, DevicePullJournalStage.CertificateReceived, binding);
            Assert.That(checkpoint!.Receipt, Is.Not.Null);
            AssertReceipt(checkpoint.Receipt!, receipt);
            Assert.That(checkpoint.Credentials, Is.Null);
        }

        /// <summary>
        /// Verifies the request, attempt, key, and channel binding in a checkpoint.
        /// </summary>
        private static void AssertBinding(
            DevicePullJournalCheckpoint? checkpoint,
            DevicePullJournalStage stage,
            PullDcaSigningBinding binding)
        {
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(checkpoint!.Stage, Is.EqualTo(stage));
            Assert.That(checkpoint.Binding.AttemptId, Is.EqualTo(binding.AttemptId));
            Assert.That(checkpoint.Binding.Registrar, Is.EqualTo(binding.Registrar));
            Assert.That(checkpoint.Binding.ApplicationId, Is.EqualTo(binding.ApplicationId));
            Assert.That(checkpoint.Binding.RequestId, Is.EqualTo(binding.RequestId));
            Assert.That(checkpoint.Binding.Profile, Is.EqualTo(binding.Profile));
            Assert.That(checkpoint.Binding.SigningRequest.ToArray(), Is.EqualTo(binding.SigningRequest.ToArray()));
            Assert.That(checkpoint.Binding.SigningKeyCertificate.ToArray(),
                Is.EqualTo(binding.SigningKeyCertificate.ToArray()));
            Assert.That(checkpoint.Binding.ClientChannelCertificate.ToArray(),
                Is.EqualTo(binding.ClientChannelCertificate.ToArray()));
            Assert.That(checkpoint.Binding.RegistrarChannelCertificate.ToArray(),
                Is.EqualTo(binding.RegistrarChannelCertificate.ToArray()));
        }

        /// <summary>
        /// Verifies the received certificate and ordered issuer chain byte-for-byte.
        /// </summary>
        private static void AssertReceipt(PullDcaCertificateReceipt actual, PullDcaCertificateReceipt expected)
        {
            Assert.That(actual.Certificate.ToArray(), Is.EqualTo(expected.Certificate.ToArray()));
            Assert.That(actual.IssuerCertificates.Count, Is.EqualTo(2));
            Assert.That(actual.IssuerCertificates[0].ToArray(),
                Is.EqualTo(expected.IssuerCertificates[0].ToArray()));
            Assert.That(actual.IssuerCertificates[1].ToArray(),
                Is.EqualTo(expected.IssuerCertificates[1].ToArray()));
        }

        /// <summary>
        /// Creates a valid signing binding and matching issued certificate chain.
        /// </summary>
        private static (PullDcaSigningBinding Binding, PullDcaCertificateReceipt Receipt) CreateMaterial()
        {
            using Certificate identity = CreateIdentity("CN=Pull Retry Device", kApplicationUri);
            using Certificate registrar = CreateIdentity("CN=Pull Retry Registrar", kRegistrarUri);
            using Certificate root = CertificateBuilder.Create("CN=Pull Retry Root")
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate issuer = CertificateBuilder.Create("CN=Pull Retry Issuer")
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .SetCAConstraint()
                .SetIssuer(root)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            ByteString csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(identity));
            var request = new Pkcs10CertificationRequest(csr.ToArray());
            using Certificate issued = CertificateBuilder.Create(request.Subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .AddExtension(new X509SubjectAltNameExtension(kApplicationUri, ["localhost"]))
                .SetIssuer(issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            var binding = new PullDcaSigningBinding(
                new PullDcaRegistrar(
                    new Uri("opc.tcp://localhost:4840"),
                    new NodeId("registrar", 1),
                    new NodeId("directory", 1)),
                new NodeId("device", 1),
                new NodeId("request", 1),
                new PullDcaCertificateProfile(
                    new NodeId("group", 1), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                csr,
                Encoded(identity),
                Encoded(identity),
                Encoded(registrar),
                kRegistrarUri,
                [Opc.Ua.Namespaces.OpcUa, "urn:request-context:original"],
                Guid.NewGuid());
            var receipt = new PullDcaCertificateReceipt(Encoded(issued), [Encoded(issuer), Encoded(root)]);
            return (binding, receipt);
        }

        /// <summary>
        /// Creates an RSA identity certificate with the specified application URI.
        /// </summary>
        private static Certificate CreateIdentity(string subject, string applicationUri)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Copies the public certificate encoding into an OPC UA byte string.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>The device application URI.</summary>
        private const string kApplicationUri = "urn:opcfoundation:tests:pull-retry-device";

        /// <summary>The registrar application URI.</summary>
        private const string kRegistrarUri = "urn:opcfoundation:tests:pull-retry-registrar";

        /// <summary>The maximum wait for a test barrier.</summary>
        private static readonly TimeSpan s_barrierTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The start of the test certificates' validity period.</summary>
        private static readonly DateTime s_notBefore = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The end of the test certificates' validity period.</summary>
        private static readonly DateTime s_notAfter = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The isolated checkpoint directory.</summary>
        private string m_directory = string.Empty;

        /// <summary>The fixture telemetry context.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>The request and channel binding.</summary>
        private PullDcaSigningBinding m_binding = null!;

        /// <summary>The certificate issued for the fixture's request.</summary>
        private PullDcaCertificateReceipt m_receipt = null!;
    }
}
#endif
