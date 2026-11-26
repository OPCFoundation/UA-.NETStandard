// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies fresh pull keys against real checkpoint files and a separate directory certificate store.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullSigningKeyStoreTests
    {
        /// <summary>
        /// Creates an isolated device identity and stores for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "pull-key-" + Guid.NewGuid().ToString("N"));
            m_telemetry = NUnitTelemetryContext.Create();
            m_identity = CreateIdentity("CN=Device", kApplicationUri);
            m_registrar = CreateIdentity("CN=Registrar", kRegistrarUri);
            m_chain = new CertificateCollection();
            m_certificates = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_certificates.Setup(manager => manager.AcquireApplicationCertificateByType(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType))
                .Returns(() => new CertificateEntry(
                    m_identity, m_chain, CoreObjectTypeIds.RsaSha256ApplicationCertificateType));
            m_resolver = new CertificateManager(m_telemetry);
        }

        /// <summary>
        /// Releases the fixture-owned certificates and removes only this test's files.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_resolver.Dispose();
            m_chain.Dispose();
            m_identity.Dispose();
            m_registrar.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// An interruption after staging but before dispatch removes the orphan before another key is generated.
        /// </summary>
        [Test]
        public async Task RestartRetiresUndispatchedKeyBeforePreparingAnother()
        {
            ByteString firstCertificate;
            using (var files = OpenFiles())
            {
                DevicePullJournal journal = CreateJournal(files);
                using var keys = CreateKeys(files, journal);
                using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
                ByteString request = await keys.CreateSigningRequestAsync(s_applicationId, s_profile)
                    .ConfigureAwait(false);
                firstCertificate = await keys.GetSigningKeyCertificateAsync(s_applicationId, s_profile, request)
                    .ConfigureAwait(false);
                Assert.That(await ContainsKeyAsync(firstCertificate).ConfigureAwait(false), Is.True);
            }

            using var reopenedFiles = OpenFiles();
            DevicePullJournal reopenedJournal = CreateJournal(reopenedFiles);
            using var reopenedKeys = CreateKeys(reopenedFiles, reopenedJournal);
            await reopenedKeys.ReconcileStagedAsync().ConfigureAwait(false);
            Assert.That(await ContainsKeyAsync(firstCertificate).ConfigureAwait(false), Is.False);
            using IDisposable nextReservation = await reopenedJournal.ReserveDispatchAsync().ConfigureAwait(false);
            ByteString nextRequest = await reopenedKeys.CreateSigningRequestAsync(s_applicationId, s_profile)
                .ConfigureAwait(false);
            ByteString nextCertificate = await reopenedKeys.GetSigningKeyCertificateAsync(
                s_applicationId, s_profile, nextRequest).ConfigureAwait(false);
            Assert.That(nextCertificate, Is.Not.EqualTo(firstCertificate));
            Assert.That(await ContainsKeyAsync(nextCertificate).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// A crash between writing the private PFX and its public index leaves no orphan after recovery.
        /// </summary>
        [Test]
        public async Task RestartRemovesPrivateKeyWithoutPublishedPublicIndex()
        {
            string publicPath;
            string privatePath;
            using (var files = OpenFiles())
            {
                DevicePullJournal journal = CreateJournal(files);
                using var keys = CreateKeys(files, journal);
                using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
                PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
                using Certificate staged = Certificate.FromRawData(binding.SigningKeyCertificate);
                using (var store = (DirectoryCertificateStore)m_resolver.OpenCertificateStore(
                    Path.Combine(m_directory, "signing-keys"), CertificateStoreType.Directory, noPrivateKeys: false))
                {
                    publicPath = store.GetPublicKeyFilePath(staged.Thumbprint)!;
                    privatePath = store.GetPrivateKeyFilePath(staged.Thumbprint)!;
                }
                Assert.That(File.Exists(publicPath), Is.True);
                Assert.That(File.Exists(privatePath), Is.True);
                File.Delete(publicPath);
            }

            using var reopenedFiles = OpenFiles();
            DevicePullJournal reopenedJournal = CreateJournal(reopenedFiles);
            using var reopenedKeys = CreateKeys(reopenedFiles, reopenedJournal);
            await reopenedKeys.ReconcileStagedAsync().ConfigureAwait(false);
            Assert.That(File.Exists(publicPath), Is.False);
            Assert.That(File.Exists(privatePath), Is.False);
        }

        /// <summary>
        /// A crash after the dispatch claim retains the key and never opens a replacement dispatch slot.
        /// </summary>
        [Test]
        public async Task RestartPreservesDispatchUnknownAndItsFreshKey()
        {
            PullDcaSigningBinding binding;
            using (var files = OpenFiles())
            {
                DevicePullJournal journal = CreateJournal(files);
                using var keys = CreateKeys(files, journal);
                using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
                binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
                await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            }

            using var reopenedFiles = OpenFiles();
            DevicePullJournal reopenedJournal = CreateJournal(reopenedFiles);
            using var reopenedKeys = CreateKeys(reopenedFiles, reopenedJournal);
            await reopenedKeys.ReconcileStagedAsync().ConfigureAwait(false);
            DevicePullJournalCheckpoint? checkpoint = await reopenedJournal.ReadAsync().ConfigureAwait(false);
            Assert.That(checkpoint!.Stage, Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
            Assert.That(checkpoint.Binding.AttemptId, Is.EqualTo(binding.AttemptId));
            Assert.That(checkpoint.Binding.SigningKeyCertificate, Is.EqualTo(binding.SigningKeyCertificate));
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.True);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reopenedJournal.ReserveDispatchAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// A received attempt reopens the original key, then retires it only after installation publication.
        /// </summary>
        [Test]
        public async Task ReceivedAttemptReloadsOriginalKeyAcrossRestartAndRetiresAfterPublication()
        {
            PullDcaSigningBinding binding;
            PullDcaCredentials credentials;
            using (var files = OpenFiles())
            {
                DevicePullJournal journal = CreateJournal(files);
                using var keys = CreateKeys(files, journal);
                using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
                binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
                credentials = await RecordReceivedAsync(journal, binding).ConfigureAwait(false);
            }

            using var reopenedFiles = OpenFiles();
            DevicePullJournal reopenedJournal = CreateJournal(reopenedFiles);
            using var reopenedKeys = CreateKeys(reopenedFiles, reopenedJournal);
            await reopenedKeys.ReconcileStagedAsync().ConfigureAwait(false);
            DevicePullJournalCheckpoint remapped = await reopenedJournal.ReadForSessionAsync(
                [Namespaces.OpcUa, "urn:other", "urn:pull-key-test"]).ConfigureAwait(false);
            Assert.That(remapped.Binding.ApplicationId.NamespaceIndex, Is.EqualTo(2));
            await reopenedKeys.RestoreSigningRequestAsync(remapped.Binding).ConfigureAwait(false);
            using Certificate? first = await reopenedKeys.GetSigningKeyAsync(
                binding.AttemptId, credentials.Certificate).ConfigureAwait(false);
            using Certificate? second = await reopenedKeys.GetSigningKeyAsync(
                binding.AttemptId, credentials.Certificate).ConfigureAwait(false);
            using Certificate issued = Certificate.FromRawData(credentials.Certificate);
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first!.HasPrivateKey, Is.True);
            Assert.That(first.RawData, Is.EqualTo(binding.SigningKeyCertificate.ToArray()));
            Assert.That(X509Utils.VerifyKeyPair(issued, first), Is.True);
            Assert.That(X509Utils.VerifyKeyPair(issued, second!), Is.True);
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.True);

            var adapter = new DevicePullInstallationJournal(reopenedJournal, reopenedKeys);
            var applied = new ProvisionableDeviceCredentials(
                credentials.Certificate, credentials.IssuerCertificates, credentials.TrustList);
            await adapter.RecordInstalledAsync(
                binding.AttemptId, Encoded(m_registrar), applied).ConfigureAwait(false);
            await adapter.RecordInstalledAsync(
                binding.AttemptId, Encoded(m_registrar), applied).ConfigureAwait(false);
            Assert.That((await reopenedJournal.ReadAsync().ConfigureAwait(false))!.Stage,
                Is.EqualTo(DevicePullJournalStage.Installed));
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Restart finishes retirement when the Installed marker was published before key cleanup.
        /// </summary>
        [Test]
        public async Task RestartFinishesKeyRetirementAfterInstalledPublication()
        {
            PullDcaSigningBinding binding;
            PullDcaCredentials credentials;
            using (var files = OpenFiles())
            {
                DevicePullJournal journal = CreateJournal(files);
                using var keys = CreateKeys(files, journal);
                using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
                binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
                credentials = await RecordReceivedAsync(journal, binding).ConfigureAwait(false);
                await journal.RecordInstalledAsync(
                    binding.AttemptId, Encoded(m_registrar), credentials).ConfigureAwait(false);
                Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.True);
            }

            using var reopenedFiles = OpenFiles();
            DevicePullJournal reopenedJournal = CreateJournal(reopenedFiles);
            using var reopenedKeys = CreateKeys(reopenedFiles, reopenedJournal);
            var adapter = new DevicePullInstallationJournal(reopenedJournal, reopenedKeys);
            await adapter.RecordInstalledAsync(
                binding.AttemptId,
                Encoded(m_registrar),
                new ProvisionableDeviceCredentials(
                    credentials.Certificate, credentials.IssuerCertificates, credentials.TrustList))
                .ConfigureAwait(false);
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Repeated reset completion cannot remove a key prepared under the new reset generation.
        /// </summary>
        [Test]
        public async Task ResetRetiresOldKeyWithoutDeletingTheNewGeneration()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            var adapter = new DevicePullInstallationJournal(journal, keys);
            ByteString oldCertificate;
            using (IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false))
            {
                PullDcaSigningBinding old = await PrepareBindingAsync(keys).ConfigureAwait(false);
                oldCertificate = old.SigningKeyCertificate;
                await journal.RecordDispatchAsync(old with { RequestId = NodeId.Null }).ConfigureAwait(false);
                await journal.OnStartCompletedAsync(old.AttemptId).ConfigureAwait(false);
                await adapter.BeginResetAsync().ConfigureAwait(false);
                await adapter.CompleteResetAsync().ConfigureAwait(false);
                Assert.That(await ContainsKeyAsync(oldCertificate).ConfigureAwait(false), Is.False);
                Assert.That(await journal.ReadRetiredAsync().ConfigureAwait(false), Has.Count.EqualTo(1));
            }

            using IDisposable newReservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding replacement = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await adapter.CompleteResetAsync().ConfigureAwait(false);
            Assert.That(replacement.SigningKeyCertificate, Is.Not.EqualTo(oldCertificate));
            Assert.That(await ContainsKeyAsync(replacement.SigningKeyCertificate).ConfigureAwait(false), Is.True);
            ServiceResultException? stale = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await keys.RetireInstalledAsync(Guid.NewGuid()).ConfigureAwait(false));
            Assert.That(stale!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await ContainsKeyAsync(replacement.SigningKeyCertificate).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// A delayed cleanup for an older installed attempt cannot delete a newer staged key.
        /// </summary>
        [Test]
        public async Task OlderInstalledCheckpointCannotRetireAnUnrelatedNewKey()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            var adapter = new DevicePullInstallationJournal(journal, keys);
            PullDcaSigningBinding old;
            byte[] installed;
            using (IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false))
            {
                old = await PrepareBindingAsync(keys).ConfigureAwait(false);
                PullDcaCredentials credentials = await RecordReceivedAsync(journal, old).ConfigureAwait(false);
                await adapter.RecordInstalledAsync(
                    old.AttemptId, Encoded(m_registrar),
                    new ProvisionableDeviceCredentials(
                        credentials.Certificate, credentials.IssuerCertificates, credentials.TrustList))
                    .ConfigureAwait(false);
                installed = (await files.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false))!;
                await adapter.BeginResetAsync().ConfigureAwait(false);
                await adapter.CompleteResetAsync().ConfigureAwait(false);
            }
            using IDisposable nextReservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding replacement = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await files.WriteAsync(kJournalKey, installed, CancellationToken.None).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await keys.RetireInstalledAsync(old.AttemptId).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await ContainsKeyAsync(replacement.SigningKeyCertificate).ConfigureAwait(false), Is.True);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await keys.ReconcileStagedAsync().ConfigureAwait(false));
            Assert.That(await ContainsKeyAsync(replacement.SigningKeyCertificate).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// A failed reset publication keeps the retained key and reports the failure until reset is retried.
        /// </summary>
        [TestCase("begin")]
        [TestCase("complete")]
        [TestCase("retirement")]
        public async Task ResetPublicationFailurePreservesTheKeyUntilRetry(string failureStage)
        {
            string? failDestination = null;
            Mock<IFileSystem> fileSystem = CreateFileSystem(() => failDestination);
            using var files = new PushRegistrarCheckpointFiles(
                Path.Combine(m_directory, "checkpoints"), fileSystem.Object);
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            var adapter = new DevicePullInstallationJournal(journal, keys);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            await journal.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
            if (failureStage == "begin")
            {
                failDestination = CheckpointPath(kJournalKey);
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await adapter.BeginResetAsync().ConfigureAwait(false));
                Assert.That(error!.Message, Is.EqualTo("Checkpoint publication failed."));
                Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                    Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
                failDestination = null;
            }
            await adapter.BeginResetAsync().ConfigureAwait(false);
            failDestination = CheckpointPath(failureStage == "retirement" ? kPreparedKey : kJournalKey);
            if (failureStage != "begin")
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await adapter.CompleteResetAsync().ConfigureAwait(false));
                Assert.That(error!.Message, Is.EqualTo("Checkpoint publication failed."));
                Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.True);
            }
            failDestination = null;
            await adapter.CompleteResetAsync().ConfigureAwait(false);
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.False);
            Assert.That(await journal.ReadRetiredAsync().ConfigureAwait(false), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Reset fences a CSR being staged and waits for its key to be retired before reporting completion.
        /// </summary>
        [Test]
        public async Task ResetWaitsForStagingAndFencesItsDispatch()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using ICertificateStore actual = m_resolver.OpenCertificateStore(
                Path.Combine(m_directory, "signing-keys"), CertificateStoreType.Directory, noPrivateKeys: false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ByteString stagedCertificate = default;
            var blocked = new Mock<ICertificateStore>(MockBehavior.Strict);
            blocked.SetupGet(store => store.SupportsLoadPrivateKey).Returns(true);
            blocked.Setup(store => store.AddAsync(
                    It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()))
                .Returns(async (Certificate certificate, char[]? password, CancellationToken ct) =>
                {
                    stagedCertificate = Encoded(certificate);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                    await actual.AddAsync(certificate, password, ct).ConfigureAwait(false);
                });
            blocked.Setup(store => store.LoadPrivateKeyAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<NodeId>(),
                    It.IsAny<char[]?>(), It.IsAny<CancellationToken>()))
                .Returns((string thumbprint, string? subject, string? application, NodeId type,
                    char[]? password, CancellationToken ct) =>
                    actual.LoadPrivateKeyAsync(thumbprint, subject, application, type, password, ct));
            blocked.Setup(store => store.FindByThumbprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string thumbprint, CancellationToken ct) => actual.FindByThumbprintAsync(thumbprint, ct));
            blocked.Setup(store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string thumbprint, CancellationToken ct) => actual.DeleteAsync(thumbprint, ct));
            blocked.Setup(store => store.Dispose());
            var resolver = new Mock<ICertificateStoreResolver>(MockBehavior.Strict);
            resolver.Setup(value => value.OpenCertificateStore(
                    It.IsAny<string>(), CertificateStoreType.Directory, false))
                .Returns(blocked.Object);
            using var keys = CreateKeys(files, journal, resolver.Object);
            var adapter = new DevicePullInstallationJournal(journal, keys);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            Task<ByteString> preparing = keys.CreateSigningRequestAsync(s_applicationId, s_profile).AsTask();
            Task? resetting = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                await adapter.BeginResetAsync().ConfigureAwait(false);
                resetting = adapter.CompleteResetAsync().AsTask();
                DateTime deadline = DateTime.UtcNow.Add(s_timeout);
                while (!await journal.HasCompletedResetAfterAsync(default).ConfigureAwait(false))
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("Reset did not publish its retired generation.");
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
                Assert.That(resetting.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await ObserveCompletionAsync(preparing).ConfigureAwait(false);
                if (resetting != null)
                {
                    await ObserveCompletionAsync(resetting).ConfigureAwait(false);
                }
            }
            await preparing.WaitAsync(s_timeout).ConfigureAwait(false);
            await resetting!.WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(await ContainsKeyAsync(stagedCertificate).ConfigureAwait(false), Is.False);
            ServiceResultException? stale = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await journal.RequireReservedGenerationAsync().ConfigureAwait(false));
            Assert.That(stale!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Selection never changes the active identity and rejects a different channel certificate.
        /// </summary>
        [Test]
        public async Task IdentitySelectionRequiresTheAlreadyActiveKeyedCertificate()
        {
            var identity = new DevicePullActiveIdentity(m_certificates.Object);
            await identity.ActivateAsync(Encoded(m_identity)).ConfigureAwait(false);
            ServiceResultException? mismatch = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await identity.ActivateAsync(Encoded(m_registrar)).ConfigureAwait(false));
            Assert.That(mismatch!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            m_certificates.Verify(manager => manager.UpdateApplicationCertificateAsync(
                It.IsAny<NodeId>(), It.IsAny<Certificate>(), It.IsAny<CertificateCollection?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Completion cleans an unclaimed key but retains a key referenced by an uncertain dispatch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AttemptCleanupOnlyRemovesAnUndispatchedKey(bool dispatchRecorded)
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            if (dispatchRecorded)
            {
                await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
                await journal.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
            }
            await keys.RetireUndispatchedAsync().ConfigureAwait(false);
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false),
                Is.EqualTo(dispatchRecorded));
            DevicePullJournalCheckpoint? checkpoint = await journal.ReadAsync().ConfigureAwait(false);
            Assert.That(checkpoint?.Stage,
                Is.EqualTo(dispatchRecorded ? DevicePullJournalStage.DispatchUnknown : null));
        }

        /// <summary>
        /// An incorrect attempt, receipt, or profile cannot borrow the retained private key.
        /// </summary>
        [Test]
        public async Task MismatchedAttemptReceiptAndProfileAreRejectedWithoutKeyLoss()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            PullDcaCredentials credentials = await RecordReceivedAsync(journal, binding).ConfigureAwait(false);
            using Certificate unrelated = CreateIdentity("CN=Unrelated", kApplicationUri);

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await keys.GetSigningKeyAsync(Guid.NewGuid(), credentials.Certificate).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await keys.GetSigningKeyAsync(binding.AttemptId, Encoded(unrelated)).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await keys.RestoreSigningRequestAsync(binding with
                {
                    Profile = binding.Profile with { CertificateGroupId = new NodeId("other", 1) }
                }).ConfigureAwait(false));
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// A certificate using a different public key cannot become the retained receipt.
        /// </summary>
        [Test]
        public async Task ReceiptWithDifferentPublicKeyCannotBePublished()
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            using IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false);
            PullDcaSigningBinding binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
            await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            await journal.RecordStartedAsync(binding).ConfigureAwait(false);
            await journal.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
            using Certificate unrelated = CreateIdentity("CN=Renamed by authority", kApplicationUri);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await journal.GetOrCreateAsync(
                    binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile,
                    _ => new ValueTask<PullDcaCertificateReceipt>(
                        new PullDcaCertificateReceipt(Encoded(unrelated), [])))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            DevicePullJournalCheckpoint? checkpoint = await journal.ReadAsync().ConfigureAwait(false);
            Assert.That(checkpoint!.Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
            Assert.That(checkpoint.Receipt, Is.Null);
            Assert.That(await ContainsKeyAsync(binding.SigningKeyCertificate).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// Recovery retrieves managers on the attempt-owned session using the installed channel certificate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecoveryContinuesOnTheOwnedInstalledSession(bool alreadyInstalled)
        {
            await ExerciseInstalledContinuationAsync(alreadyInstalled, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Recovery rejects a changed client, registrar, or endpoint before retrieving managers.
        /// </summary>
        [TestCase(false, "client")]
        [TestCase(false, "registrar")]
        [TestCase(false, "endpoint")]
        [TestCase(true, "client")]
        [TestCase(true, "registrar")]
        [TestCase(true, "endpoint")]
        public async Task RecoveryRejectsChangedInstalledChannelBeforeManagers(bool alreadyInstalled, string mismatch)
        {
            await ExerciseInstalledContinuationAsync(alreadyInstalled, mismatch).ConfigureAwait(false);
        }

        /// <summary>
        /// A reset that wins admission revokes the captured deployment before the manager request can run.
        /// </summary>
        [Test]
        public async Task ResetBeforeManagerAdmissionRejectsTheRetainedAttempt()
        {
            var resetEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReset = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await WithInstalledDeviceAsync(
                async ct =>
                {
                    resetEntered.TrySetResult(true);
                    await releaseReset.Task.WaitAsync(s_timeout, ct).ConfigureAwait(false);
                },
                async (security, journal, binding, credentials) =>
                {
                    var operation = new DevicePullInstalledManagerOperation(
                        security.CreateApplicationOperations(), journal);
                    int calls = 0;
                    Task resetting = security.ResetToServerDefaultsAsync().AsTask();
                    Task<ArrayOf<ManagerDescription>>? managers = null;
                    try
                    {
                        await resetEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                        managers = operation.ExecuteAsync(
                            binding,
                            credentials.Certificate,
                            _ =>
                            {
                                calls++;
                                return new ValueTask<ArrayOf<ManagerDescription>>(
                                    ArrayOf<ManagerDescription>.Empty);
                            }).AsTask();
                        Assert.That(managers.IsCompleted, Is.False);
                        releaseReset.TrySetResult(true);
                        await resetting.WaitAsync(s_timeout).ConfigureAwait(false);
                        ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await managers.WaitAsync(s_timeout).ConfigureAwait(false));
                        Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                        Assert.That(calls, Is.Zero);
                        Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
                    }
                    finally
                    {
                        releaseReset.TrySetResult(true);
                        await ObserveCompletionAsync(resetting).ConfigureAwait(false);
                        if (managers != null)
                        {
                            await ObserveCompletionAsync(managers).ConfigureAwait(false);
                        }
                    }
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// A reset waits until an admitted manager request returns before invalidating its attempt.
        /// </summary>
        [Test]
        public async Task ResetWaitsForAdmittedInstalledManagerRequest()
        {
            var managerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseManager = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resetEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await WithInstalledDeviceAsync(
                _ =>
                {
                    resetEntered.TrySetResult(true);
                    return ValueTask.CompletedTask;
                },
                async (security, journal, binding, credentials) =>
                {
                    var operation = new DevicePullInstalledManagerOperation(
                        security.CreateApplicationOperations(), journal);
                    int calls = 0;
                    Task<ArrayOf<ManagerDescription>> managers = operation.ExecuteAsync(
                        binding,
                        credentials.Certificate,
                        async ct =>
                        {
                            calls++;
                            managerEntered.TrySetResult(true);
                            await releaseManager.Task.WaitAsync(s_timeout, ct).ConfigureAwait(false);
                            return ArrayOf<ManagerDescription>.Empty;
                        }).AsTask();
                    Task? resetting = null;
                    try
                    {
                        await managerEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                        resetting = security.ResetToServerDefaultsAsync().AsTask();
                        Assert.That(resetting.IsCompleted, Is.False);
                        Assert.That(resetEntered.Task.IsCompleted, Is.False);
                        Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                            Is.EqualTo(DevicePullJournalStage.Installed));
                        releaseManager.TrySetResult(true);
                        Assert.That((await managers.WaitAsync(s_timeout).ConfigureAwait(false)).Count, Is.Zero);
                        await resetting.WaitAsync(s_timeout).ConfigureAwait(false);
                        Assert.That(calls, Is.EqualTo(1));
                        Assert.That(resetEntered.Task.IsCompleted, Is.True);
                        Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
                    }
                    finally
                    {
                        releaseManager.TrySetResult(true);
                        await ObserveCompletionAsync(managers).ConfigureAwait(false);
                        if (resetting != null)
                        {
                            await ObserveCompletionAsync(resetting).ConfigureAwait(false);
                        }
                    }
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// A live deployment still rejects a different journal attempt or issued certificate.
        /// </summary>
        [TestCase("attempt")]
        [TestCase("certificate")]
        public async Task InstalledManagerRequestRequiresTheExactJournalReceipt(string mismatch)
        {
            await WithInstalledDeviceAsync(
                _ => ValueTask.CompletedTask,
                async (security, journal, binding, credentials) =>
                {
                    var operation = new DevicePullInstalledManagerOperation(
                        security.CreateApplicationOperations(), journal);
                    PullDcaSigningBinding selected = mismatch == "attempt"
                        ? binding with { AttemptId = Guid.NewGuid() }
                        : binding;
                    ByteString certificate = mismatch == "certificate"
                        ? Encoded(m_identity)
                        : credentials.Certificate;
                    int calls = 0;

                    ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await operation.ExecuteAsync(
                            selected,
                            certificate,
                            _ =>
                            {
                                calls++;
                                return new ValueTask<ArrayOf<ManagerDescription>>(
                                    ArrayOf<ManagerDescription>.Empty);
                            }).ConfigureAwait(false));

                    Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    Assert.That(calls, Is.Zero);
                    Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                        Is.EqualTo(DevicePullJournalStage.Installed));
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates an installed journal and real security owner with controlled platform effects.
        /// </summary>
        private async Task WithInstalledDeviceAsync(
            Func<CancellationToken, ValueTask> reset,
            Func<ProvisionableDeviceSecurity, DevicePullJournal, PullDcaSigningBinding, PullDcaCredentials, Task> test)
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            PullDcaSigningBinding binding;
            PullDcaCredentials issued;
            using (IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false))
            {
                binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
                issued = await RecordReceivedAsync(journal, binding).ConfigureAwait(false);
            }
            var credentials = new ProvisionableDeviceCredentials(
                issued.Certificate, issued.IssuerCertificates, issued.TrustList);
            m_certificates.Setup(manager => manager.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            var installer = new Mock<IProvisionableDeviceSecurityInstaller>(MockBehavior.Strict);
            Mock<IProvisionableDevicePullSecurityInstaller> pull =
                installer.As<IProvisionableDevicePullSecurityInstaller>();
            Mock<IProvisionableDeviceAppliedSecurity> applied = installer.As<IProvisionableDeviceAppliedSecurity>();
            pull.Setup(value => value.RequirePullKeyAsync(
                    It.IsAny<Guid>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            pull.Setup(value => value.VerifyInstalledPullKeyAsync(
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            pull.Setup(value => value.InstallForPullAsync(
                    It.IsAny<Guid>(), It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(credentials);
            installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => reset(token));
            using var security = new ProvisionableDeviceSecurity(
                new InMemorySecretStore(), "manager-test", kApplicationUri, m_certificates.Object,
                installer.Object, m_telemetry, new DevicePullInstallationJournal(journal));
            await using var session = new SessionRequestContextTestContext(
                telemetry: m_telemetry, securityMode: MessageSecurityMode.SignAndEncrypt);
            session.Channel.Object.EndpointDescription.Server.ApplicationUri = kRegistrarUri;
            session.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_registrar.RawData);
            await security.InstallFromPullAsync(
                session.ManagedSession, kRegistrarUri, credentials, binding.AttemptId).ConfigureAwait(false);
            await test(security, journal, binding, issued).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs a received or installed checkpoint through the sample's actual startup task.
        /// </summary>
        private async Task ExerciseInstalledContinuationAsync(bool alreadyInstalled, string? mismatch)
        {
            using var files = OpenFiles();
            DevicePullJournal journal = CreateJournal(files);
            using var keys = CreateKeys(files, journal);
            PullDcaSigningBinding binding;
            PullDcaCredentials issued;
            using (IDisposable reservation = await journal.ReserveDispatchAsync().ConfigureAwait(false))
            {
                binding = await PrepareBindingAsync(keys).ConfigureAwait(false);
                issued = await RecordReceivedAsync(journal, binding).ConfigureAwait(false);
                if (alreadyInstalled)
                {
                    await journal.RecordInstalledAsync(
                        binding.AttemptId, Encoded(m_registrar), issued).ConfigureAwait(false);
                }
            }

            await using var session = new SessionRequestContextTestContext(
                telemetry: m_telemetry, securityMode: MessageSecurityMode.SignAndEncrypt);
            session.MessageContext.NamespaceUris.GetIndexOrAppend("urn:pull-key-test");
            session.Channel.Object.EndpointDescription.EndpointUrl = binding.Registrar.EndpointUrl.AbsoluteUri;
            session.Channel.Object.EndpointDescription.Server.ApplicationUri = kRegistrarUri;
            byte[] clientCertificate = alreadyInstalled ? issued.Certificate.ToArray() : m_identity.RawData;
            byte[] registrarCertificate = m_registrar.RawData;
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(() => clientCertificate);
            session.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(() => registrarCertificate);
            if (alreadyInstalled && mismatch != null)
            {
                clientCertificate = mismatch == "client" ? m_identity.RawData : issued.Certificate.ToArray();
                registrarCertificate = mismatch == "registrar" ? m_identity.RawData : m_registrar.RawData;
                if (mismatch == "endpoint")
                {
                    session.Channel.Object.EndpointDescription.EndpointUrl = "opc.tcp://localhost:4841";
                }
            }

            int created = 0;
            var sessions = new DevicePullSession(_ =>
            {
                created++;
                return Task.FromResult(session.ManagedSession);
            });
            ProvisionableDeviceSecurityStage stage = alreadyInstalled
                ? ProvisionableDeviceSecurityStage.Installed
                : ProvisionableDeviceSecurityStage.Unconfigured;
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            var operations = new Mock<IApplicationConfigurationPushOperations>(MockBehavior.Strict);
            operations.Setup(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<ArrayOf<ManagerDescription>>>>(), It.IsAny<CancellationToken>()))
                .Returns((
                    Func<ValueTask<ArrayOf<ManagerDescription>>> operation,
                    CancellationToken _) => operation());
            Mock<IProvisionableDevicePullInstallation> installation =
                security.As<IProvisionableDevicePullInstallation>();
            Mock<IProvisionableDevicePullInstallationRecovery> recovery =
                security.As<IProvisionableDevicePullInstallationRecovery>();
            bool capturedOperation = false;
            security.Setup(value => value.CreateApplicationOperations())
                .Returns(() =>
                {
                    capturedOperation = true;
                    return operations.Object;
                });
            security.Setup(value => value.GetStageAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Assert.That(capturedOperation, Is.True);
                    return new ValueTask<ProvisionableDeviceSecurityStage>(stage);
                });
            installation.Setup(value => value.InstallFromPullAsync(
                    It.IsAny<Opc.Ua.Client.ISession>(), kRegistrarUri, It.IsAny<ProvisionableDeviceCredentials>(),
                    binding.AttemptId, It.IsAny<CancellationToken>()))
                .Returns(async (
                    Opc.Ua.Client.ISession connected,
                    string _,
                    ProvisionableDeviceCredentials installed,
                    Guid attemptId,
                    CancellationToken ct) =>
                {
                    Assert.That(connected, Is.SameAs(session.ManagedSession));
                    Assert.That(installed.Certificate, Is.EqualTo(issued.Certificate));
                    await journal.RecordInstalledAsync(attemptId, Encoded(m_registrar), issued, ct)
                        .ConfigureAwait(false);
                    stage = ProvisionableDeviceSecurityStage.Installed;
                });
            recovery.Setup(value => value.CompleteInterruptedPullAsync(
                    binding.AttemptId, It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Guid, ByteString, ProvisionableDeviceCredentials, CancellationToken>(
                    (_, registrar, installed, _) =>
                    {
                        Assert.That(registrar, Is.EqualTo(Encoded(m_registrar)));
                        Assert.That(installed.Certificate, Is.EqualTo(issued.Certificate));
                    })
                .Returns(default(ValueTask));
            var identity = new Mock<IPullDcaIdentityProvider>(MockBehavior.Strict);
            identity.Setup(value => value.ActivateAsync(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var credentials = new DevicePullCredentials(
                identity.Object, keys, security.Object, sessions.GetCurrentAsync, kRegistrarUri);
            var connection = new FakePullDcaBoundSigningConnection(
                async (NodeId application, NodeId request, CancellationToken ct) =>
                {
                    DevicePullJournalCheckpoint resolved = await journal.ReadForSessionAsync(
                        session.ManagedSession.NamespaceUris.ToArrayOf(), ct).ConfigureAwait(false);
                    Assert.That(application, Is.EqualTo(resolved.Binding.ApplicationId));
                    Assert.That(request, Is.EqualTo(resolved.Binding.RequestId));
                    return resolved.Binding;
                })
            {
                GetManagersOnInstalledChannel = (retained, installedCertificate, _) =>
                {
                    Assert.That(retained?.AttemptId, Is.EqualTo(binding.AttemptId));
                    Assert.That(installedCertificate, Is.EqualTo(issued.Certificate));
                    return new ValueTask<ArrayOf<ManagerDescription>>(ArrayOf<ManagerDescription>.Empty);
                }
            };
            int connectionFactories = 0;
            var workflow = new PullDcaWorkflow(
                new PullDcaOptions(),
                async (registrar, ct) =>
                {
                    connectionFactories++;
                    Assert.That(await sessions.GetCurrentAsync(ct).ConfigureAwait(false),
                        Is.SameAs(session.ManagedSession));
                    Assert.That(registrar.RegistrarId.NamespaceIndex,
                        Is.EqualTo(session.ManagedSession.NamespaceUris.GetIndex("urn:pull-key-test")));
                    return connection;
                },
                credentials, credentials, receipts: journal, signingJournal: journal);
            var registrars = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            registrars.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ApplicationDescription>>(
                [
                    new ApplicationDescription
                    {
                        ApplicationUri = kRegistrarUri,
                        ApplicationType = ApplicationType.Server,
                        DiscoveryUrls = [binding.Registrar.EndpointUrl.AbsoluteUri]
                    }
                ]));
            var values = new Dictionary<string, string?>
            {
                ["pkiRoot"] = m_directory,
                ["applicationUri"] = kApplicationUri,
                ["productUri"] = "urn:pull-key-test:product",
                ["productInstanceUri"] = "urn:pull-key-test:instance",
                ["subjectName"] = "CN=Device",
                ["factoryCertificate"] = Path.Combine(m_directory, "identity.der"),
                ["ticketFiles"] = Path.Combine(m_directory, "ticket.json"),
                ["endpointUrl"] = "opc.tcp://localhost:62561/Device",
                ["pullRegistrarEndpoint"] = binding.Registrar.EndpointUrl.AbsoluteUri,
                ["pullRegistrarApplicationUri"] = kRegistrarUri
            };
            int reconnects = 0;
            var task = new DevicePullTask(
                new DeviceSampleOptions(new ConfigurationBuilder().AddInMemoryCollection(values).Build()),
                security.Object, registrars.Object, Mock.Of<IProvisionableDeviceRuntime>(),
                Mock.Of<IOpcUaApplicationConfigurationProvider>(), workflow, sessions, journal, credentials,
                signingKeys: keys,
                reconnectInstalled: (connected, _) =>
                {
                    Assert.That(connected, Is.SameAs(session.ManagedSession));
                    reconnects++;
                    clientCertificate = mismatch == "client" ? m_identity.RawData : issued.Certificate.ToArray();
                    registrarCertificate = mismatch == "registrar" ? m_identity.RawData : m_registrar.RawData;
                    if (mismatch == "endpoint")
                    {
                        session.Channel.Object.EndpointDescription.EndpointUrl = "opc.tcp://localhost:4841";
                    }
                    return ValueTask.CompletedTask;
                });
            if (mismatch == null)
            {
                await task.OnServerStartedAsync(Mock.Of<IServerContext>()).ConfigureAwait(false);
                Assert.That(connection.GetManagersOnInstalledChannelCalls, Is.EqualTo(1));
                Assert.That(connectionFactories, Is.EqualTo(alreadyInstalled ? 1 : 2));
            }
            else
            {
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await task.OnServerStartedAsync(Mock.Of<IServerContext>()).ConfigureAwait(false));
                Assert.That(connection.GetManagersOnInstalledChannelCalls, Is.Zero);
                Assert.That(connectionFactories, Is.EqualTo(alreadyInstalled ? 0 : 1));
            }
            identity.Verify(value => value.ActivateAsync(
                    alreadyInstalled ? issued.Certificate : binding.ClientChannelCertificate,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            installation.Verify(value => value.InstallFromPullAsync(
                    session.ManagedSession, kRegistrarUri, It.IsAny<ProvisionableDeviceCredentials>(),
                    binding.AttemptId, It.IsAny<CancellationToken>()),
                alreadyInstalled ? Times.Never() : Times.Once());
            recovery.Verify(value => value.CompleteInterruptedPullAsync(
                    binding.AttemptId, It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()),
                alreadyInstalled ? Times.Once() : Times.Never());
            Assert.That(created, Is.EqualTo(1));
            Assert.That(reconnects, Is.EqualTo(alreadyInstalled ? 0 : 1));
            security.Verify(value => value.CreateApplicationOperations(), Times.Once);
            operations.Verify(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<ArrayOf<ManagerDescription>>>>(), It.IsAny<CancellationToken>()),
                mismatch == null ? Times.Once() : Times.Never());
            Assert.That(session.ManagedSession.Disposed, Is.True);
            Assert.That(session.Requests.FindAll(static request => request is CloseSessionRequest),
                Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Opens the test's exclusively leased checkpoint directory.
        /// </summary>
        private PushRegistrarCheckpointFiles OpenFiles()
        {
            return new PushRegistrarCheckpointFiles(Path.Combine(m_directory, "checkpoints"));
        }

        /// <summary>
        /// Forwards real file operations while allowing one publication destination to fail.
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
                        throw new IOException("Checkpoint publication failed.");
                    }
                    local.Replace(source, destination);
                });
            return fileSystem;
        }

        /// <summary>
        /// Resolves the sample writer's digest filename for a checkpoint key.
        /// </summary>
        private string CheckpointPath(string key)
        {
            return Path.Combine(
                m_directory, "checkpoints",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".bin");
        }

        /// <summary>
        /// Creates a journal that requires an attempt-owned dispatch generation.
        /// </summary>
        private DevicePullJournal CreateJournal(PushRegistrarCheckpointFiles files)
        {
            return new DevicePullJournal(
                files, kApplicationUri, kRegistrarUri, m_telemetry, null, requireDispatchReservation: true);
        }

        /// <summary>
        /// Creates a key provider over the same dedicated directory after each simulated restart.
        /// </summary>
        private DevicePullSigningKeyStore CreateKeys(
            PushRegistrarCheckpointFiles files,
            DevicePullJournal journal,
            ICertificateStoreResolver? resolver = null)
        {
            return new DevicePullSigningKeyStore(
                files, journal, m_certificates.Object, resolver ?? m_resolver, kApplicationUri,
                Path.Combine(m_directory, "signing-keys"), CertificateStoreType.Directory, m_telemetry);
        }

        /// <summary>
        /// Prepares a fresh CSR and its complete channel and attempt binding.
        /// </summary>
        private async ValueTask<PullDcaSigningBinding> PrepareBindingAsync(DevicePullSigningKeyStore keys)
        {
            ByteString request = await keys.CreateSigningRequestAsync(s_applicationId, s_profile)
                .ConfigureAwait(false);
            ByteString certificate = await keys.GetSigningKeyCertificateAsync(s_applicationId, s_profile, request)
                .ConfigureAwait(false);
            return new PullDcaSigningBinding(
                new PullDcaRegistrar(
                    new Uri("opc.tcp://localhost:4840"), new NodeId("registrar", 1), new NodeId("directory", 1)),
                s_applicationId, new NodeId("request", 1), s_profile, request, certificate,
                Encoded(m_identity), Encoded(m_registrar), kRegistrarUri,
                [Namespaces.OpcUa, "urn:pull-key-test"], Guid.NewGuid());
        }

        /// <summary>
        /// Publishes a signed receipt and complete trust list for the specified request.
        /// </summary>
        private async ValueTask<PullDcaCredentials> RecordReceivedAsync(
            DevicePullJournal journal,
            PullDcaSigningBinding binding)
        {
            await journal.RecordDispatchAsync(binding with { RequestId = NodeId.Null }).ConfigureAwait(false);
            await journal.RecordStartedAsync(binding).ConfigureAwait(false);
            await journal.OnStartCompletedAsync(binding.AttemptId).ConfigureAwait(false);
            using Certificate issuer = CertificateBuilder.Create("CN=Issuer")
                .SetCAConstraint().SetRSAKeySize(2048).CreateForRSA();
            var request = new Pkcs10CertificationRequest(binding.SigningRequest.ToArray());
            using Certificate issued = CertificateBuilder.Create("CN=Renamed by authority")
                .AddExtension(new X509SubjectAltNameExtension(kApplicationUri, ["localhost"]))
                .SetIssuer(issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            var receipt = new PullDcaCertificateReceipt(Encoded(issued), [Encoded(issuer)]);
            await journal.GetOrCreateAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile,
                _ => new ValueTask<PullDcaCertificateReceipt>(receipt)).ConfigureAwait(false);
            var credentials = new PullDcaCredentials(
                receipt.Certificate, receipt.IssuerCertificates,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates = [Encoded(m_registrar)],
                    TrustedCrls = [],
                    IssuerCertificates = [Encoded(issuer)],
                    IssuerCrls = []
                });
            await journal.RecordReceivedAsync(binding, credentials).ConfigureAwait(false);
            return credentials;
        }

        /// <summary>
        /// Checks public certificate presence in the dedicated key store.
        /// </summary>
        private async ValueTask<bool> ContainsKeyAsync(ByteString certificate)
        {
            using Certificate expected = Certificate.FromRawData(certificate);
            using ICertificateStore store = m_resolver.OpenCertificateStore(
                Path.Combine(m_directory, "signing-keys"), CertificateStoreType.Directory, noPrivateKeys: false);
            using CertificateCollection matches = await store.FindByThumbprintAsync(expected.Thumbprint)
                .ConfigureAwait(false);
            return matches.Count != 0;
        }

        /// <summary>
        /// Observes an interrupted operation before its fixture-owned stores are disposed.
        /// </summary>
        private static async Task ObserveCompletionAsync(Task operation)
        {
            try
            {
                await operation.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception)
            {
                // The test asserts the operation's outcome after cleanup.
            }
        }

        /// <summary>
        /// Creates a keyed application certificate.
        /// </summary>
        private static Certificate CreateIdentity(string subject, string applicationUri)
        {
            return CertificateBuilder.Create(subject)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Copies public certificate bytes for a journal binding.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>The device application URI.</summary>
        private const string kApplicationUri = "urn:opcfoundation:tests:pull-key-device";

        /// <summary>The registrar application URI.</summary>
        private const string kRegistrarUri = "urn:opcfoundation:tests:pull-key-registrar";

        /// <summary>The pull journal's public checkpoint key.</summary>
        private const string kJournalKey = "DevicePullSigning-" + kApplicationUri;

        /// <summary>The prepared signing key's public checkpoint key.</summary>
        private const string kPreparedKey = "DevicePullPreparedKey-" + kApplicationUri;

        /// <summary>The selected application identifier.</summary>
        private static readonly NodeId s_applicationId = new("device", 1);

        /// <summary>The selected certificate profile.</summary>
        private static readonly PullDcaCertificateProfile s_profile = new(
            new NodeId("group", 1), CoreObjectTypeIds.RsaSha256ApplicationCertificateType);

        /// <summary>The bounded duration of a synchronization barrier.</summary>
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        /// <summary>The isolated test directory.</summary>
        private string m_directory = string.Empty;

        /// <summary>The test telemetry context.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>The active device identity.</summary>
        private Certificate m_identity = null!;

        /// <summary>The authenticated registrar identity.</summary>
        private Certificate m_registrar = null!;

        /// <summary>The shared empty identity issuer chain.</summary>
        private CertificateCollection m_chain = null!;

        /// <summary>The active identity registry.</summary>
        private Mock<ICertificateManager> m_certificates = null!;

        /// <summary>The real directory store resolver.</summary>
        private CertificateManager m_resolver = null!;
    }
}
#endif
