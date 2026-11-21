/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises retained FinishRequest retries and reset fencing with real signing evidence.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullReceiptRetryTests
    {
        /// <summary>
        /// Creates a request key, registrar certificate, and issued certificate for an isolated journal.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                Path.GetTempPath(), nameof(DevicePullReceiptRetryTests), Guid.NewGuid().ToString("N"));
            m_signingKey = CertificateBuilder.Create("CN=Pull request key")
                .AddExtension(new X509SubjectAltNameExtension(kDeviceUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_registrar = CertificateBuilder.Create("CN=Pull registrar")
                .SetCAConstraint()
                .AddExtension(new X509SubjectAltNameExtension(kRegistrarUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(m_signingKey).AsSpan());
            var request = new Pkcs10CertificationRequest(m_csr.ToArray());
            m_issued = CertificateBuilder.Create(request.Subject)
                .AddExtension(new X509SubjectAltNameExtension(kDeviceUri, ["localhost"]))
                .SetIssuer(m_registrar)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
        }

        /// <summary>
        /// Releases certificates and removes this test's checkpoint directory.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_issued?.Dispose();
            m_registrar?.Dispose();
            m_signingKey?.Dispose();
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// A lost response retains the request identifier and a later callback records that request's certificate.
        /// </summary>
        [Test]
        public async Task UnknownFinishOutcomeRetriesTheSameRequest()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            (DevicePullJournal journal, PullDcaSigningBinding binding) = await StartJournalAsync(files)
                .ConfigureAwait(false);
            ServiceResultException? timeout = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReceiveAsync(journal, binding, _ => throw new ServiceResultException(StatusCodes.BadTimeout))
                    .ConfigureAwait(false));
            Assert.That(timeout!.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            DevicePullJournalCheckpoint? receiving = await journal.ReadAsync().ConfigureAwait(false);
            Assert.That(receiving!.Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
            Assert.That(receiving.Binding.RequestId, Is.EqualTo(binding.RequestId));
            Assert.That(receiving.Binding.AttemptId, Is.EqualTo(binding.AttemptId));

            int calls = 0;
            PullDcaCertificateReceipt receipt = await ReceiveAsync(journal, binding, _ =>
            {
                calls++;
                return new ValueTask<PullDcaCertificateReceipt>(CreateReceipt());
            }).ConfigureAwait(false);
            PullDcaCertificateReceipt replay = await ReceiveAsync(journal, binding, _ =>
                throw new AssertionException("A retained receipt must not invoke FinishRequest again."))
                .ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(receipt.Certificate, Is.EqualTo(ByteString.From(m_issued.RawData.AsSpan())));
            Assert.That(replay.Certificate, Is.EqualTo(receipt.Certificate));
            Assert.That((await journal.ReadAsync().ConfigureAwait(false))!.Stage,
                Is.EqualTo(DevicePullJournalStage.CertificateReceived));
        }

        /// <summary>
        /// Journal instances sharing one writer run at most one FinishRequest callback at a time.
        /// </summary>
        [Test]
        public async Task ConcurrentReceiversShareOnePublishedReceipt()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            (DevicePullJournal firstJournal, PullDcaSigningBinding binding) = await StartJournalAsync(files)
                .ConfigureAwait(false);
            var secondJournal = CreateJournal(files);
            await secondJournal.ResolveAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.NamespaceUris)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;

            async ValueTask<PullDcaCertificateReceipt> Receive(CancellationToken ct)
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CreateReceipt();
            }

            Task<PullDcaCertificateReceipt> first = ReceiveAsync(firstJournal, binding, Receive).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Task<PullDcaCertificateReceipt> second = ReceiveAsync(secondJournal, binding, Receive).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                release.TrySetResult(true);
                PullDcaCertificateReceipt[] receipts = await Task.WhenAll(first, second).ConfigureAwait(false);

                Assert.That(calls, Is.EqualTo(1));
                Assert.That(receipts[0].Certificate, Is.EqualTo(receipts[1].Certificate));
                Assert.That((await firstJournal.ReadAsync().ConfigureAwait(false))!.Stage,
                    Is.EqualTo(DevicePullJournalStage.CertificateReceived));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// A queued receiver retains its selected profile even if the caller changes an opaque identifier.
        /// </summary>
        [Test]
        public async Task QueuedReceiverDetachesOpaqueProfileIdentifier()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            byte[] identifier = [1, 2, 3];
            var profile = new PullDcaCertificateProfile(
                new NodeId(ByteString.From(identifier), 1),
                Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            (DevicePullJournal firstJournal, PullDcaSigningBinding binding) = await StartJournalAsync(files, profile)
                .ConfigureAwait(false);
            var secondJournal = CreateJournal(files);
            await secondJournal.ResolveAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.NamespaceUris)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;

            async ValueTask<PullDcaCertificateReceipt> Receive(CancellationToken ct)
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CreateReceipt();
            }

            Task<PullDcaCertificateReceipt> first = ReceiveAsync(firstJournal, binding, Receive).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Task<PullDcaCertificateReceipt> second = ReceiveAsync(secondJournal, binding, Receive).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                identifier[0] = 9;
                release.TrySetResult(true);
                PullDcaCertificateReceipt[] receipts = await Task.WhenAll(first, second).ConfigureAwait(false);

                Assert.That(calls, Is.EqualTo(1));
                Assert.That(receipts[0].Certificate, Is.EqualTo(receipts[1].Certificate));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Reset preserves old Receiving evidence and rejects a callback after a new attempt is recorded.
        /// </summary>
        [Test]
        public async Task ResetRejectsAnInFlightReceiptAndPreservesItsEvidence()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            (DevicePullJournal journal, PullDcaSigningBinding binding) = await StartJournalAsync(files)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async ValueTask<PullDcaCertificateReceipt> Receive(CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CreateReceipt();
            }

            Task<PullDcaCertificateReceipt> pending = ReceiveAsync(journal, binding, Receive).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await journal.BeginResetAsync().ConfigureAwait(false);
                await journal.CompleteResetAsync().ConfigureAwait(false);
                using Certificate replacementKey = CertificateBuilder.Create("CN=Replacement request key")
                    .AddExtension(new X509SubjectAltNameExtension(kDeviceUri, ["localhost"]))
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                PullDcaSigningBinding replacement = binding with
                {
                    AttemptId = Guid.NewGuid(),
                    RequestId = NodeId.Null,
                    SigningRequest = ByteString.From(
                        DefaultCertificateFactory.Instance.CreateSigningRequest(replacementKey).AsSpan()),
                    SigningKeyCertificate = ByteString.From(replacementKey.RawData.AsSpan())
                };
                await journal.RecordDispatchAsync(replacement).ConfigureAwait(false);
                byte[]? active = await files.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false);
                release.TrySetResult(true);

                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await pending.ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(await files.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false),
                    Is.EqualTo(active));
                DevicePullJournalCheckpoint? current = await journal.ReadAsync().ConfigureAwait(false);
                Assert.That(current!.Stage, Is.EqualTo(DevicePullJournalStage.DispatchUnknown));
                Assert.That(current.Binding.AttemptId, Is.EqualTo(replacement.AttemptId));
                ArrayOf<DevicePullJournalCheckpoint> retired = await journal.ReadRetiredAsync().ConfigureAwait(false);
                Assert.That(retired.Count, Is.EqualTo(1));
                Assert.That(retired[0].Stage, Is.EqualTo(DevicePullJournalStage.Receiving));
                Assert.That(retired[0].Binding.AttemptId, Is.EqualTo(binding.AttemptId));
                Assert.That(retired[0].Binding.RequestId, Is.EqualTo(binding.RequestId));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Persists one request and its returned identifier before testing FinishRequest handling.
        /// </summary>
        private async ValueTask<(DevicePullJournal Journal, PullDcaSigningBinding Binding)> StartJournalAsync(
            PushRegistrarCheckpointFiles files,
            PullDcaCertificateProfile? profile = null)
        {
            var journal = CreateJournal(files);
            var registrar = new PullDcaRegistrar(
                new Uri("opc.tcp://registrar.example:4840/"), new NodeId(10u, 1), new NodeId(11u, 1));
            profile ??= new PullDcaCertificateProfile(
                new NodeId(12u, 1), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var binding = new PullDcaSigningBinding(
                registrar,
                new NodeId(13u, 1),
                NodeId.Null,
                profile,
                m_csr,
                ByteString.From(m_signingKey.RawData.AsSpan()),
                ByteString.From(m_signingKey.RawData.AsSpan()),
                ByteString.From(m_registrar.RawData.AsSpan()),
                kRegistrarUri,
                [Opc.Ua.Namespaces.OpcUa, "urn:test:registrar"],
                Guid.NewGuid());
            await journal.RecordDispatchAsync(binding).ConfigureAwait(false);
            binding = binding with { RequestId = new NodeId(14u, 1) };
            await journal.RecordStartedAsync(binding).ConfigureAwait(false);
            return (journal, binding);
        }

        /// <summary>
        /// Creates a journal bound to the fixture's device and registrar.
        /// </summary>
        private static DevicePullJournal CreateJournal(PushRegistrarCheckpointFiles files)
        {
            return new DevicePullJournal(files, kDeviceUri, kRegistrarUri, NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Invokes receipt handling using the retained request binding.
        /// </summary>
        private static ValueTask<PullDcaCertificateReceipt> ReceiveAsync(
            DevicePullJournal journal,
            PullDcaSigningBinding binding,
            Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive)
        {
            return journal.GetOrCreateAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile, receive);
        }

        /// <summary>
        /// Returns the certificate issued for the fixture's retained signing key.
        /// </summary>
        private PullDcaCertificateReceipt CreateReceipt()
        {
            return new PullDcaCertificateReceipt(
                ByteString.From(m_issued.RawData.AsSpan()),
                [ByteString.From(m_registrar.RawData.AsSpan())]);
        }

        /// <summary>The device application URI.</summary>
        private const string kDeviceUri = "urn:opcfoundation:tests:pull-device";

        /// <summary>The authenticated registrar application URI.</summary>
        private const string kRegistrarUri = "urn:opcfoundation:tests:pull-registrar";

        /// <summary>The journal's complete-value persistence key.</summary>
        private const string kJournalKey = "DevicePullSigning-" + kDeviceUri;

        /// <summary>The current test's isolated checkpoint directory.</summary>
        private string m_directory = string.Empty;

        /// <summary>The retained request's private signing key.</summary>
        private Certificate m_signingKey = null!;

        /// <summary>The registrar certificate used to issue the response.</summary>
        private Certificate m_registrar = null!;

        /// <summary>The certificate issued for the retained request key.</summary>
        private Certificate m_issued = null!;

        /// <summary>The request containing the retained signing public key.</summary>
        private ByteString m_csr;
    }
}
