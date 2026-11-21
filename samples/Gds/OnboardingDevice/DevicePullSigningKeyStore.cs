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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OnboardingRegistrar;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace OnboardingDevice
{
    /// <summary>
    /// Stages a fresh key in a dedicated store and binds it to the device's pull journal.
    /// </summary>
    internal sealed class DevicePullSigningKeyStore :
        IPullDcaCredentialProvider,
        IPullDcaSigningKeyEvidenceProvider,
        IDevicePullSigningRecovery,
        IProvisionableDevicePullKeyProvider,
        IDisposable
    {
        /// <summary>
        /// Borrows the local checkpoint writer and certificate services owned by the device host.
        /// </summary>
        public DevicePullSigningKeyStore(
            PushRegistrarCheckpointFiles files,
            DevicePullJournal journal,
            ICertificateManager certificates,
            ICertificateStoreResolver stores,
            string applicationUri,
            string storePath,
            string storeType,
            ITelemetryContext telemetry,
            ICertificatePasswordProvider? passwords = null)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_journal = journal ?? throw new ArgumentNullException(nameof(journal));
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            m_stores = stores ?? throw new ArgumentNullException(nameof(stores));
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute application URI is required.", nameof(applicationUri));
            }
            if (string.IsNullOrWhiteSpace(storePath) || string.IsNullOrWhiteSpace(storeType))
            {
                throw new ArgumentException("A dedicated signing key store is required.", nameof(storePath));
            }
            if (!string.Equals(storeType, CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The sample requires a directory signing key store.", nameof(storeType));
            }
            m_applicationUri = applicationUri;
            m_storePath = storePath;
            m_storeType = storeType;
            m_passwords = passwords;
            m_checkpointKey = "DevicePullPreparedKey-" + applicationUri;
            m_context = ServiceMessageContext.Create(telemetry ?? throw new ArgumentNullException(nameof(telemetry)));
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            applicationId = CopyNodeId(applicationId);
            profile = CopyProfile(profile);
            if (applicationId.IsNull || profile.CertificateGroupId.IsNull ||
                profile.CertificateTypeId != ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_active != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A signing key is already owned by an attempt.");
                }
                ByteString generation = await m_journal.RequireReservedGenerationAsync(cancellationToken)
                    .ConfigureAwait(false);
                byte[]? previous = await m_files.ReadAsync(m_checkpointKey, cancellationToken).ConfigureAwait(false);
                if (previous != null)
                {
                    StagedKey old = Decode(previous);
                    if (!old.Retired)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The staged signing key requires recovery.");
                    }
                    await DeleteKeyAsync(old.Certificate, cancellationToken).ConfigureAwait(false);
                }
                using CertificateEntry identity = m_certificates.AcquireApplicationCertificateByType(
                    profile.CertificateTypeId) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
                using Certificate key = CertificateBuilder.Create(identity.Certificate.SubjectName)
                    .AddExtension(new X509SubjectAltNameExtension(
                        m_applicationUri, X509Utils.GetDomainsFromCertificate(identity.Certificate).ToArray() ?? []))
                    .SetLifeTime(kSigningKeyLifeTimeMonths)
                    .SetRSAKeySize(kSigningKeySize)
                    .CreateForRSA();
                var staged = new StagedKey(
                    false,
                    ByteString.From(key.RawData.AsSpan()),
                    ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(key).AsSpan()),
                    generation);
                byte[] encoded = Encode(staged);
                if (!await m_files.CompareExchangeAsync(
                    m_checkpointKey, previous, encoded, cancellationToken).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The staged signing key changed during preparation.");
                }
                try
                {
                    using ICertificateStore store = OpenStore();
                    if (!store.SupportsLoadPrivateKey)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The signing store cannot reload private keys.");
                    }
                    char[] password = PasswordFor(key.Thumbprint);
                    try
                    {
                        await store.AddAsync(key, password, cancellationToken).ConfigureAwait(false);
                        using Certificate? persisted = await store.LoadPrivateKeyAsync(
                            key.Thumbprint, null, m_applicationUri, profile.CertificateTypeId, password,
                            cancellationToken).ConfigureAwait(false);
                        if (persisted == null || !persisted.HasPrivateKey ||
                            !persisted.RawData.AsSpan().SequenceEqual(key.RawData) ||
                            !X509Utils.VerifyKeyPair(key, persisted))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadSecurityChecksFailed, "The signing store did not retain the key.");
                        }
                    }
                    finally
                    {
                        Array.Clear(password, 0, password.Length);
                    }
                }
                catch (Exception error)
                {
                    try
                    {
                        await RetireAsync(encoded, staged, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupError)
                    {
                        throw new AggregateException(
                            "Signing key preparation and cleanup failed.", error, cleanupError);
                    }
                    throw;
                }
                m_active = new ActiveRequest(applicationId, profile, staged.Certificate, staged.SigningRequest);
                return CopyBytes(staged.SigningRequest);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> GetSigningKeyCertificateAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString signingRequest,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            applicationId = CopyNodeId(applicationId);
            profile = CopyProfile(profile);
            signingRequest = CopyBytes(signingRequest);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ActiveRequest active = m_active ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                if (active.ApplicationId != applicationId || active.Profile != profile ||
                    active.SigningRequest != signingRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                StagedKey staged = await RequireStagedAsync(cancellationToken).ConfigureAwait(false);
                RequireMatch(staged, active.Certificate, active.SigningRequest);
                using Certificate retained = await LoadKeyAsync(staged.Certificate, cancellationToken)
                    .ConfigureAwait(false);
                return CopyBytes(staged.Certificate);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Persists an immutable assignment baseline for the staged key and reserved dispatch generation.
        /// </summary>
        public async ValueTask RecordAssignmentsAsync(
            PullDcaSigningBinding binding,
            ArrayOf<NodeId> certificateTypes,
            ArrayOf<ByteString> certificates,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            binding = CopyBinding(binding);
            if (!binding.RequestId.IsNull || certificateTypes.IsNull || certificates.IsNull ||
                certificateTypes.Count != certificates.Count)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            var types = new NodeId[certificateTypes.Count];
            var copies = new ByteString[certificates.Count];
            for (int i = 0; i < types.Length; i++)
            {
                types[i] = CopyNodeId(certificateTypes[i]);
                copies[i] = CopyBytes(certificates[i]);
                if (types[i].IsNull || copies[i].IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                using Certificate assigned = Certificate.FromRawData(copies[i]);
                if (!assigned.RawData.AsSpan().SequenceEqual(copies[i].Span))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ActiveRequest active = m_active ?? throw new ServiceResultException(StatusCodes.BadInvalidState);
                if (active.ApplicationId != binding.ApplicationId || active.Profile != binding.Profile ||
                    active.Certificate != binding.SigningKeyCertificate ||
                    active.SigningRequest != binding.SigningRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                StagedKey staged = await RequireStagedAsync(cancellationToken).ConfigureAwait(false);
                RequireMatch(staged, binding.SigningKeyCertificate, binding.SigningRequest);
                ByteString generation = await m_journal.RequireReservedGenerationAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (generation != staged.Generation)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                using Certificate retained = await LoadKeyAsync(staged.Certificate, cancellationToken)
                    .ConfigureAwait(false);
                var baseline = new AssignmentBaseline(
                    m_journal.DigestDispatchBinding(binding), binding.NamespaceUris, types, copies);
                byte[] encoded = EncodeAssignments(baseline);
                string key = GetAssignmentKey(binding.AttemptId);
                if (!await m_files.CompareExchangeAsync(key, null, encoded, cancellationToken).ConfigureAwait(false))
                {
                    byte[]? existing = await m_files.ReadAsync(key, cancellationToken).ConfigureAwait(false);
                    if (existing == null || !existing.AsSpan().SequenceEqual(encoded))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The attempt already has a different assignment baseline.");
                    }
                }
                if (await m_journal.RequireReservedGenerationAsync(cancellationToken).ConfigureAwait(false) !=
                    staged.Generation)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Validates a recovered assignment against the original baseline, retained key, and received trust.
        /// </summary>
        public async ValueTask ValidateAssignedCertificateAsync(
            PullDcaSigningBinding originalBinding,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (originalBinding == null)
            {
                throw new ArgumentNullException(nameof(originalBinding));
            }
            originalBinding = CopyBinding(originalBinding);
            credentials = DevicePullJournal.SnapshotCredentials(credentials);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DevicePullJournalCheckpoint checkpoint = await RequireAttemptAsync(
                    originalBinding.AttemptId, cancellationToken).ConfigureAwait(false);
                if (checkpoint.Stage != DevicePullJournalStage.Receiving ||
                    checkpoint.Binding.RequestId != originalBinding.RequestId)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                _ = await m_journal.GetReceivedAsync(originalBinding, cancellationToken).ConfigureAwait(false);
                byte[] encoded = await m_files.ReadAsync(
                    GetAssignmentKey(originalBinding.AttemptId), cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The attempt has no pre-request assignment baseline.");
                AssignmentBaseline baseline = DecodeAssignments(encoded);
                DevicePullJournalCheckpoint originalNamespaces = await m_journal.ReadForSessionAsync(
                    baseline.NamespaceUris, cancellationToken).ConfigureAwait(false);
                if (originalNamespaces.Stage != DevicePullJournalStage.Receiving ||
                    originalNamespaces.Binding.AttemptId != originalBinding.AttemptId ||
                    baseline.BindingDigest != m_journal.DigestDispatchBinding(originalNamespaces.Binding))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                for (int i = 0; i < baseline.Certificates.Count; i++)
                {
                    if (baseline.Certificates[i] == credentials.Certificate)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed, "The certificate was assigned before this request.");
                    }
                }
                StagedKey staged = await RequireStagedAsync(cancellationToken).ConfigureAwait(false);
                RequireMatch(staged, originalBinding.SigningKeyCertificate, originalBinding.SigningRequest);
                using Certificate retained = await LoadKeyAsync(staged.Certificate, cancellationToken)
                    .ConfigureAwait(false);
                if (credentials.Certificate.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                using Certificate issued = Certificate.FromRawData(credentials.Certificate);
                if (!issued.RawData.AsSpan().SequenceEqual(credentials.Certificate.Span))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                DateTime now = DateTime.UtcNow;
                if (now < issued.NotBefore.ToUniversalTime() || now > issued.NotAfter.ToUniversalTime())
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateTimeInvalid);
                }
                if (!X509Utils.GetApplicationUrisFromCertificate(issued).Contains(m_applicationUri))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
                }
                if (!CertificateIdentifier.ValidateCertificateType(
                    issued, originalBinding.Profile.CertificateTypeId) ||
                    !X509Utils.VerifyKeyPair(issued, retained))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                await ValidateTrustAsync(issued, credentials, cancellationToken).ConfigureAwait(false);
                _ = await m_journal.GetReceivedAsync(originalBinding, cancellationToken).ConfigureAwait(false);
                checkpoint = await RequireAttemptAsync(originalBinding.AttemptId, cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint.Stage != DevicePullJournalStage.Receiving)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask RestoreSigningRequestAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            binding = CopyBinding(binding);
            NodeId applicationId = binding.ApplicationId;
            PullDcaCertificateProfile profile = binding.Profile;
            ByteString certificate = binding.SigningKeyCertificate;
            ByteString signingRequest = binding.SigningRequest;
            Guid attemptId = binding.AttemptId;
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _ = await m_journal.GetReceivedAsync(binding, cancellationToken).ConfigureAwait(false);
                DevicePullJournalCheckpoint checkpoint = await RequireAttemptAsync(attemptId, cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint.Stage == DevicePullJournalStage.Installed ||
                    checkpoint.Binding.SigningKeyCertificate != certificate ||
                    checkpoint.Binding.SigningRequest != signingRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                StagedKey staged = await RequireStagedAsync(cancellationToken).ConfigureAwait(false);
                RequireMatch(staged, certificate, signingRequest);
                using Certificate retained = await LoadKeyAsync(certificate, cancellationToken).ConfigureAwait(false);
                m_active = new ActiveRequest(applicationId, profile, certificate, signingRequest);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<Certificate?> GetSigningKeyAsync(
            Guid attemptId,
            ByteString issuedCertificate,
            CancellationToken cancellationToken = default)
        {
            issuedCertificate = CopyBytes(issuedCertificate);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DevicePullJournalCheckpoint checkpoint = await RequireAttemptAsync(attemptId, cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint.Stage != DevicePullJournalStage.Received || checkpoint.Credentials == null ||
                    checkpoint.Credentials.Certificate != issuedCertificate)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                StagedKey staged = await RequireStagedAsync(cancellationToken).ConfigureAwait(false);
                RequireMatch(staged, checkpoint.Binding.SigningKeyCertificate, checkpoint.Binding.SigningRequest);
                using Certificate issued = Certificate.FromRawData(issuedCertificate);
                Certificate retained = await LoadKeyAsync(staged.Certificate, cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!X509Utils.VerifyKeyPair(issued, retained))
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                    return retained;
                }
                catch
                {
                    retained.Dispose();
                    throw;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask InstallAsync(
            NodeId applicationId,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "Only the device security owner can install pull credentials.");
        }

        /// <summary>
        /// Retires an unreferenced staged key after a restart without dispatching its CSR.
        /// </summary>
        public async ValueTask ReconcileStagedAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[]? bytes = await m_files.ReadAsync(m_checkpointKey, cancellationToken).ConfigureAwait(false);
                if (bytes == null)
                {
                    return;
                }
                StagedKey staged = Decode(bytes);
                DevicePullJournalCheckpoint? checkpoint = await m_journal.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint == null)
                {
                    await RetireAsync(bytes, staged, cancellationToken).ConfigureAwait(false);
                }
                else if (checkpoint.Stage == DevicePullJournalStage.Installed && staged.Retired)
                {
                    RequireSameMaterial(
                        staged, checkpoint.Binding.SigningKeyCertificate, checkpoint.Binding.SigningRequest);
                    await DeleteKeyAsync(staged.Certificate, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    RequireMatch(staged, checkpoint.Binding.SigningKeyCertificate, checkpoint.Binding.SigningRequest);
                    using Certificate retained = await LoadKeyAsync(staged.Certificate, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Removes a staged key only after the security owner has recorded installation.
        /// </summary>
        public async ValueTask RetireInstalledAsync(Guid attemptId, CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DevicePullJournalCheckpoint checkpoint = await RequireAttemptAsync(attemptId, cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint.Stage != DevicePullJournalStage.Installed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                byte[] bytes = await m_files.ReadAsync(m_checkpointKey, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                StagedKey staged = Decode(bytes);
                RequireSameMaterial(
                    staged, checkpoint.Binding.SigningKeyCertificate, checkpoint.Binding.SigningRequest);
                await RetireAsync(bytes, staged, cancellationToken).ConfigureAwait(false);
                m_active = null;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Removes staged key material after the journal has durably invalidated the previous attempt.
        /// </summary>
        public async ValueTask RetireResetAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[]? bytes = await m_files.ReadAsync(m_checkpointKey, cancellationToken).ConfigureAwait(false);
                if (bytes != null)
                {
                    StagedKey staged = Decode(bytes);
                    if (await m_journal.HasCompletedResetAfterAsync(staged.Generation, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        await RetireAsync(bytes, staged, cancellationToken).ConfigureAwait(false);
                        m_active = null;
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Retires this process's prepared key if its attempt ended before recording a dispatch.
        /// </summary>
        public async ValueTask RetireUndispatchedAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_active == null)
                {
                    return;
                }
                byte[] bytes = await m_files.ReadAsync(m_checkpointKey, cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                StagedKey staged = Decode(bytes);
                RequireSameMaterial(staged, m_active.Certificate, m_active.SigningRequest);
                DevicePullJournalCheckpoint? checkpoint = await m_journal.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint == null)
                {
                    await RetireAsync(bytes, staged, cancellationToken).ConfigureAwait(false);
                    m_active = null;
                }
                else
                {
                    RequireMatch(staged, checkpoint.Binding.SigningKeyCertificate, checkpoint.Binding.SigningRequest);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the private operation gate after all device operations have stopped.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Reads an active attempt by its durable identifier.
        /// </summary>
        private async ValueTask<DevicePullJournalCheckpoint> RequireAttemptAsync(Guid attemptId, CancellationToken ct)
        {
            DevicePullJournalCheckpoint? checkpoint = await m_journal.ReadAsync(ct).ConfigureAwait(false);
            if (attemptId == Guid.Empty || checkpoint == null || checkpoint.Binding.AttemptId != attemptId)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            return checkpoint;
        }

        /// <summary>
        /// Reads the prepared key without accepting a retired marker.
        /// </summary>
        private async ValueTask<StagedKey> RequireStagedAsync(CancellationToken ct)
        {
            byte[] bytes = await m_files.ReadAsync(m_checkpointKey, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            StagedKey staged = Decode(bytes);
            if (staged.Retired)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            return staged;
        }

        /// <summary>
        /// Requires the staged key and CSR to match the durable attempt.
        /// </summary>
        private static void RequireMatch(StagedKey staged, ByteString certificate, ByteString signingRequest)
        {
            if (staged.Retired)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            RequireSameMaterial(staged, certificate, signingRequest);
        }

        /// <summary>
        /// Compares the complete key certificate and CSR without changing the staged marker.
        /// </summary>
        private static void RequireSameMaterial(StagedKey staged, ByteString certificate, ByteString signingRequest)
        {
            if (staged.Certificate != certificate || staged.SigningRequest != signingRequest)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The staged key belongs to a different signing request.");
            }
        }

        /// <summary>
        /// Loads an independently owned private-key handle from the dedicated signing store.
        /// </summary>
        private async ValueTask<Certificate> LoadKeyAsync(ByteString certificate, CancellationToken ct)
        {
            using Certificate expected = Certificate.FromRawData(certificate);
            using ICertificateStore store = OpenStore();
            if (!store.SupportsLoadPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            char[] password = PasswordFor(expected.Thumbprint);
            Certificate? retained;
            try
            {
                retained = await store.LoadPrivateKeyAsync(
                    expected.Thumbprint, null, m_applicationUri, ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    password, ct).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }
            try
            {
                if (retained == null || !retained.HasPrivateKey ||
                    !retained.RawData.AsSpan().SequenceEqual(certificate.Span) ||
                    !X509Utils.VerifyKeyPair(expected, retained))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed, "The retained signing key is missing or different.");
                }
                return retained;
            }
            catch
            {
                retained?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Publishes retirement before deleting the private key, and retries either step after interruption.
        /// </summary>
        private async ValueTask RetireAsync(byte[] original, StagedKey staged, CancellationToken ct)
        {
            if (!staged.Retired)
            {
                byte[] retired = Encode(staged with { Retired = true });
                if (!await m_files.CompareExchangeAsync(
                    m_checkpointKey, original, retired, ct).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The staged signing key changed during retirement.");
                }
            }
            await DeleteKeyAsync(staged.Certificate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes only the key named by the staged public certificate.
        /// </summary>
        private async ValueTask DeleteKeyAsync(ByteString certificate, CancellationToken ct)
        {
            using Certificate expected = Certificate.FromRawData(certificate);
            using ICertificateStore store = OpenStore();
            using CertificateCollection initial = await store.FindByThumbprintAsync(expected.Thumbprint, ct)
                .ConfigureAwait(false);
            if (initial.Count == 0)
            {
                // A directory store can persist its PFX before publishing the public certificate index.
                await store.AddAsync(expected, null, ct).ConfigureAwait(false);
            }
            using CertificateCollection found = await store.FindByThumbprintAsync(expected.Thumbprint, ct)
                .ConfigureAwait(false);
            if (found.Count != 1 || !found[0].RawData.AsSpan().SequenceEqual(certificate.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The signing-store entry differs from its retained evidence.");
            }
            if (!await store.DeleteAsync(expected.Thumbprint, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The retired signing key could not be removed.");
            }
        }

        /// <summary>
        /// Opens the dedicated key store without borrowing the application certificate store.
        /// </summary>
        private ICertificateStore OpenStore()
        {
            return m_stores.OpenCertificateStore(m_storePath, m_storeType, noPrivateKeys: false);
        }

        /// <summary>
        /// Obtains the configured password for one signing certificate.
        /// </summary>
        private char[] PasswordFor(string thumbprint)
        {
            var identifier = new CertificateIdentifier
            {
                StorePath = m_storePath,
                StoreType = m_storeType,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = thumbprint
            };
            return m_passwords?.GetPassword(identifier) ?? [];
        }

        /// <summary>
        /// Validates the recovered chain against only the complete trust list read from the registrar.
        /// </summary>
        private async ValueTask ValidateTrustAsync(
            Certificate issued,
            PullDcaCredentials credentials,
            CancellationToken ct)
        {
            TrustListDataType trust = credentials.TrustList;
            if (!credentials.IssuerCertificates.Span.SequenceEqual(trust.IssuerCertificates.Span))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            using var chain = new CertificateCollection { issued };
            for (int i = 0; i < credentials.IssuerCertificates.Count; i++)
            {
                ByteString bytes = credentials.IssuerCertificates[i];
                if (bytes.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                using Certificate issuer = Certificate.FromRawData(bytes);
                if (!issuer.RawData.AsSpan().SequenceEqual(bytes.Span))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                chain.Add(issuer);
            }
            Opc.Ua.CertificateValidationResult result = await m_certificates.ValidateAsync(
                chain,
                TrustListIdentifier.Peers,
                new ValidationOptions
                {
                    TrustScope = new CertificateValidationScope(
                        trust.TrustedCertificates,
                        trust.IssuerCertificates,
                        (trust.TrustedCrls.ToArray() ?? []).Concat(trust.IssuerCrls.ToArray() ?? []).ToArrayOf()),
                    RejectUnknownRevocationStatus = true,
                    RejectSHA1SignedCertificates = true,
                    MinimumCertificateKeySize = 2048,
                    AutoAcceptUntrustedCertificates = false,
                    AcceptError = static (_, _) => false,
                    RecordRejectedCertificates = false
                },
                ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                throw new ServiceResultException(result.StatusCode);
            }
        }

        /// <summary>
        /// Names an immutable public assignment checkpoint by device and local attempt.
        /// </summary>
        private string GetAssignmentKey(Guid attemptId)
        {
            return "DevicePullAssignments-" + m_applicationUri + "-" + attemptId.ToString("N");
        }

        /// <summary>
        /// Encodes the bound assignment arrays without retaining private key material.
        /// </summary>
        private byte[] EncodeAssignments(AssignmentBaseline baseline)
        {
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, kAssignmentVersion);
                encoder.WriteString(null, m_applicationUri);
                encoder.WriteByteString(null, baseline.BindingDigest);
                encoder.WriteStringArray(null, baseline.NamespaceUris);
                encoder.WriteNodeIdArray(null, baseline.CertificateTypes);
                encoder.WriteByteStringArray(null, baseline.Certificates);
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Requires a complete assignment checkpoint with valid DER certificates and parallel arrays.
        /// </summary>
        private AssignmentBaseline DecodeAssignments(byte[] encoded)
        {
            try
            {
                using var buffer = new MemoryStream(encoded, writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                if (decoder.ReadUInt32(null) != kAssignmentVersion || decoder.ReadString(null) != m_applicationUri)
                {
                    throw new InvalidDataException("The assignment baseline belongs to another device or version.");
                }
                ByteString bindingDigest = decoder.ReadByteString(null);
                ArrayOf<string> namespaceUris = CopyStringArray(decoder.ReadStringArray(null));
                var baseline = new AssignmentBaseline(
                    bindingDigest, namespaceUris, decoder.ReadNodeIdArray(null), decoder.ReadByteStringArray(null));
                if (baseline.BindingDigest.Length != 32 || baseline.NamespaceUris.IsNull ||
                    baseline.NamespaceUris.Count == 0 || baseline.CertificateTypes.IsNull ||
                    baseline.Certificates.IsNull || baseline.CertificateTypes.Count != baseline.Certificates.Count ||
                    decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The assignment baseline is incomplete.");
                }
                for (int i = 0; i < baseline.CertificateTypes.Count; i++)
                {
                    ByteString bytes = baseline.Certificates[i];
                    if (baseline.CertificateTypes[i].IsNull || bytes.IsEmpty)
                    {
                        throw new InvalidDataException("The assignment baseline contains an incomplete entry.");
                    }
                    using Certificate certificate = Certificate.FromRawData(bytes);
                    if (!certificate.RawData.AsSpan().SequenceEqual(bytes.Span))
                    {
                        throw new InvalidDataException("The assignment baseline contains invalid DER.");
                    }
                }
                return baseline;
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or
                ArgumentException or FormatException or CryptographicException)
            {
                throw new InvalidDataException("The assignment baseline is invalid.", error);
            }
        }

        /// <summary>
        /// Copies decoded namespace URIs into the assignment's non-null representation.
        /// </summary>
        private static ArrayOf<string> CopyStringArray(ArrayOf<string?> values)
        {
            if (values.IsNull)
            {
                return default;
            }
            var copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i] ?? throw new InvalidDataException("An assignment namespace URI is null.");
            }
            return copy.ToArrayOf();
        }

        /// <summary>
        /// Encodes only public request evidence in the checkpoint directory.
        /// </summary>
        private byte[] Encode(StagedKey staged)
        {
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, kVersion);
                encoder.WriteString(null, m_applicationUri);
                encoder.WriteBoolean(null, staged.Retired);
                encoder.WriteByteString(null, staged.Certificate);
                encoder.WriteByteString(null, staged.SigningRequest);
                encoder.WriteByteString(null, staged.Generation);
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Reads a bounded public checkpoint for this device.
        /// </summary>
        private StagedKey Decode(byte[] encoded)
        {
            try
            {
                using var buffer = new MemoryStream(encoded, writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                if (decoder.ReadUInt32(null) != kVersion || decoder.ReadString(null) != m_applicationUri)
                {
                    throw new InvalidDataException("The staged key belongs to another device or version.");
                }
                var staged = new StagedKey(
                    decoder.ReadBoolean(null), decoder.ReadByteString(null), decoder.ReadByteString(null),
                    decoder.ReadByteString(null));
                if (staged.Certificate.IsEmpty || staged.SigningRequest.IsEmpty ||
                    (!staged.Generation.IsNull && staged.Generation.Length != 32) ||
                    decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The staged key checkpoint is invalid.");
                }
                return staged;
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or
                ArgumentException or FormatException)
            {
                throw new InvalidDataException("The staged key checkpoint is invalid.", error);
            }
        }

        /// <summary>
        /// Copies an opaque node identifier before an asynchronous operation.
        /// </summary>
        internal static NodeId CopyNodeId(NodeId id)
        {
            return id.TryGetValue(out ByteString bytes)
                ? new NodeId(CopyBytes(bytes), id.NamespaceIndex)
                : id;
        }

        /// <summary>
        /// Copies both identifiers in a certificate profile.
        /// </summary>
        internal static PullDcaCertificateProfile CopyProfile(PullDcaCertificateProfile profile)
        {
            return profile with
            {
                CertificateGroupId = CopyNodeId(profile.CertificateGroupId),
                CertificateTypeId = CopyNodeId(profile.CertificateTypeId)
            };
        }

        /// <summary>
        /// Detaches all request and channel evidence before comparing it with the journal.
        /// </summary>
        private static PullDcaSigningBinding CopyBinding(PullDcaSigningBinding binding)
        {
            if (binding.Registrar == null || binding.Profile == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            return binding with
            {
                Registrar = binding.Registrar with
                {
                    RegistrarId = CopyNodeId(binding.Registrar.RegistrarId),
                    CertificateDirectoryId = CopyNodeId(binding.Registrar.CertificateDirectoryId)
                },
                ApplicationId = CopyNodeId(binding.ApplicationId),
                RequestId = CopyNodeId(binding.RequestId),
                Profile = CopyProfile(binding.Profile),
                SigningRequest = CopyBytes(binding.SigningRequest),
                SigningKeyCertificate = CopyBytes(binding.SigningKeyCertificate),
                ClientChannelCertificate = CopyBytes(binding.ClientChannelCertificate),
                RegistrarChannelCertificate = CopyBytes(binding.RegistrarChannelCertificate),
                NamespaceUris = binding.NamespaceUris.IsNull
                    ? default(ArrayOf<string>)
                    : binding.NamespaceUris.Span.ToArray()
            };
        }

        /// <summary>
        /// Copies a byte string while retaining its null value.
        /// </summary>
        private static ByteString CopyBytes(ByteString value)
        {
            return value.IsNull ? default : ByteString.From(value.Span);
        }

        /// <summary>The public evidence for one staged signing key.</summary>
        private sealed record StagedKey(
            bool Retired,
            ByteString Certificate,
            ByteString SigningRequest,
            ByteString Generation);

        /// <summary>The original assignment arrays and the digest of their dispatch binding.</summary>
        private sealed record AssignmentBaseline(
            ByteString BindingDigest,
            ArrayOf<string> NamespaceUris,
            ArrayOf<NodeId> CertificateTypes,
            ArrayOf<ByteString> Certificates);

        /// <summary>The in-process binding of a prepared request to its caller.</summary>
        private sealed record ActiveRequest(
            NodeId ApplicationId,
            PullDcaCertificateProfile Profile,
            ByteString Certificate,
            ByteString SigningRequest);

        /// <summary>The checkpoint format version.</summary>
        private const uint kVersion = 1;

        /// <summary>The assignment-baseline checkpoint format version.</summary>
        private const uint kAssignmentVersion = 1;

        /// <summary>The generated RSA key size.</summary>
        private const ushort kSigningKeySize = 2048;

        /// <summary>The staged key certificate lifetime in months.</summary>
        private const ushort kSigningKeyLifeTimeMonths = 12;

        /// <summary>The single-writer public checkpoint backend.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;

        /// <summary>The device's pull request journal.</summary>
        private readonly DevicePullJournal m_journal;

        /// <summary>The active device certificate manager.</summary>
        private readonly ICertificateManager m_certificates;

        /// <summary>The host's certificate-store resolver.</summary>
        private readonly ICertificateStoreResolver m_stores;

        /// <summary>The device application URI.</summary>
        private readonly string m_applicationUri;

        /// <summary>The dedicated key store's path.</summary>
        private readonly string m_storePath;

        /// <summary>The dedicated key store's type.</summary>
        private readonly string m_storeType;

        /// <summary>The optional host-owned certificate password provider.</summary>
        private readonly ICertificatePasswordProvider? m_passwords;

        /// <summary>The staged public checkpoint key.</summary>
        private readonly string m_checkpointKey;

        /// <summary>The standard checkpoint encoding context.</summary>
        private readonly ServiceMessageContext m_context;

        /// <summary>Serializes key preparation, retention, and retirement.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>The request prepared by this process, if any.</summary>
        private ActiveRequest? m_active;

        /// <summary>Whether the host has released this provider.</summary>
        private int m_disposed;
    }
}
