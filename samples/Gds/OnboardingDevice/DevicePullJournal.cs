// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OnboardingRegistrar;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

namespace OnboardingDevice
{
    /// <summary>
    /// Stages retained by the local pull signing journal.
    /// </summary>
    internal enum DevicePullJournalStage
    {
        /// <summary>The StartSigningRequest outcome is not known.</summary>
        DispatchUnknown = 1,

        /// <summary>The registrar returned the request identifier.</summary>
        Started = 2,

        /// <summary>The FinishRequest outcome is not known.</summary>
        Receiving = 3,

        /// <summary>The issued certificate and issuers are retained.</summary>
        CertificateReceived = 4,

        /// <summary>The issued certificate, issuers, and complete trust list are retained.</summary>
        Received = 5,

        /// <summary>The device security owner completed installation.</summary>
        Installed = 6,

        /// <summary>A device security reset is in progress.</summary>
        Resetting = 7,

        /// <summary>The retained request was invalidated by a completed reset.</summary>
        Retired = 8
    }

    /// <summary>
    /// Detached public evidence for one pull request.
    /// </summary>
    internal sealed record DevicePullJournalCheckpoint(
        DevicePullJournalStage Stage,
        PullDcaSigningBinding Binding,
        PullDcaCertificateReceipt? Receipt,
        PullDcaCredentials? Credentials,
        ArrayOf<string> RetiredAttempts = default);

    /// <summary>
    /// Retains one device request in the sample's exclusively leased checkpoint directory.
    /// The directory lease coordinates processes using that directory, not separate hosts or directories.
    /// Private keys remain in their certificate stores.
    /// </summary>
    internal sealed class DevicePullJournal :
        IPullDcaSigningJournal,
        IPullDcaStartDispatchObserver,
        IPullDcaCertificateReceiptStore
    {
        /// <summary>
        /// Borrows the checkpoint writer and binds this journal to the device and selected registrar.
        /// </summary>
        public DevicePullJournal(
            PushRegistrarCheckpointFiles files,
            string applicationUri,
            string? registrarApplicationUri,
            ITelemetryContext telemetry)
            : this(files, applicationUri, registrarApplicationUri, telemetry, null)
        {
        }

        /// <summary>
        /// Borrows the checkpoint writer and a validator for explicitly configured registrar candidates.
        /// </summary>
        public DevicePullJournal(
            PushRegistrarCheckpointFiles files,
            string applicationUri,
            string? registrarApplicationUri,
            ITelemetryContext telemetry,
            Func<PullDcaRegistrar, string, CancellationToken, ValueTask>? requireConfiguredRegistrar)
            : this(files, applicationUri, registrarApplicationUri, telemetry, requireConfiguredRegistrar, false)
        {
        }

        /// <summary>
        /// Optionally requires each dispatch to retain the reset generation selected by its owning attempt.
        /// </summary>
        public DevicePullJournal(
            PushRegistrarCheckpointFiles files,
            string applicationUri,
            string? registrarApplicationUri,
            ITelemetryContext telemetry,
            Func<PullDcaRegistrar, string, CancellationToken, ValueTask>? requireConfiguredRegistrar,
            bool requireDispatchReservation)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute device application URI is required.", nameof(applicationUri));
            }
            if (registrarApplicationUri != null &&
                !Uri.TryCreate(registrarApplicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute registrar application URI is required.", nameof(registrarApplicationUri));
            }
            m_applicationUri = applicationUri;
            m_registrarApplicationUri = registrarApplicationUri;
            m_requireConfiguredRegistrar = requireConfiguredRegistrar;
            m_requireDispatchReservation = requireDispatchReservation;
            m_receiveGate = s_receiveGates.GetValue(files, static _ => new SemaphoreSlim(1, 1));
            m_key = "DevicePullSigning-" + applicationUri;
            m_context = ServiceMessageContext.Create(telemetry ?? throw new ArgumentNullException(nameof(telemetry)));
        }

