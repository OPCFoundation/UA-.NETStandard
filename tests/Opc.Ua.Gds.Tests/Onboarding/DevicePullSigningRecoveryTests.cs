// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises request-bound lost-Finish recovery with real checkpoints and a dedicated private-key store.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullSigningRecoveryTests
    {
        /// <summary>
        /// Creates isolated identities, checkpoint storage, and a certificate manager.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "pull-recovery-" + Guid.NewGuid().ToString("N"));
            m_telemetry = NUnitTelemetryContext.Create();
            m_identity = CreateIdentity("CN=Device", kApplicationUri);
            m_registrar = CreateIdentity("CN=Registrar", kRegistrarUri);
            m_issuer = CertificateBuilder.Create("CN=Recovery issuer")
                .SetNotBefore(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetCAConstraint().SetRSAKeySize(2048).CreateForRSA();
            m_issuers = new CertificateCollection();
            m_manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_manager.Setup(value => value.AcquireApplicationCertificateByType(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType))
                .Returns(() => new CertificateEntry(
                    m_identity, m_issuers, CoreObjectTypeIds.RsaSha256ApplicationCertificateType));
            m_manager.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            m_resolver = new CertificateManager(m_telemetry);
        }

        /// <summary>
        /// Releases fixture-owned handles and removes this test's isolated files.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_resolver.Dispose();
            m_issuers.Dispose();
            m_issuer.Dispose();
            m_registrar.Dispose();
            m_identity.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// A prior assignment using the same key remains rejected after recreation, while a new certificate
        /// for that retained key can be received after the authority changes the CSR subject.
        /// </summary>
        [Test]
        public async Task SameKeyPriorAssignmentIsRejectedAndFreshAssignmentSurvivesRestart()
        {
            PullDcaSigningBinding binding;
            PullDcaCredentials prior;
            PullDcaCredentials fresh;
            using (var files = OpenFiles())
            {
                DevicePullJournal journal = CreateJournal(files);
                using var keys = CreateKeys(files, journal);
                using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
                binding = await PrepareBindingAsync(keys, reorderedNamespaces: true).ConfigureAwait(false);
                prior = Issue(binding.SigningRequest, kApplicationUri, "CN=Prior");
                fresh = Issue(binding.SigningRequest, kApplicationUri, "CN=Renamed by authority");
                using Certificate first = Certificate.FromRawData(prior.Certificate);
                using Certificate second = Certificate.FromRawData(fresh.Certificate);
                using RSA firstKey = first.GetRSAPublicKey()!;
                using RSA secondKey = second.GetRSAPublicKey()!;
                Assert.That(firstKey.ExportParameters(false).Modulus,
                    Is.EqualTo(secondKey.ExportParameters(false).Modulus));
                Assert.That(firstKey.ExportParameters(false).Exponent,
                    Is.EqualTo(secondKey.ExportParameters(false).Exponent));
                Assert.That(prior.Certificate, Is.Not.EqualTo(fresh.Certificate));

                byte[] callerBytes = prior.Certificate.ToArray();
                await keys.RecordAssignmentsAsync(
                    binding with { RequestId = NodeId.Null },
                    [s_profile.CertificateTypeId], [ByteString.From(callerBytes)]).ConfigureAwait(false);
                Array.Fill(callerBytes, (byte)0);
                await StartJournalAsync(journal, binding).ConfigureAwait(false);
                await using var session = CreateSession();
                PullDcaSigningBinding resolved = await ResolveAsync(journal, binding, session).ConfigureAwait(false);
                var recovery = new DevicePullSigningRecovery(
                    journal, keys, _ => Task.FromResult(session.ManagedSession));
                Assert.That(binding.ApplicationId.NamespaceIndex, Is.EqualTo(2));
                Assert.That(resolved.ApplicationId.NamespaceIndex, Is.EqualTo(1));

                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await ReceiveAsync(journal, resolved, recovery, prior).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                    Is.EqualTo(DevicePullJournalStage.Receiving));
                m_manager.Verify(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
            }

            using var reopened = OpenFiles();
            DevicePullJournal restarted = CreateJournal(reopened);
            using var reopenedKeys = CreateKeys(reopened, restarted);
            await reopenedKeys.ReconcileStagedAsync().ConfigureAwait(false);
            await using var newSession = CreateSession();
            PullDcaSigningBinding resumed = await ResolveAsync(restarted, binding, newSession).ConfigureAwait(false);
            await reopenedKeys.RestoreSigningRequestAsync(resumed).ConfigureAwait(false);
            var restartedRecovery = new DevicePullSigningRecovery(
                restarted, reopenedKeys, _ => Task.FromResult(newSession.ManagedSession));
            PullDcaCertificateReceipt receipt = await ReceiveAsync(
                restarted, resumed, restartedRecovery, fresh).ConfigureAwait(false);

            Assert.That(receipt.Certificate, Is.EqualTo(fresh.Certificate));
            Assert.That(receipt.IssuerCertificates.Span.SequenceEqual(fresh.IssuerCertificates.Span), Is.True);
            Assert.That((await restarted.ReadAsync().ConfigureAwait(false))!.Stage,
                Is.EqualTo(DevicePullJournalStage.CertificateReceived));
            m_manager.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.Is<ValidationOptions>(options =>
                    options.TrustScope != null &&
                    options.TrustScope.TrustedCertificates.Count == 1 &&
                    options.TrustScope.TrustedCertificates[0] == fresh.TrustList.TrustedCertificates[0] &&
                    options.RejectUnknownRevocationStatus == true &&
                    options.RejectSHA1SignedCertificates == true &&
                    options.AutoAcceptUntrustedCertificates == false &&
                    options.MinimumCertificateKeySize == 2048),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Only an unassigned group or unsupported optional method permits the documented baseline fallbacks.
        /// Other failures stop before StartSigningRequest.
        /// </summary>
        [TestCase("group")]
        [TestCase("fallback")]
        [TestCase("fallback-absent")]
        [TestCase("fallback-not-found")]
        [TestCase("fallback-unsupported")]
        [TestCase("fallback-security")]
        [TestCase("unsupported")]
        [TestCase("not-found")]
        [TestCase("unknown-node")]
        [TestCase("access")]
        [TestCase("security")]
        [TestCase("malformed")]
        public async Task BaselineQueriesPreserveErrorsAndFallbackToAllGroups(string scenario)
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding prepared = await PrepareBindingAsync(keys).ConfigureAwait(false);
            PullDcaCredentials prior = Issue(prepared.SigningRequest, kApplicationUri, "CN=Prior");
            await using var session = CreateSession();
            NodeId getCertificates = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates, session.MessageContext.NamespaceUris);
            NodeId start = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                session.MessageContext.NamespaceUris);
            int baselineCalls = 0;
            int starts = 0;
            session.Handler = (request, _) =>
            {
                if (request is TranslateBrowsePathsToNodeIdsRequest translate)
                {
                    var results = new BrowsePathResult[translate.BrowsePaths.Count];
                    for (int i = 0; i < results.Length; i++)
                    {
                        ArrayOf<RelativePathElement> elements = translate.BrowsePaths[i].RelativePath.Elements;
                        NodeId methodId = elements[0].TargetName.Name switch
                        {
                            "GetCertificates" => ExpandedNodeId.ToNodeId(
                                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates,
                                session.MessageContext.NamespaceUris),
                            "StartSigningRequest" => ExpandedNodeId.ToNodeId(
                                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                                session.MessageContext.NamespaceUris),
                            _ => NodeId.Null
                        };
                        results[i] = methodId.IsNull
                            ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] }
                            : new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = new ExpandedNodeId(methodId),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            };
                    }
                    return new ValueTask<IServiceResponse>(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results
                    });
                }
                var method = ((CallRequest)request).MethodsToCall[0];
                if (method.MethodId == start)
                {
                    starts++;
                    return Reply(StatusCodes.Good, Variant.From(prepared.RequestId));
                }
                Assert.That(method.MethodId, Is.EqualTo(getCertificates));
                baselineCalls++;
                NodeId group = method.InputArguments[1].TryGetValue(out NodeId value) ? value : NodeId.Null;
                Assert.That(group, Is.EqualTo(baselineCalls == 1 ? s_profile.CertificateGroupId : NodeId.Null));
                if (scenario.StartsWith("fallback", StringComparison.Ordinal) && baselineCalls == 1)
                {
                    return Reply(StatusCodes.BadInvalidArgument);
                }
                if (scenario == "fallback-absent")
                {
                    return Reply(StatusCodes.BadInvalidArgument);
                }
                if (scenario == "fallback-not-found")
                {
                    return Reply(StatusCodes.BadNotFound);
                }
                if (scenario == "fallback-unsupported")
                {
                    return Reply(StatusCodes.BadNotSupported);
                }
                if (scenario == "unsupported")
                {
                    return Reply(StatusCodes.BadMethodInvalid);
                }
                if (scenario == "not-found")
                {
                    return Reply(StatusCodes.BadNotFound);
                }
                if (scenario == "unknown-node")
                {
                    return Reply(StatusCodes.BadNodeIdUnknown);
                }
                if (scenario == "access")
                {
                    return Reply(StatusCodes.BadUserAccessDenied);
                }
                if (scenario is "security" or "fallback-security")
                {
                    return Reply(StatusCodes.BadSecurityChecksFailed);
                }
                ArrayOf<NodeId> types = [s_profile.CertificateTypeId];
                ArrayOf<ByteString> certificates = scenario == "malformed" ? [] : [prior.Certificate];
                return Reply(StatusCodes.Good, Variant.From(types), Variant.From(certificates));
            };
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            var connection = new PullDcaConnection(
                session.InnerSession, prepared.Registrar, m_telemetry, 1024, _ => default, journal, recovery);
            if (scenario is "group" or "fallback" or "fallback-unsupported" or "unsupported")
            {
                NodeId requestId = await connection.StartWithEvidenceAsync(
                    prepared.ApplicationId, prepared.Profile, prepared.SigningRequest,
                    prepared.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false);
                Assert.That(requestId, Is.EqualTo(prepared.RequestId));
                DevicePullJournalCheckpoint checkpoint = (await journal.ReadAsync().ConfigureAwait(false))!;
                PullDcaSigningBinding resolved = await ResolveAsync(journal, checkpoint.Binding, session)
                    .ConfigureAwait(false);
                PullDcaCredentials candidate = scenario is "group" or "fallback"
                    ? prior
                    : Issue(prepared.SigningRequest, kApplicationUri, "CN=New");
                ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await ReceiveAsync(journal, resolved, recovery, candidate).ConfigureAwait(false));
                Assert.That(rejected!.StatusCode, Is.EqualTo(scenario is "group" or "fallback"
                    ? StatusCodes.BadSecurityChecksFailed
                    : StatusCodes.BadInvalidState));
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(baselineCalls, Is.EqualTo(
                    scenario.StartsWith("fallback", StringComparison.Ordinal) ? 2 : 1));
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await connection.StartWithEvidenceAsync(
                        prepared.ApplicationId, prepared.Profile, prepared.SigningRequest,
                        prepared.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));
                StatusCode expected = scenario switch
                {
                    "fallback-absent" => StatusCodes.BadInvalidArgument,
                    "not-found" or "fallback-not-found" => StatusCodes.BadNotFound,
                    "unknown-node" => StatusCodes.BadNodeIdUnknown,
                    "access" => StatusCodes.BadUserAccessDenied,
                    "security" or "fallback-security" => StatusCodes.BadSecurityChecksFailed,
                    _ => StatusCodes.BadUnexpectedError
                };
                Assert.That(error!.StatusCode, Is.EqualTo(expected));
                Assert.That(starts, Is.Zero);
                Assert.That(baselineCalls, Is.EqualTo(
                    scenario.StartsWith("fallback", StringComparison.Ordinal) ? 2 : 1));
                Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
            }
        }

        /// <summary>
        /// A malformed assigned certificate prevents the signing request from being sent.
        /// </summary>
        [Test]
        public async Task InvalidBaselineCertificatePreventsSigningDispatch()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding prepared = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await using var session = CreateSession();
            NodeId getCertificates = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates, session.MessageContext.NamespaceUris);
            NodeId start = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                session.MessageContext.NamespaceUris);
            int baselineCalls = 0;
            int starts = 0;
            session.Handler = (request, _) =>
            {
                var method = ((CallRequest)request).MethodsToCall[0];
                if (method.MethodId == start)
                {
                    starts++;
                    return Reply(StatusCodes.Good, Variant.From(prepared.RequestId));
                }
                Assert.That(method.MethodId, Is.EqualTo(getCertificates));
                baselineCalls++;
                ArrayOf<NodeId> types = [s_profile.CertificateTypeId];
                ArrayOf<ByteString> certificates = [ByteString.From(new byte[] { 0x30, 0x00 }.AsSpan())];
                return Reply(StatusCodes.Good, Variant.From(types), Variant.From(certificates));
            };
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            var connection = new PullDcaConnection(
                session.InnerSession, prepared.Registrar, m_telemetry, 1024, _ => default, journal, recovery);

            await Assert.ThrowsAsync<CryptographicException>(async () =>
                await connection.StartWithEvidenceAsync(
                    prepared.ApplicationId, prepared.Profile, prepared.SigningRequest,
                    prepared.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));

            Assert.That(baselineCalls, Is.EqualTo(1));
            Assert.That(starts, Is.Zero);
            Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// An assignment-checkpoint publication failure stops the actual transport before Start.
        /// </summary>
        [Test]
        public async Task BaselinePublicationFailurePreventsStart()
        {
            string? failDestination = null;
            Mock<IFileSystem> fileSystem = CreateFileSystem(() => failDestination);
            using var files = new PushRegistrarCheckpointFiles(
                Path.Combine(m_directory, "checkpoints"), fileSystem.Object);
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding prepared = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await using var session = CreateSession();
            NodeId getCertificates = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates, session.MessageContext.NamespaceUris);
            int starts = 0;
            session.Handler = (request, _) =>
            {
                NodeId method = ((CallRequest)request).MethodsToCall[0].MethodId;
                if (method == getCertificates)
                {
                    return Reply(StatusCodes.Good,
                        Variant.From(ArrayOf<NodeId>.Empty), Variant.From(ArrayOf<ByteString>.Empty));
                }
                starts++;
                return Reply(StatusCodes.Good, Variant.From(prepared.RequestId));
            };
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            var observer = new Mock<IPullDcaSigningAssignmentsObserver>(MockBehavior.Strict);
            observer.Setup(value => value.RecordAssignmentsAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((PullDcaSigningBinding binding, ArrayOf<NodeId> types, ArrayOf<ByteString> certificates,
                    CancellationToken ct) =>
                {
                    failDestination = CheckpointPath(AssignmentKey(binding.AttemptId));
                    return recovery.RecordAssignmentsAsync(binding, types, certificates, ct);
                });
            var connection = new PullDcaConnection(
                session.InnerSession, prepared.Registrar, m_telemetry, 1024, _ => default, journal, observer.Object);

            await Assert.ThrowsAsync<IOException>(async () =>
                await connection.StartWithEvidenceAsync(
                    prepared.ApplicationId, prepared.Profile, prepared.SigningRequest,
                    prepared.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));

            Assert.That(starts, Is.Zero);
            Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Once Start is sent, an unknown result retains its key, baseline, and dispatch fence.
        /// </summary>
        [Test]
        public async Task UnknownStartRetainsBaselineAndDoesNotDispatchAgain()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding prepared = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await using var session = CreateSession();
            NodeId getCertificates = ExpandedNodeId.ToNodeId(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates, session.MessageContext.NamespaceUris);
            int starts = 0;
            session.Handler = (request, _) =>
            {
                if (((CallRequest)request).MethodsToCall[0].MethodId == getCertificates)
                {
                    return Reply(StatusCodes.Good,
                        Variant.From(ArrayOf<NodeId>.Empty), Variant.From(ArrayOf<ByteString>.Empty));
                }
                starts++;
                throw new IOException("The Start response was lost.");
            };
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            var connection = new PullDcaConnection(
                session.InnerSession, prepared.Registrar, m_telemetry, 1024, _ => default, journal, recovery);

            await Assert.ThrowsAsync<IOException>(async () =>
                await connection.StartWithEvidenceAsync(
                    prepared.ApplicationId, prepared.Profile, prepared.SigningRequest,
                    prepared.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));
            DevicePullJournalCheckpoint retained = (await journal.ReadAsync().ConfigureAwait(false))!;
            Assert.That(retained.Stage, Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
            Assert.That(retained.Binding.RequestId.IsNull, Is.True);
            Assert.That(await files.ReadAsync(AssignmentKey(retained.Binding.AttemptId), CancellationToken.None)
                .ConfigureAwait(false), Is.Not.Null);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.StartWithEvidenceAsync(
                    prepared.ApplicationId, prepared.Profile, prepared.SigningRequest,
                    prepared.SigningKeyCertificate, CancellationToken.None).ConfigureAwait(false));
            Assert.That(starts, Is.EqualTo(1));
            Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
        }

        /// <summary>
        /// The workflow handles a consumed Finish response with the device's retained key and receipt journal.
        /// </summary>
        [Test]
        public async Task ConsumedFinishUsesDeviceRecoveryBeforePublishingAndInstalling()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await keys.RecordAssignmentsAsync(binding with { RequestId = NodeId.Null }, [], []).ConfigureAwait(false);
            await StartJournalAsync(journal, binding).ConfigureAwait(false);
            await using var session = CreateSession();
            PullDcaSigningBinding resolved = await ResolveAsync(journal, binding, session).ConfigureAwait(false);
            PullDcaCredentials candidate = Issue(binding.SigningRequest, kApplicationUri, "CN=Renamed");
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            var connection = new FakePullDcaBoundSigningConnection(
                (applicationId, requestId, _) =>
                {
                    Assert.That(applicationId, Is.EqualTo(resolved.ApplicationId));
                    Assert.That(requestId, Is.EqualTo(resolved.RequestId));
                    return new ValueTask<PullDcaSigningBinding>(resolved);
                })
            {
                Finish = (applicationId, requestId, _) =>
                {
                    Assert.That(applicationId, Is.EqualTo(resolved.ApplicationId));
                    Assert.That(requestId, Is.EqualTo(resolved.RequestId));
                    return ValueTask.FromException<(ByteString, ArrayOf<ByteString>)>(
                        new ServiceResultException(StatusCodes.BadInvalidArgument));
                }
            };
            ArrayOf<NodeId> types = [resolved.Profile.CertificateTypeId];
            ArrayOf<ByteString> certificates = [candidate.Certificate];
            connection.GetCertificates = (applicationId, certificateGroupId, _) =>
            {
                Assert.That(applicationId, Is.EqualTo(resolved.ApplicationId));
                Assert.That(certificateGroupId, Is.EqualTo(resolved.Profile.CertificateGroupId));
                return new ValueTask<(ArrayOf<NodeId>, ArrayOf<ByteString>)>((types, certificates));
            };
            connection.ReadTrustList = (applicationId, certificateGroupId, _) =>
            {
                Assert.That(applicationId, Is.EqualTo(resolved.ApplicationId));
                Assert.That(certificateGroupId, Is.EqualTo(resolved.Profile.CertificateGroupId));
                return new ValueTask<TrustListDataType>(candidate.TrustList);
            };
            var credentials = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            credentials.As<IPullDcaBoundCredentialProvider>()
                .Setup(value => value.InstallAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<PullDcaCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (PullDcaSigningBinding actual, PullDcaCredentials issued, CancellationToken ct) =>
                {
                    DevicePullJournalCheckpoint checkpoint = (await journal.ReadAsync(ct).ConfigureAwait(false))!;
                    Assert.That(checkpoint.Stage, Is.EqualTo(DevicePullJournalStage.Received));
                    Assert.That(checkpoint.Credentials!.Certificate, Is.EqualTo(candidate.Certificate));
                    Assert.That(checkpoint.Credentials.TrustList.TrustedCertificates[0],
                        Is.EqualTo(candidate.TrustList.TrustedCertificates[0]));
                    await journal.RecordInstalledAsync(
                        actual.AttemptId, Encoded(m_registrar), issued, ct).ConfigureAwait(false);
                });
            var workflow = new PullDcaWorkflow(
                new PullDcaOptions(),
                (_, _) => new ValueTask<IPullDcaConnection>(connection),
                Mock.Of<IPullDcaIdentityProvider>(),
                credentials.Object,
                receipts: journal,
                signingRecovery: recovery,
                signingJournal: journal);

            PullDcaCredentials installed = await workflow.CompleteCertificateRequestAsync(resolved)
                .ConfigureAwait(false);

            Assert.That(installed.Certificate, Is.EqualTo(candidate.Certificate));
            Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                Is.EqualTo(DevicePullJournalStage.Installed));
            Assert.That(connection.FinishCalls, Is.EqualTo(1));
            Assert.That(connection.GetCertificatesCalls, Is.EqualTo(1));
            Assert.That(connection.ReadTrustListCalls, Is.EqualTo(2));
            credentials.As<IPullDcaBoundCredentialProvider>().Verify(value => value.InstallAsync(
                It.Is<PullDcaSigningBinding>(actual => actual.AttemptId == binding.AttemptId),
                It.Is<PullDcaCredentials>(actual => actual.Certificate == candidate.Certificate),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Each mismatch leaves the request in Receiving without publishing a certificate.
        /// </summary>
        [TestCase("registrar")]
        [TestCase("application")]
        [TestCase("request")]
        [TestCase("group")]
        [TestCase("type")]
        [TestCase("key")]
        [TestCase("uri")]
        [TestCase("expired")]
        [TestCase("trust")]
        [TestCase("channel")]
        [TestCase("missing-baseline")]
        public async Task RecoveryRejectsMismatchedBindingKeyCertificateTrustAndChannel(string scenario)
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            if (scenario != "missing-baseline")
            {
                await keys.RecordAssignmentsAsync(
                    binding with { RequestId = NodeId.Null }, [], []).ConfigureAwait(false);
            }
            await StartJournalAsync(journal, binding).ConfigureAwait(false);
            await using var session = CreateSession();
            PullDcaSigningBinding resolved = await ResolveAsync(journal, binding, session).ConfigureAwait(false);
            ByteString signingRequest = scenario == "key"
                ? ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(m_identity).AsSpan())
                : binding.SigningRequest;
            PullDcaCredentials candidate = Issue(
                signingRequest, scenario == "uri" ? "urn:other:application" : kApplicationUri,
                "CN=Issued", scenario == "expired");
            if (scenario == "trust")
            {
                m_manager.Setup(value => value.ValidateAsync(
                        It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                        It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CertificateValidationResult(
                        false, StatusCodes.BadCertificateUntrusted, [], false));
            }
            if (scenario == "channel")
            {
                session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(m_registrar.RawData);
            }
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            PullDcaRegistrar registrar = scenario == "registrar"
                ? resolved.Registrar with { RegistrarId = new NodeId("other-registrar", 1) }
                : resolved.Registrar;
            NodeId application = scenario == "application" ? new NodeId("other-device", 1) : resolved.ApplicationId;
            NodeId request = scenario == "request" ? new NodeId("other-request", 1) : resolved.RequestId;
            PullDcaCertificateProfile profile = scenario switch
            {
                "group" => resolved.Profile with { CertificateGroupId = new NodeId("other-group", 1) },
                "type" => resolved.Profile with { CertificateTypeId = new NodeId("other-type", 1) },
                _ => resolved.Profile
            };
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await journal.GetOrCreateAsync(
                    resolved.Registrar, resolved.ApplicationId, resolved.RequestId, resolved.Profile,
                    async ct =>
                    {
                        await recovery.ValidateAsync(registrar, application, request, profile, candidate, ct)
                            .ConfigureAwait(false);
                        return new PullDcaCertificateReceipt(candidate.Certificate, candidate.IssuerCertificates);
                    }).ConfigureAwait(false));

            StatusCode expected = scenario switch
            {
                "uri" => StatusCodes.BadCertificateUriInvalid,
                "expired" => StatusCodes.BadCertificateTimeInvalid,
                "trust" => StatusCodes.BadCertificateUntrusted,
                "missing-baseline" => StatusCodes.BadInvalidState,
                _ => StatusCodes.BadSecurityChecksFailed
            };
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            DevicePullJournalCheckpoint current = (await journal.ReadAsync().ConfigureAwait(false))!;
            Assert.That(current.Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
            Assert.That(current.Receipt, Is.Null);
        }

        /// <summary>
        /// An assignment baseline copied from another local attempt cannot authorize this request.
        /// </summary>
        [Test]
        public async Task BaselineFromDifferentAttemptCannotAuthorizeReplacement()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding first = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await keys.RecordAssignmentsAsync(first with { RequestId = NodeId.Null }, [], []).ConfigureAwait(false);
            byte[] baseline = (await files.ReadAsync(AssignmentKey(first.AttemptId), CancellationToken.None)
                .ConfigureAwait(false))!;
            PullDcaSigningBinding replacement = first with { AttemptId = Guid.NewGuid() };
            await files.WriteAsync(AssignmentKey(replacement.AttemptId), baseline, CancellationToken.None)
                .ConfigureAwait(false);
            await StartJournalAsync(journal, replacement).ConfigureAwait(false);
            await using var session = CreateSession();
            PullDcaSigningBinding resolved = await ResolveAsync(journal, replacement, session).ConfigureAwait(false);
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            PullDcaCredentials candidate = Issue(replacement.SigningRequest, kApplicationUri, "CN=Issued");

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReceiveAsync(journal, resolved, recovery, candidate).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                Is.EqualTo(DevicePullJournalStage.Receiving));
        }

        /// <summary>
        /// A truncated assignment checkpoint is rejected rather than interpreted as an empty baseline.
        /// </summary>
        [Test]
        public async Task CorruptBaselineDoesNotPublishReceipt()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await keys.RecordAssignmentsAsync(binding with { RequestId = NodeId.Null }, [], []).ConfigureAwait(false);
            await files.WriteAsync(AssignmentKey(binding.AttemptId), new byte[] { 1, 2 }, CancellationToken.None)
                .ConfigureAwait(false);
            await StartJournalAsync(journal, binding).ConfigureAwait(false);
            await using var session = CreateSession();
            PullDcaSigningBinding resolved = await ResolveAsync(journal, binding, session).ConfigureAwait(false);
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            PullDcaCredentials candidate = Issue(binding.SigningRequest, kApplicationUri, "CN=Issued");

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await ReceiveAsync(journal, resolved, recovery, candidate).ConfigureAwait(false));

            DevicePullJournalCheckpoint checkpoint = (await journal.ReadAsync().ConfigureAwait(false))!;
            Assert.That(checkpoint.Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
            Assert.That(checkpoint.Receipt, Is.Null);
        }

        /// <summary>
        /// A reset or changed channel during trust validation prevents receipt publication.
        /// </summary>
        [TestCase("reset")]
        [TestCase("channel")]
        public async Task ResetOrChannelChangeDuringRecoveryPreventsPublication(string transition)
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await keys.RecordAssignmentsAsync(binding with { RequestId = NodeId.Null }, [], []).ConfigureAwait(false);
            await StartJournalAsync(journal, binding).ConfigureAwait(false);
            await using var session = CreateSession();
            PullDcaSigningBinding resolved = await ResolveAsync(journal, binding, session).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_manager.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Returns(async (CertificateCollection _, TrustListIdentifier _, ValidationOptions _,
                    CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_timeout, ct).ConfigureAwait(false);
                    return CertificateValidationResult.Success;
                });
            var recovery = new DevicePullSigningRecovery(journal, keys, _ => Task.FromResult(session.ManagedSession));
            PullDcaCredentials candidate = Issue(binding.SigningRequest, kApplicationUri, "CN=Issued");
            Task<PullDcaCertificateReceipt> receiving = ReceiveAsync(journal, resolved, recovery, candidate).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                if (transition == "reset")
                {
                    await journal.BeginResetAsync().ConfigureAwait(false);
                    await journal.CompleteResetAsync().ConfigureAwait(false);
                }
                else
                {
                    session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(m_registrar.RawData);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (transition == "reset")
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await receiving.ConfigureAwait(false));
                Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
                ArrayOf<DevicePullJournalCheckpoint> retired = await journal.ReadRetiredAsync().ConfigureAwait(false);
                Assert.That(retired.Count, Is.EqualTo(1));
                Assert.That(retired[0].Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
                Assert.That(retired[0].Receipt, Is.Null);
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await receiving.ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                DevicePullJournalCheckpoint current = (await journal.ReadAsync().ConfigureAwait(false))!;
                Assert.That(current.Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
                Assert.That(current.Receipt, Is.Null);
            }
            Assert.That(await files.ReadAsync(AssignmentKey(binding.AttemptId), CancellationToken.None)
                .ConfigureAwait(false), Is.Not.Null);
        }

        /// <summary>
        /// Opens a locally leased checkpoint directory.
        /// </summary>
        private PushRegistrarCheckpointFiles OpenFiles()
        {
            return new PushRegistrarCheckpointFiles(Path.Combine(m_directory, "checkpoints"));
        }

        /// <summary>
        /// Creates a journal that requires an owned dispatch generation.
        /// </summary>
        private DevicePullJournal CreateJournal(PushRegistrarCheckpointFiles files)
        {
            return new DevicePullJournal(
                files, kApplicationUri, kRegistrarUri, m_telemetry, null, requireDispatchReservation: true);
        }

        /// <summary>
        /// Reopens the same dedicated private-key directory after recreation.
        /// </summary>
        private DevicePullSigningKeyStore CreateKeys(PushRegistrarCheckpointFiles files, DevicePullJournal journal)
        {
            return new DevicePullSigningKeyStore(
                files, journal, m_manager.Object, m_resolver, kApplicationUri,
                Path.Combine(m_directory, "signing-keys"), CertificateStoreType.Directory, m_telemetry);
        }

        /// <summary>
        /// Prepares one fresh key and the channel evidence used by the scripted session.
        /// </summary>
        private async ValueTask<PullDcaSigningBinding> PrepareBindingAsync(
            DevicePullSigningKeyStore keys,
            bool reorderedNamespaces = false)
        {
            ushort index = reorderedNamespaces ? (ushort)2 : (ushort)1;
            NodeId applicationId = new("device", index);
            var profile = new PullDcaCertificateProfile(
                new NodeId("group", index), CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            ByteString request = await keys.CreateSigningRequestAsync(applicationId, profile)
                .ConfigureAwait(false);
            ByteString key = await keys.GetSigningKeyCertificateAsync(applicationId, profile, request)
                .ConfigureAwait(false);
            ArrayOf<string> namespaces = reorderedNamespaces
                ? [Namespaces.OpcUa, "urn:unused", "urn:request-context:original"]
                : [Namespaces.OpcUa, "urn:request-context:original"];
            return new PullDcaSigningBinding(
                new PullDcaRegistrar(
                    new Uri("opc.tcp://localhost:4840"),
                    new NodeId("registrar", index), new NodeId("directory", index)),
                applicationId, new NodeId("request", index), profile, request, key,
                Encoded(m_identity), Encoded(m_registrar), kRegistrarUri,
                namespaces, Guid.NewGuid());
        }

        /// <summary>
        /// Publishes a known request ID and releases the local Start fence.
        /// </summary>
        private static async ValueTask StartJournalAsync(DevicePullJournal journal, PullDcaSigningBinding binding)
        {
            await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            await journal.RecordStartedAsync(binding).ConfigureAwait(false);
            await journal.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the journal in the actual scripted session's namespace table.
        /// </summary>
        private static async ValueTask<PullDcaSigningBinding> ResolveAsync(
            DevicePullJournal journal,
            PullDcaSigningBinding binding,
            SessionRequestContextTestContext session)
        {
            ArrayOf<string> namespaces = session.MessageContext.NamespaceUris.ToArrayOf();
            DevicePullJournalCheckpoint current = await journal.ReadForSessionAsync(namespaces).ConfigureAwait(false);
            Assert.That(current.Binding.AttemptId, Is.EqualTo(binding.AttemptId));
            return await journal.ResolveAsync(
                current.Binding.Registrar, current.Binding.ApplicationId, current.Binding.RequestId, namespaces)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Receives only after the actual recovery provider accepts the candidate.
        /// </summary>
        private static ValueTask<PullDcaCertificateReceipt> ReceiveAsync(
            DevicePullJournal journal,
            PullDcaSigningBinding binding,
            DevicePullSigningRecovery recovery,
            PullDcaCredentials candidate)
        {
            return journal.GetOrCreateAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile,
                async ct =>
                {
                    await recovery.ValidateAsync(
                        binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile, candidate, ct)
                        .ConfigureAwait(false);
                    return new PullDcaCertificateReceipt(candidate.Certificate, candidate.IssuerCertificates);
                });
        }

        /// <summary>
        /// Creates a signed certificate and complete registrar trust for a supplied CSR.
        /// </summary>
        private PullDcaCredentials Issue(
            ByteString signingRequest,
            string applicationUri,
            string subject,
            bool expired = false)
        {
            var request = new Pkcs10CertificationRequest(signingRequest.ToArray());
            DateTime notBefore = expired
                ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime notAfter = expired
                ? new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(2098, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetIssuer(m_issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            ByteString issuer = Encoded(m_issuer);
            return new PullDcaCredentials(
                Encoded(certificate),
                [issuer],
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates = [issuer],
                    TrustedCrls = [],
                    IssuerCertificates = [issuer],
                    IssuerCrls = []
                });
        }

        /// <summary>
        /// Creates a confidential session with the original client and registrar certificates.
        /// </summary>
        private SessionRequestContextTestContext CreateSession()
        {
            var session = new SessionRequestContextTestContext(
                telemetry: m_telemetry, securityMode: MessageSecurityMode.SignAndEncrypt);
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(m_identity.RawData);
            session.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_registrar.RawData);
            session.Channel.Object.EndpointDescription.Server.ApplicationUri = kRegistrarUri;
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            return session;
        }

        /// <summary>
        /// Creates a generated-method response for the scripted registrar.
        /// </summary>
        private static ValueTask<IServiceResponse> Reply(StatusCode status, params Variant[] outputs)
        {
            return new ValueTask<IServiceResponse>(new CallResponse
            {
                Results = [new CallMethodResult { StatusCode = status, OutputArguments = outputs }]
            });
        }

        /// <summary>
        /// Forwards real filesystem operations while allowing a selected publication to fail.
        /// </summary>
        private static Mock<IFileSystem> CreateFileSystem(Func<string?> failDestination)
        {
            LocalFileSystem local = LocalFileSystem.Instance;
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(value => value.Exists(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((string path, bool isDirectory) => local.Exists(path, isDirectory));
            fileSystem.Setup(value => value.GetLength(It.IsAny<string>())).Returns<string>(local.GetLength);
            fileSystem.Setup(value => value.OpenRead(It.IsAny<string>())).Returns<string>(local.OpenRead);
            fileSystem.Setup(value => value.OpenWrite(It.IsAny<string>())).Returns<string>(local.OpenWrite);
            fileSystem.Setup(value => value.Delete(It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, bool>(local.Delete);
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((source, destination) =>
                {
                    if (string.Equals(destination, failDestination(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Assignment checkpoint publication failed.");
                    }
                    local.Replace(source, destination);
                });
            return fileSystem;
        }

        /// <summary>
        /// Resolves the writer's digest filename for a checkpoint key.
        /// </summary>
        private string CheckpointPath(string key)
        {
            return Path.Combine(
                m_directory, "checkpoints", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".bin");
        }

        /// <summary>
        /// Names the public baseline for one local attempt.
        /// </summary>
        private static string AssignmentKey(Guid attemptId)
        {
            return "DevicePullAssignments-" + kApplicationUri + "-" + attemptId.ToString("N");
        }

        /// <summary>
        /// Creates an application identity with a private RSA key.
        /// </summary>
        private static Certificate CreateIdentity(string subject, string applicationUri)
        {
            return CertificateBuilder.Create(subject)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Copies a certificate's public DER.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>The device application URI.</summary>
        private const string kApplicationUri = "urn:opcfoundation:tests:pull-recovery-device";

        /// <summary>The authenticated registrar application URI.</summary>
        private const string kRegistrarUri = "urn:opcfoundation:tests:pull-recovery-registrar";

        /// <summary>The selected certificate group and type.</summary>
        private static readonly PullDcaCertificateProfile s_profile = new(
            new NodeId("group", 1), CoreObjectTypeIds.RsaSha256ApplicationCertificateType);

        /// <summary>The maximum synchronization wait.</summary>
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        /// <summary>The isolated test directory.</summary>
        private string m_directory = string.Empty;

        /// <summary>The fixture telemetry.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>The original device identity.</summary>
        private Certificate m_identity = null!;

        /// <summary>The authenticated registrar identity.</summary>
        private Certificate m_registrar = null!;

        /// <summary>The issuer for test certificates.</summary>
        private Certificate m_issuer = null!;

        /// <summary>The identity certificate's issuer chain.</summary>
        private CertificateCollection m_issuers = null!;

        /// <summary>The instrumented validation manager.</summary>
        private Mock<ICertificateManager> m_manager = null!;

        /// <summary>The real directory-store resolver.</summary>
        private CertificateManager m_resolver = null!;
    }
}
#endif