        /// <summary>
        /// Reserves the current empty or retired journal generation for one session-owned outbound attempt.
        /// </summary>
        public async ValueTask<IDisposable> ReserveDispatchAsync(CancellationToken cancellationToken = default)
        {
            byte[]? original = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
            ResetMarker? reset = original == null ? null : DecodeResetMarker(original);
            if (original != null && reset?.Stage != DevicePullJournalStage.Retired)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A pull signing request or reset already requires recovery.");
            }
            lock (m_dispatchGate)
            {
                if (m_resetInProgress || m_dispatchReservation != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A pull attempt or reset already owns the dispatch generation.");
                }
                var reservation = new DispatchReservation(this, original);
                m_dispatchReservation = reservation;
                return reservation;
            }
        }

        /// <summary>
        /// Copies decoded strings into the journal's non-null representation.
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
                copy[i] = values[i] ?? throw new InvalidDataException("A pull checkpoint string is null.");
            }
            return copy.ToArrayOf();
        }

        /// <summary>
        /// Returns the reserved reset generation only if it still matches the journal.
        /// </summary>
        public async ValueTask<ByteString> RequireReservedGenerationAsync(
            CancellationToken cancellationToken = default)
        {
            DispatchReservation reservation;
            lock (m_dispatchGate)
            {
                reservation = m_dispatchReservation ??
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                if (reservation.Revoked || reservation.Released)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The pull attempt no longer owns its dispatch generation.");
                }
            }
            byte[]? current = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
            bool stillOwned;
            lock (m_dispatchGate)
            {
                stillOwned = ReferenceEquals(m_dispatchReservation, reservation) &&
                    !reservation.Revoked && !reservation.Released;
            }
            if (!stillOwned || !SameCheckpoint(reservation.Expected, current))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The pull attempt predates a device security transition.");
            }
            return Generation(reservation.Expected);
        }

        /// <summary>
        /// Reports whether a completed reset replaced the generation retained by a staged key.
        /// </summary>
        public async ValueTask<bool> HasCompletedResetAfterAsync(
            ByteString generation,
            CancellationToken cancellationToken = default)
        {
            generation = CopyByteString(generation);
            byte[]? current = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return false;
            }
            ResetMarker? reset = DecodeResetMarker(current);
            return reset?.Stage == DevicePullJournalStage.Retired && Generation(current) != generation;
        }

        /// <summary>
        /// Returns a detached checkpoint using the namespace table retained with the request.
        /// </summary>
        public async ValueTask<DevicePullJournalCheckpoint?> ReadAsync(CancellationToken cancellationToken = default)
        {
            byte[]? bytes = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
            if (bytes == null)
            {
                return null;
            }
            ResetMarker? reset = DecodeResetMarker(bytes);
            if (reset?.Stage == DevicePullJournalStage.Resetting)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The device security reset is pending.");
            }
            return reset == null ? Decode(bytes, default) : null;
        }

        /// <summary>
        /// Reads the public evidence retained for attempts invalidated by security resets.
        /// </summary>
        public async ValueTask<ArrayOf<DevicePullJournalCheckpoint>> ReadRetiredAsync(
            CancellationToken cancellationToken = default)
        {
            byte[]? bytes = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
            if (bytes == null)
            {
                return [];
            }
            ArrayOf<string> retired = DecodeResetMarker(bytes)?.RetiredAttempts ??
                Decode(bytes, default).RetiredAttempts;
            var checkpoints = new List<DevicePullJournalCheckpoint>();
            for (int i = 0; i < retired.Count; i++)
            {
                byte[] evidence = await m_files.ReadAsync(retired[i], cancellationToken).ConfigureAwait(false) ??
                    throw new InvalidDataException("A retired pull signing checkpoint is missing.");
                if (!string.Equals(retired[i], GetRetiredKey(evidence), StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A retired pull signing checkpoint does not match its digest.");
                }
                checkpoints.Add(Decode(evidence, default));
            }
            return checkpoints.ToArrayOf();
        }

        /// <summary>
        /// Resolves retained identifiers using the namespace table of the actual selected session.
        /// </summary>
        public async ValueTask<DevicePullJournalCheckpoint> ReadForSessionAsync(
            ArrayOf<string> namespaceUris,
            CancellationToken cancellationToken = default)
        {
            if (namespaceUris.IsNull || namespaceUris.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The selected session namespace table is required.");
            }
            byte[] bytes = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            return Decode(bytes, namespaceUris);
        }

        /// <summary>
        /// Hashes the original dispatch binding independently of the later request identifier and journal stage.
        /// </summary>
        internal ByteString DigestDispatchBinding(PullDcaSigningBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            PullDcaSigningBinding dispatch = SnapshotBinding(binding with { RequestId = NodeId.Null });
            RequireBinding(dispatch, requireRequest: false);
            byte[] encoded = Encode(new DevicePullJournalCheckpoint(
                DevicePullJournalStage.DispatchUnknown, dispatch, null, null, []));
            return ByteString.From(SHA256.HashData(encoded).AsSpan());
        }

        /// <inheritdoc/>
        public async ValueTask RecordDispatchAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default)
        {
            RequireBinding(binding, requireRequest: false);
            binding = SnapshotBinding(binding);
            DispatchReservation? reservation;
            lock (m_dispatchGate)
            {
                reservation = m_dispatchReservation;
                if (m_resetInProgress || (m_requireDispatchReservation && reservation == null))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The pull attempt has not reserved a dispatch generation.");
                }
                if (reservation != null)
                {
                    if (reservation.Revoked || reservation.Released || reservation.DispatchCompletion != null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The pull attempt cannot start another dispatch.");
                    }
                    reservation.Start(binding.AttemptId);
                }
            }
            try
            {
                await RequireConfiguredRegistrarAsync(binding, cancellationToken).ConfigureAwait(false);
                byte[]? original = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
                bool stillOwned;
                lock (m_dispatchGate)
                {
                    stillOwned = reservation == null ||
                        (ReferenceEquals(m_dispatchReservation, reservation) &&
                         !reservation.Revoked && !reservation.Released);
                }
                if (!stillOwned || (reservation != null && !SameCheckpoint(reservation.Expected, original)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The pull attempt predates a device security transition.");
                }
                ResetMarker? reset = original == null ? null : DecodeResetMarker(original);
                if (original != null && reset?.Stage != DevicePullJournalStage.Retired)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A pull signing request already requires recovery.");
                }
                byte[] encoded = Encode(new(
                    DevicePullJournalStage.DispatchUnknown, binding, null, null, reset?.RetiredAttempts ?? []));
                if (!await m_files.CompareExchangeAsync(m_key, original, encoded, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A pull signing request already requires recovery.");
                }
            }
            catch
            {
                CompleteDispatch(binding.AttemptId);
                throw;
            }
        }

        /// <inheritdoc/>
        public ValueTask OnStartCompletedAsync(Guid attemptId)
        {
            if (attemptId == Guid.Empty)
            {
                throw new ArgumentException("The pull attempt ID is required.", nameof(attemptId));
            }
            CompleteDispatch(attemptId);
            return default;
        }

        /// <summary>
        /// Completes a recorded local dispatch without changing its durable checkpoint.
        /// </summary>
        private void CompleteDispatch(Guid attemptId)
        {
            lock (m_dispatchGate)
            {
                DispatchReservation? reservation = m_dispatchReservation;
                if (reservation == null || reservation.DispatchAttempt != attemptId)
                {
                    return;
                }
                reservation.Complete();
                if (reservation.Released)
                {
                    m_dispatchReservation = null;
                }
            }
        }

        /// <summary>
        /// Compares the complete reset generation, including an absent initial checkpoint.
        /// </summary>
        private static bool SameCheckpoint(byte[]? expected, byte[]? current)
        {
            return expected == null ? current == null : current != null && expected.AsSpan().SequenceEqual(current);
        }

        /// <summary>
        /// Represents an absent checkpoint separately from the digest of a complete checkpoint.
        /// </summary>
        private static ByteString Generation(byte[]? checkpoint)
        {
            return checkpoint == null ? default : ByteString.From(SHA256.HashData(checkpoint).AsSpan());
        }

        /// <inheritdoc/>
        public async ValueTask RecordStartedAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default)
        {
            RequireBinding(binding, requireRequest: true);
            binding = SnapshotBinding(binding);
            await RequireConfiguredRegistrarAsync(binding, cancellationToken).ConfigureAwait(false);
            byte[] original = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint existing = Decode(original, binding.NamespaceUris);
            if (existing.Stage != DevicePullJournalStage.DispatchUnknown ||
                !Matches(existing.Binding, binding, includeRequest: false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The returned request does not match the recorded dispatch.");
            }
            byte[] started = Encode(existing with { Stage = DevicePullJournalStage.Started, Binding = binding });
            await ReplaceAsync(original, started, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref m_binding, Decode(started, binding.NamespaceUris).Binding);
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaSigningBinding> ResolveAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            ArrayOf<string> namespaceUris,
            CancellationToken cancellationToken = default)
        {
            if (namespaceUris.IsNull || namespaceUris.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The selected session namespace table is required.");
            }
            byte[] bytes = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint checkpoint = Decode(bytes, namespaceUris);
            if (checkpoint.Stage is DevicePullJournalStage.DispatchUnknown or DevicePullJournalStage.Installed ||
                checkpoint.Binding.Registrar != registrar || checkpoint.Binding.ApplicationId != applicationId ||
                checkpoint.Binding.RequestId != requestId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The journal does not contain this unfinished signing request.");
            }
            Volatile.Write(ref m_binding, SnapshotBinding(checkpoint.Binding));
            return checkpoint.Binding;
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaCredentials?> GetReceivedAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default)
        {
            binding = SnapshotBinding(binding);
            byte[] bytes = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint checkpoint = Decode(bytes, binding.NamespaceUris);
            RequireMatch(checkpoint.Binding, binding);
            if (checkpoint.Stage == DevicePullJournalStage.Installed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The retained credentials are already installed.");
            }
            return checkpoint.Credentials;
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaCertificateReceipt> GetOrCreateAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive,
            CancellationToken cancellationToken = default)
        {
            if (receive == null)
            {
                throw new ArgumentNullException(nameof(receive));
            }
            PullDcaSigningBinding binding = SnapshotBinding(Volatile.Read(ref m_binding) ??
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Resolve the signing request before receiving its certificate."));
            if (binding.Registrar != registrar || binding.ApplicationId != applicationId ||
                binding.RequestId != requestId || binding.Profile != profile)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            await m_receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] original = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
                DevicePullJournalCheckpoint existing = Decode(original, binding.NamespaceUris);
                RequireMatch(existing.Binding, binding);
                if (existing.Receipt != null)
                {
                    return existing.Receipt;
                }
                if (existing.Stage is not (DevicePullJournalStage.Started or DevicePullJournalStage.Receiving))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The FinishRequest outcome requires recovery.");
                }
                byte[] receiving = original;
                if (existing.Stage == DevicePullJournalStage.Started)
                {
                    receiving = Encode(existing with { Stage = DevicePullJournalStage.Receiving });
                    await ReplaceAsync(original, receiving, cancellationToken).ConfigureAwait(false);
                }
                PullDcaCertificateReceipt receipt;
                try
                {
                    receipt = await receive(cancellationToken).ConfigureAwait(false);
                }
                catch (PullDcaCertificatePendingException)
                {
                    byte[] started = Encode(existing with { Stage = DevicePullJournalStage.Started });
                    await ReplaceAsync(receiving, started, cancellationToken).ConfigureAwait(false);
                    throw;
                }
                if (receipt == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                receipt = new PullDcaCertificateReceipt(
                    CopyByteString(receipt.Certificate), CopyByteStrings(receipt.IssuerCertificates));
                RequireReceipt(receipt, binding);
                byte[] received = Encode(existing with
                {
                    Stage = DevicePullJournalStage.CertificateReceived,
                    Receipt = receipt
                });
                await ReplaceAsync(receiving, received, cancellationToken).ConfigureAwait(false);
                return Decode(received, binding.NamespaceUris).Receipt!;
            }
            finally
            {
                m_receiveGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask RecordReceivedAsync(
            PullDcaSigningBinding binding,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            binding = SnapshotBinding(binding);
            credentials = SnapshotCredentials(credentials);
            byte[] original = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint existing = Decode(original, binding.NamespaceUris);
            RequireMatch(existing.Binding, binding);
            if (existing.Stage != DevicePullJournalStage.CertificateReceived || existing.Receipt == null ||
                existing.Receipt.Certificate != credentials.Certificate ||
                !existing.Receipt.IssuerCertificates.Span.SequenceEqual(credentials.IssuerCertificates.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The received credentials do not match the signing receipt.");
            }
            byte[] received = Encode(existing with
            {
                Stage = DevicePullJournalStage.Received,
                Credentials = credentials
            });
            await ReplaceAsync(original, received, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RecordInstalledAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default)
        {
            await RequireInstalledCheckpointAsync(binding, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Requires the retained credentials and original registrar to match an installation attempt.
        /// </summary>
        public async ValueTask RequireReceivedAsync(
            Guid attemptId,
            ByteString registrarCertificate,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            registrarCertificate = CopyByteString(registrarCertificate);
            credentials = SnapshotCredentials(credentials);
            byte[] original = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint existing = RequireSecurityMatch(
                original, attemptId, registrarCertificate, credentials);
            if (existing.Stage != DevicePullJournalStage.Received)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The retained pull attempt is not awaiting installation.");
            }
        }

        /// <summary>
        /// Records installation after the host has verified the applied certificate and trust stores.
        /// </summary>
        public async ValueTask RecordInstalledAsync(
            Guid attemptId,
            ByteString registrarCertificate,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            registrarCertificate = CopyByteString(registrarCertificate);
            credentials = SnapshotCredentials(credentials);
            byte[] original = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint existing = RequireSecurityMatch(
                original, attemptId, registrarCertificate, credentials);
            if (existing.Stage == DevicePullJournalStage.Installed)
            {
                return;
            }
            if (existing.Stage != DevicePullJournalStage.Received)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            byte[] installed = Encode(existing with { Stage = DevicePullJournalStage.Installed });
            await ReplaceAsync(original, installed, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Matches an installation attempt to its retained registrar and credentials.
        /// </summary>
        private DevicePullJournalCheckpoint RequireSecurityMatch(
            byte[] original,
            Guid attemptId,
            ByteString registrarCertificate,
            PullDcaCredentials credentials)
        {
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            DevicePullJournalCheckpoint existing = Decode(original, default);
            using CertificateCollection registrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                existing.Binding.RegistrarChannelCertificate.Memory);
            if (attemptId == Guid.Empty || existing.Binding.AttemptId != attemptId || registrar.Count == 0 ||
                !registrar[0].RawData.AsSpan().SequenceEqual(registrarCertificate.Span) ||
                existing.Credentials == null || !SameCredentials(existing.Credentials, credentials))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The installed security does not match the pull receipt.");
            }
            return existing;
        }

        /// <summary>
        /// Retains and invalidates the current attempt before a security reset begins.
        /// </summary>
        public async ValueTask BeginResetAsync(CancellationToken cancellationToken = default)
        {
            Task? dispatch;
            lock (m_dispatchGate)
            {
                if (m_resetInProgress)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A pull signing reset is already in progress.");
                }
                m_resetInProgress = true;
                DispatchReservation? reservation = m_dispatchReservation;
                if (reservation != null)
                {
                    reservation.Revoked = true;
                }
                dispatch = reservation?.DispatchCompletion?.Task;
            }
            try
            {
                if (dispatch != null)
                {
                    await dispatch.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                await BeginResetCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (m_dispatchGate)
                {
                    m_resetInProgress = false;
                }
            }
        }

        /// <summary>
        /// Publishes a reset marker and preserves the invalidated request.
        /// </summary>
        private async ValueTask BeginResetCoreAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                byte[]? original = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
                ResetMarker? reset = original == null ? null : DecodeResetMarker(original);
                if (reset?.Stage == DevicePullJournalStage.Resetting)
                {
                    return;
                }
                ArrayOf<string> retired = reset?.RetiredAttempts ?? [];
                if (original != null && reset == null)
                {
                    DevicePullJournalCheckpoint current = Decode(original, default);
                    var entries = current.RetiredAttempts.ToList();
                    string retiredKey = GetRetiredKey(original);
                    if (!entries.Contains(retiredKey, StringComparer.Ordinal))
                    {
                        entries.Add(retiredKey);
                    }
                    if (!await m_files.CompareExchangeAsync(
                        retiredKey, null, original, cancellationToken).ConfigureAwait(false))
                    {
                        byte[]? existing = await m_files.ReadAsync(retiredKey, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing == null || !existing.AsSpan().SequenceEqual(original))
                        {
                            throw new InvalidDataException("Retired pull signing evidence conflicts with its digest.");
                        }
                    }
                    retired = entries.ToArrayOf();
                }
                byte[] resetting = EncodeResetMarker(
                    new(DevicePullJournalStage.Resetting, retired, Guid.NewGuid()));
                if (await m_files.CompareExchangeAsync(
                    m_key, original, resetting, cancellationToken).ConfigureAwait(false))
                {
                    Volatile.Write(ref m_binding, null);
                    return;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadInvalidState, "The pull signing journal changed repeatedly during reset.");
        }

        /// <summary>
        /// Records completion of a pending security reset.
        /// </summary>
        public async ValueTask CompleteResetAsync(CancellationToken cancellationToken = default)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                byte[]? original = await m_files.ReadAsync(m_key, cancellationToken).ConfigureAwait(false);
                if (original == null)
                {
                    return;
                }
                ResetMarker? reset = DecodeResetMarker(original);
                if (reset == null || reset.Stage == DevicePullJournalStage.Retired)
                {
                    return;
                }
                byte[] retired = EncodeResetMarker(reset with
                {
                    Stage = DevicePullJournalStage.Retired,
                    Generation = reset.Generation == Guid.Empty ? Guid.NewGuid() : reset.Generation
                });
                if (await m_files.CompareExchangeAsync(
                    m_key, original, retired, cancellationToken).ConfigureAwait(false))
                {
                    Volatile.Write(ref m_binding, null);
                    return;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadInvalidState, "The pull signing journal changed repeatedly during reset completion.");
        }

        /// <summary>
        /// Requires the installed journal entry and certificate for a manager request.
        /// </summary>
        internal async ValueTask RequireInstalledManagerAsync(
            PullDcaSigningBinding binding,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            certificate = CopyByteString(certificate);
            DevicePullJournalCheckpoint existing = await RequireInstalledCheckpointAsync(binding, cancellationToken)
                .ConfigureAwait(false);
            if (certificate.IsEmpty || existing.Credentials == null ||
                existing.Credentials.Certificate != certificate)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The installed certificate differs from the pull receipt.");
            }
        }

        /// <summary>
        /// Detaches every encoded identifier and byte string before an operation can yield to its caller.
        /// </summary>
        internal PullDcaSigningBinding SnapshotBinding(PullDcaSigningBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            bool started = !binding.RequestId.IsNull;
            RequireBinding(binding, started);
            DevicePullJournalStage stage = started
                ? DevicePullJournalStage.Started
                : DevicePullJournalStage.DispatchUnknown;
            return Decode(Encode(new(stage, binding, null, null, [])), default).Binding;
        }

        /// <summary>
        /// Reads an installed checkpoint only when every retained binding field matches.
        /// </summary>
        private async ValueTask<DevicePullJournalCheckpoint> RequireInstalledCheckpointAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken)
        {
            binding = SnapshotBinding(binding);
            byte[] original = await RequireBytesAsync(cancellationToken).ConfigureAwait(false);
            DevicePullJournalCheckpoint existing = Decode(original, binding.NamespaceUris);
            RequireMatch(existing.Binding, binding);
            if (existing.Stage == DevicePullJournalStage.Installed)
            {
                return existing;
            }
            throw new ServiceResultException(
                StatusCodes.BadInvalidState, "The device security owner has not recorded the installation.");
        }

        /// <summary>
        /// Reads the current checkpoint or reports that no checkpoint exists.
        /// </summary>
        private async ValueTask<byte[]> RequireBytesAsync(CancellationToken ct)
        {
            return await m_files.ReadAsync(m_key, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotFound, "The pull signing journal is missing.");
        }

        /// <summary>
        /// Publishes a checkpoint only if the expected complete value is still current.
        /// </summary>
        private async ValueTask ReplaceAsync(byte[] expected, byte[] replacement, CancellationToken ct)
        {
            if (!await m_files.CompareExchangeAsync(m_key, expected, replacement, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The pull signing journal changed during the operation.");
            }
        }

        /// <summary>
        /// Encodes the reset stage, generation, and retained evidence references.
        /// </summary>
        private byte[] EncodeResetMarker(ResetMarker marker)
        {
            if (marker.Stage is not (DevicePullJournalStage.Resetting or DevicePullJournalStage.Retired) ||
                marker.Generation == Guid.Empty)
            {
                throw new InvalidDataException("The pull reset stage is invalid.");
            }
            RequireRetiredKeys(marker.RetiredAttempts);
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, kResetMarkerVersion);
                encoder.WriteString(null, m_applicationUri);
                encoder.WriteInt32(null, (int)marker.Stage);
                encoder.WriteGuid(null, marker.Generation);
                encoder.WriteStringArray(null, marker.RetiredAttempts);
            }
            if (buffer.Length > PushRegistrarCheckpointFiles.MaximumBytes)
            {
                throw new InvalidDataException("The pull reset marker exceeds its size limit.");
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Reads current and legacy reset markers, or returns null for an active checkpoint.
        /// </summary>
        private ResetMarker? DecodeResetMarker(ReadOnlySpan<byte> bytes)
        {
            try
            {
                using var buffer = new MemoryStream(bytes.ToArray(), writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                uint version = decoder.ReadUInt32(null);
                if (version is not (kVersion or kResetMarkerVersion) ||
                    decoder.ReadString(null) != m_applicationUri)
                {
                    throw new InvalidDataException("The pull signing journal version or device does not match.");
                }
                var stage = (DevicePullJournalStage)decoder.ReadInt32(null);
                if (stage is not (DevicePullJournalStage.Resetting or DevicePullJournalStage.Retired))
                {
                    if (version != kVersion)
                    {
                        throw new InvalidDataException("The pull reset stage is invalid.");
                    }
                    return null;
                }
                Guid generation = version == kResetMarkerVersion ? decoder.ReadGuid(null) : Guid.Empty;
                if (version == kResetMarkerVersion && generation == Guid.Empty)
                {
                    throw new InvalidDataException("The pull reset generation is invalid.");
                }
                ArrayOf<string> retired = CopyStringArray(decoder.ReadStringArray(null));
                RequireRetiredKeys(retired);
                if (decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The pull reset marker contains trailing data.");
                }
                return new ResetMarker(stage, retired, generation);
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or
                ArgumentException or FormatException)
            {
                throw new InvalidDataException("The pull reset marker is invalid.", error);
            }
        }

        /// <summary>
        /// Identifies retired evidence by its complete encoded content.
        /// </summary>
        private string GetRetiredKey(byte[] checkpoint)
        {
            return m_key + "-retired-" + CoreUtils.ToHexString(SHA256.HashData(checkpoint));
        }

        /// <summary>
        /// Validates unique evidence identifiers within this journal's scope.
        /// </summary>
        private void RequireRetiredKeys(ArrayOf<string> retired)
        {
            if (retired.IsNull)
            {
                throw new InvalidDataException("The retired pull checkpoint list is missing.");
            }
            string prefix = m_key + "-retired-";
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in retired)
            {
                if (key == null || !key.StartsWith(prefix, StringComparison.Ordinal) ||
                    key.Length != prefix.Length + 64 || !unique.Add(key))
                {
                    throw new InvalidDataException("A retired pull checkpoint key is invalid.");
                }
                foreach (char character in key.AsSpan(prefix.Length))
                {
                    if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
                    {
                        throw new InvalidDataException("A retired pull checkpoint digest is invalid.");
                    }
                }
            }
        }

        /// <summary>
        /// Encodes a complete request checkpoint with URI-qualified identifiers.
        /// </summary>
        private byte[] Encode(DevicePullJournalCheckpoint checkpoint)
        {
            PullDcaSigningBinding binding = checkpoint.Binding;
            RequireRetiredKeys(checkpoint.RetiredAttempts);
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, kVersion);
                encoder.WriteString(null, m_applicationUri);
                encoder.WriteInt32(null, (int)checkpoint.Stage);
                encoder.WriteStringArray(null, checkpoint.RetiredAttempts);
                encoder.WriteGuid(null, binding.AttemptId);
                encoder.WriteStringArray(null, binding.NamespaceUris);
                encoder.WriteString(null, binding.Registrar.EndpointUrl.AbsoluteUri);
                encoder.WriteString(null, binding.RegistrarApplicationUri);
                WriteId(encoder, binding.Registrar.RegistrarId, binding.NamespaceUris);
                WriteId(encoder, binding.Registrar.CertificateDirectoryId, binding.NamespaceUris);
                WriteId(encoder, binding.ApplicationId, binding.NamespaceUris);
                WriteId(encoder, binding.RequestId, binding.NamespaceUris);
                WriteId(encoder, binding.Profile.CertificateGroupId, binding.NamespaceUris);
                WriteId(encoder, binding.Profile.CertificateTypeId, binding.NamespaceUris);
                encoder.WriteByteString(null, binding.SigningRequest);
                encoder.WriteByteString(null, binding.SigningKeyCertificate);
                encoder.WriteByteString(null, binding.ClientChannelCertificate);
                encoder.WriteByteString(null, binding.RegistrarChannelCertificate);
                encoder.WriteBoolean(null, checkpoint.Receipt != null);
                if (checkpoint.Receipt != null)
                {
                    encoder.WriteByteString(null, checkpoint.Receipt.Certificate);
                    encoder.WriteByteStringArray(null, checkpoint.Receipt.IssuerCertificates);
                }
                encoder.WriteBoolean(null, checkpoint.Credentials != null);
                if (checkpoint.Credentials != null)
                {
                    encoder.WriteByteString(null, checkpoint.Credentials.Certificate);
                    encoder.WriteByteStringArray(null, checkpoint.Credentials.IssuerCertificates);
                    encoder.WriteEncodeable(null, checkpoint.Credentials.TrustList);
                }
            }
            if (buffer.Length > PushRegistrarCheckpointFiles.MaximumBytes)
            {
                throw new InvalidDataException("The pull signing journal exceeds its size limit.");
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Decodes and validates a checkpoint against its retained or selected namespaces.
        /// </summary>
        private DevicePullJournalCheckpoint Decode(ReadOnlySpan<byte> bytes, ArrayOf<string> targetNamespaces)
        {
            try
            {
                using var buffer = new MemoryStream(bytes.ToArray(), writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                if (decoder.ReadUInt32(null) != kVersion || decoder.ReadString(null) != m_applicationUri)
                {
                    throw new InvalidDataException("The pull signing journal version or device does not match.");
                }
                var stage = (DevicePullJournalStage)decoder.ReadInt32(null);
                ArrayOf<string> retired = CopyStringArray(decoder.ReadStringArray(null));
                RequireRetiredKeys(retired);
                Guid attemptId = decoder.ReadGuid(null);
                ArrayOf<string> originalNamespaces = CopyStringArray(decoder.ReadStringArray(null));
                ArrayOf<string> namespaces = targetNamespaces.IsNull ? originalNamespaces : targetNamespaces;
                string endpointText = decoder.ReadString(null) ?? string.Empty;
                string registrarUri = decoder.ReadString(null) ?? string.Empty;
                if (!Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint))
                {
                    throw new InvalidDataException("The pull signing journal endpoint is invalid.");
                }
                NodeId registrarId = ReadId(decoder, namespaces);
                NodeId directoryId = ReadId(decoder, namespaces);
                NodeId applicationId = ReadId(decoder, namespaces);
                NodeId requestId = ReadId(decoder, namespaces);
                NodeId groupId = ReadId(decoder, namespaces);
                NodeId typeId = ReadId(decoder, namespaces);
                var binding = new PullDcaSigningBinding(
                    new PullDcaRegistrar(endpoint, registrarId, directoryId),
                    applicationId, requestId, new PullDcaCertificateProfile(groupId, typeId),
                    decoder.ReadByteString(null), decoder.ReadByteString(null), decoder.ReadByteString(null),
                    decoder.ReadByteString(null), registrarUri, namespaces, attemptId);
                PullDcaCertificateReceipt? receipt = null;
                if (decoder.ReadBoolean(null))
                {
                    receipt = new PullDcaCertificateReceipt(
                        decoder.ReadByteString(null), decoder.ReadByteStringArray(null));
                }
                PullDcaCredentials? credentials = null;
                if (decoder.ReadBoolean(null))
                {
                    credentials = new PullDcaCredentials(
                        decoder.ReadByteString(null), decoder.ReadByteStringArray(null),
                        decoder.ReadEncodeable<TrustListDataType>(null));
                }
                if (decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The pull signing journal contains trailing data.");
                }
                bool consistent = stage switch
                {
                    DevicePullJournalStage.DispatchUnknown =>
                        binding.RequestId.IsNull && receipt == null && credentials == null,
                    DevicePullJournalStage.Started or DevicePullJournalStage.Receiving =>
                        !binding.RequestId.IsNull && receipt == null && credentials == null,
                    DevicePullJournalStage.CertificateReceived =>
                        !binding.RequestId.IsNull && receipt != null && credentials == null,
                    DevicePullJournalStage.Received or DevicePullJournalStage.Installed =>
                        !binding.RequestId.IsNull && receipt != null && credentials != null,
                    _ => false
                };
                if (!consistent)
                {
                    throw new InvalidDataException("The pull signing journal stage is inconsistent.");
                }
                RequireBinding(binding, stage != DevicePullJournalStage.DispatchUnknown);
                if (receipt != null)
                {
                    RequireReceipt(receipt, binding);
                }
                if (credentials != null &&
                    (credentials.TrustList == null ||
                        credentials.TrustList.SpecifiedLists != (uint)TrustListMasks.All ||
                        credentials.Certificate != receipt!.Certificate ||
                        !credentials.IssuerCertificates.Span.SequenceEqual(receipt.IssuerCertificates.Span)))
                {
                    throw new InvalidDataException("The received credentials do not match the signing receipt.");
                }
                return new DevicePullJournalCheckpoint(stage, binding, receipt, credentials, retired);
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or
                ArgumentException or FormatException or CryptographicException)
            {
                throw new InvalidDataException("The pull signing journal is invalid.", error);
            }
        }

        /// <summary>
        /// Validates request identifiers, channel certificates, and the CSR's signing-key evidence.
        /// </summary>
        private void RequireBinding(PullDcaSigningBinding binding, bool requireRequest)
        {
            if (binding == null || binding.Registrar == null || binding.Profile == null ||
                binding.Registrar.EndpointUrl == null || !binding.Registrar.EndpointUrl.IsAbsoluteUri ||
                binding.ApplicationId.IsNull || binding.Registrar.RegistrarId.IsNull ||
                binding.Registrar.CertificateDirectoryId.IsNull || binding.Profile.CertificateGroupId.IsNull ||
                binding.Profile.CertificateTypeId.IsNull || binding.RequestId.IsNull == requireRequest ||
                binding.SigningRequest.IsEmpty || binding.SigningKeyCertificate.IsEmpty ||
                binding.ClientChannelCertificate.IsEmpty || binding.RegistrarChannelCertificate.IsEmpty ||
                binding.AttemptId == Guid.Empty ||
                !Uri.TryCreate(binding.RegistrarApplicationUri, UriKind.Absolute, out _))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            using Certificate signingKey = Certificate.FromRawData(binding.SigningKeyCertificate);
            using RSA? publicKey = signingKey.GetRSAPublicKey();
            var request = new Pkcs10CertificationRequest(binding.SigningRequest.ToArray());
            if (publicKey == null || !request.Verify() ||
                !EncodeRsaSubjectPublicKeyInfo(publicKey).AsSpan().SequenceEqual(request.SubjectPublicKeyInfo) ||
                !X509Utils.GetApplicationUrisFromCertificate(signingKey).Contains(m_applicationUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The retained signing key does not match the CSR.");
            }
            using CertificateCollection registrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                binding.RegistrarChannelCertificate.Memory);
            if (registrar.Count == 0 ||
                !X509Utils.GetApplicationUrisFromCertificate(registrar[0]).Contains(binding.RegistrarApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
        }

        /// <summary>
        /// Encodes the RSA public key in the canonical SubjectPublicKeyInfo form used by the platform.
        /// </summary>
        private static byte[] EncodeRsaSubjectPublicKeyInfo(RSA publicKey)
        {
            RSAParameters parameters = publicKey.ExportParameters(false);
            var rsa = new AsnWriter(AsnEncodingRules.DER);
            rsa.PushSequence();
            WriteUnsignedInteger(rsa, parameters.Modulus);
            WriteUnsignedInteger(rsa, parameters.Exponent);
            rsa.PopSequence();

            var encoded = new AsnWriter(AsnEncodingRules.DER);
            encoded.PushSequence();
            encoded.PushSequence();
            encoded.WriteObjectIdentifier(Oids.Rsa);
            encoded.WriteNull();
            encoded.PopSequence();
            encoded.WriteBitString(rsa.Encode());
            encoded.PopSequence();
            return encoded.Encode();
        }

        /// <summary>
        /// Writes a non-empty unsigned RSA parameter using its minimal DER representation.
        /// </summary>
        private static void WriteUnsignedInteger(AsnWriter writer, byte[]? value)
        {
            if (value == null || value.Length == 0)
            {
                throw new CryptographicException("An RSA public-key parameter is missing.");
            }
            ReadOnlySpan<byte> integer = value;
            while (integer.Length > 1 && integer[0] == 0)
            {
                integer = integer[1..];
            }
            writer.WriteIntegerUnsigned(integer);
        }

        /// <summary>
        /// Checks that the authenticated registrar remains an explicitly configured candidate.
        /// </summary>
        private async ValueTask RequireConfiguredRegistrarAsync(PullDcaSigningBinding binding, CancellationToken ct)
        {
            if (m_requireConfiguredRegistrar != null)
            {
                await m_requireConfiguredRegistrar(binding.Registrar, binding.RegistrarApplicationUri, ct)
                    .ConfigureAwait(false);
            }
            else if (!string.Equals(
                binding.RegistrarApplicationUri, m_registrarApplicationUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
        }

        /// <summary>
        /// Detaches the certificate, issuer chain, and each complete trust-list entry.
        /// </summary>
        internal static PullDcaCredentials SnapshotCredentials(PullDcaCredentials credentials)
        {
            if (credentials == null || credentials.TrustList == null ||
                credentials.TrustList.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A complete received trust list is required.");
            }
            TrustListDataType trust = credentials.TrustList;
            return new PullDcaCredentials(
                CopyByteString(credentials.Certificate),
                CopyByteStrings(credentials.IssuerCertificates),
                new TrustListDataType
                {
                    SpecifiedLists = trust.SpecifiedLists,
                    TrustedCertificates = CopyByteStrings(trust.TrustedCertificates),
                    TrustedCrls = CopyByteStrings(trust.TrustedCrls),
                    IssuerCertificates = CopyByteStrings(trust.IssuerCertificates),
                    IssuerCrls = CopyByteStrings(trust.IssuerCrls)
                });
        }

        /// <summary>
        /// Copies a byte string while retaining its OPC UA null value.
        /// </summary>
        private static ByteString CopyByteString(ByteString value)
        {
            return value.IsNull ? default : ByteString.From(value.Span);
        }

        /// <summary>
        /// Copies an array and its byte strings without replacing a null array with an empty array.
        /// </summary>
        private static ArrayOf<ByteString> CopyByteStrings(ArrayOf<ByteString> values)
        {
            if (values.IsNull)
            {
                return default;
            }
            var copy = new ByteString[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = CopyByteString(values[i]);
            }
            return copy;
        }

        /// <summary>
        /// Requires the issued certificate to use the retained request key and device application URI.
        /// </summary>
        private void RequireReceipt(PullDcaCertificateReceipt receipt, PullDcaSigningBinding binding)
        {
            if (receipt == null || receipt.Certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            foreach (ByteString issuer in receipt.IssuerCertificates)
            {
                if (issuer.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
            }
            using Certificate issued = Certificate.FromRawData(receipt.Certificate);
            using Certificate signing = Certificate.FromRawData(binding.SigningKeyCertificate);
            using RSA? issuedKey = issued.GetRSAPublicKey();
            using RSA? signingKey = signing.GetRSAPublicKey();
            if (issuedKey == null || signingKey == null ||
                !EncodeRsaSubjectPublicKeyInfo(issuedKey).AsSpan()
                    .SequenceEqual(EncodeRsaSubjectPublicKeyInfo(signingKey)) ||
                !X509Utils.GetApplicationUrisFromCertificate(issued).Contains(m_applicationUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The issued certificate does not match the retained CSR.");
            }
        }

        /// <summary>
        /// Compares the issued certificate, issuer chain, and complete trust lists.
        /// </summary>
        private static bool SameCredentials(
            PullDcaCredentials expected,
            PullDcaCredentials actual)
        {
            return expected.Certificate == actual.Certificate &&
                expected.IssuerCertificates.Span.SequenceEqual(actual.IssuerCertificates.Span) &&
                expected.TrustList.SpecifiedLists == actual.TrustList.SpecifiedLists &&
                SameTrustEntries(expected.TrustList.TrustedCertificates, actual.TrustList.TrustedCertificates) &&
                SameTrustEntries(expected.TrustList.TrustedCrls, actual.TrustList.TrustedCrls) &&
                SameTrustEntries(expected.TrustList.IssuerCertificates, actual.TrustList.IssuerCertificates) &&
                SameTrustEntries(expected.TrustList.IssuerCrls, actual.TrustList.IssuerCrls);
        }

        /// <summary>
        /// Compares trust-list entries as sets, independent of store enumeration order.
        /// </summary>
        private static bool SameTrustEntries(ArrayOf<ByteString> left, ArrayOf<ByteString> right)
        {
            return new HashSet<ByteString>(left.ToList()).SetEquals(right.ToList());
        }

        /// <summary>
        /// Rejects a binding that differs from the retained signing request.
        /// </summary>
        private static void RequireMatch(PullDcaSigningBinding expected, PullDcaSigningBinding actual)
        {
            if (!Matches(expected, actual, includeRequest: true))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The signing request does not match the retained binding.");
            }
        }

        /// <summary>
        /// Compares request, channel, key, and registrar evidence for one attempt.
        /// </summary>
        private static bool Matches(
            PullDcaSigningBinding expected,
            PullDcaSigningBinding actual,
            bool includeRequest)
        {
            return expected.Registrar == actual.Registrar &&
                expected.AttemptId == actual.AttemptId &&
                expected.ApplicationId == actual.ApplicationId &&
                (!includeRequest || expected.RequestId == actual.RequestId) &&
                expected.Profile == actual.Profile &&
                expected.SigningRequest == actual.SigningRequest &&
                expected.SigningKeyCertificate == actual.SigningKeyCertificate &&
                expected.ClientChannelCertificate == actual.ClientChannelCertificate &&
                expected.RegistrarChannelCertificate == actual.RegistrarChannelCertificate &&
                string.Equals(expected.RegistrarApplicationUri, actual.RegistrarApplicationUri,
                    StringComparison.Ordinal) &&
                expected.NamespaceUris.Span.SequenceEqual(actual.NamespaceUris.Span);
        }

        /// <summary>
        /// Writes a node identifier with the URI of its namespace.
        /// </summary>
        private static void WriteId(BinaryEncoder encoder, NodeId id, ArrayOf<string> namespaces)
        {
            if (id.IsNull)
            {
                encoder.WriteString(null, null);
                encoder.WriteNodeId(null, NodeId.Null);
                return;
            }
            int index = id.NamespaceIndex;
            if (index >= namespaces.Count || string.IsNullOrEmpty(namespaces[index]))
            {
                throw new InvalidDataException("A signing identifier has no session namespace URI.");
            }
            encoder.WriteString(null, namespaces[index]);
            encoder.WriteNodeId(null, id.WithNamespaceIndex(0));
        }

        /// <summary>
        /// Resolves an encoded namespace URI in the selected session's namespace table.
        /// </summary>
        private static NodeId ReadId(BinaryDecoder decoder, ArrayOf<string> namespaces)
        {
            string? uri = decoder.ReadString(null);
            NodeId id = decoder.ReadNodeId(null);
            if (id.NamespaceIndex != 0)
            {
                throw new InvalidDataException("A signing identifier is not URI-qualified.");
            }
            if (string.IsNullOrEmpty(uri))
            {
                return id.IsNull
                    ? id
                    : throw new InvalidDataException("A signing identifier is not URI-qualified.");
            }
            for (int i = 0; i < namespaces.Count && i <= ushort.MaxValue; i++)
            {
                if (string.Equals(namespaces[i], uri, StringComparison.Ordinal))
                {
                    if (i == 0 && id.IsNull)
                    {
                        throw new InvalidDataException("A null signing identifier cannot carry a namespace URI.");
                    }
                    return id.WithNamespaceIndex((ushort)i);
                }
            }
            throw new InvalidDataException("A signing namespace is not present in the selected session.");
        }

        /// <summary>
        /// Retains reset evidence and a generation that changes for every new reset.
        /// </summary>
        private sealed record ResetMarker(
            DevicePullJournalStage Stage, ArrayOf<string> RetiredAttempts, Guid Generation);

        /// <summary>
        /// Holds one outbound attempt's checkpoint generation until its owned session is retired.
        /// </summary>
        private sealed class DispatchReservation : IDisposable
        {
            /// <summary>
            /// Captures the complete journal value before the attempt can select a channel.
            /// </summary>
            public DispatchReservation(DevicePullJournal owner, byte[]? expected)
            {
                m_owner = owner;
                Expected = expected;
            }

            /// <summary>The checkpoint value that the attempt may replace.</summary>
            public byte[]? Expected { get; }

            /// <summary>The attempt whose local Start call is still in progress.</summary>
            public Guid DispatchAttempt { get; private set; }

            /// <summary>Completes when the local Start call and its request ID publication finish.</summary>
            public TaskCompletionSource<bool>? DispatchCompletion { get; private set; }

            /// <summary>Whether a reset invalidated this reservation.</summary>
            public bool Revoked { get; set; }

            /// <summary>Whether the owning attempt released this reservation.</summary>
            public bool Released { get; private set; }

            /// <summary>
            /// Records the local Start operation before checkpoint publication begins.
            /// </summary>
            public void Start(Guid attemptId)
            {
                DispatchAttempt = attemptId;
                DispatchCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            /// <summary>
            /// Signals that the local Start operation and request ID publication have ended.
            /// </summary>
            public void Complete()
            {
                DispatchCompletion?.TrySetResult(true);
                DispatchCompletion = null;
                DispatchAttempt = Guid.Empty;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (m_owner.m_dispatchGate)
                {
                    Released = true;
                    Revoked = true;
                    if (DispatchCompletion == null && ReferenceEquals(m_owner.m_dispatchReservation, this))
                    {
                        m_owner.m_dispatchReservation = null;
                    }
                }
            }

            /// <summary>The journal which owns this reservation.</summary>
            private readonly DevicePullJournal m_owner;
        }

        /// <summary>The journal format containing reset history.</summary>
        private const uint kVersion = 2;

        /// <summary>The reset format containing a unique generation.</summary>
        private const uint kResetMarkerVersion = 3;

        /// <summary>The receive callback gate shared by journals borrowing one exclusive writer.</summary>
        private static readonly ConditionalWeakTable<PushRegistrarCheckpointFiles, SemaphoreSlim> s_receiveGates =
            new();

        /// <summary>The receive callback gate for this journal's exclusive writer.</summary>
        private readonly SemaphoreSlim m_receiveGate;

        /// <summary>The single-writer public checkpoint backend.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;

        /// <summary>The device application URI recorded with the request.</summary>
        private readonly string m_applicationUri;

        /// <summary>The operator-selected registrar application URI.</summary>
        private readonly string? m_registrarApplicationUri;

        /// <summary>The optional validator for explicitly configured registrar candidates.</summary>
        private readonly Func<PullDcaRegistrar, string, CancellationToken, ValueTask>? m_requireConfiguredRegistrar;

        /// <summary>Whether outbound dispatch requires a session-owned reset generation.</summary>
        private readonly bool m_requireDispatchReservation;

        /// <summary>Protects the one active dispatch reservation.</summary>
        private readonly Lock m_dispatchGate = new();

        /// <summary>The checkpoint key for this device.</summary>
        private readonly string m_key;

        /// <summary>The standard binary encoding context.</summary>
        private readonly ServiceMessageContext m_context;

        /// <summary>The most recently resolved binding for the receipt-store interface.</summary>
        private PullDcaSigningBinding? m_binding;

        /// <summary>The reset generation owned by the active outbound attempt.</summary>
        private DispatchReservation? m_dispatchReservation;

        /// <summary>Whether a reset is waiting for or publishing a local dispatch transition.</summary>
        private bool m_resetInProgress;
    }
}
