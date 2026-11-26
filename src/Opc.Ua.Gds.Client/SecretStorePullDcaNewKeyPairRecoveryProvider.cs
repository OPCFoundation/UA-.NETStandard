// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// A host-supplied secret store for pull DCA recovery checkpoints.
    /// </summary>
    /// <remarks>
    /// The host must enforce exclusive writer ownership of each configured slot for the lifetime
    /// of the workflow. Successful writes and removals must be atomic and durable. A read must
    /// return the latest complete checkpoint, or null only for a confirmed absent key. Protect
    /// checkpoint confidentiality and integrity, including staging and backups; never log payloads.
    /// A write must not retain the caller's input buffer after it returns.
    /// </remarks>
    public interface IProtectedPullDcaRecoveryStore : ISecretStore
    {
    }

    /// <summary>
    /// Manages one active key-pair checkpoint in a host-owned protected secret slot.
    /// </summary>
    /// <remarks>
    /// Use one workflow and one adapter per stable slot. The slot name must not change when channel
    /// certificates, endpoints, or profiles change: the retained binding must reject those changes.
    /// The host must stop all operations before disposing the adapter or resetting an unknown Start.
    /// </remarks>
    public sealed class SecretStorePullDcaNewKeyPairRecoveryProvider :
        IPullDcaNewKeyPairRecoveryProvider, IDisposable
    {
        /// <summary>
        /// Borrows the host's protected store and opens a dedicated recovery slot.
        /// </summary>
        public SecretStorePullDcaNewKeyPairRecoveryProvider(
            IProtectedPullDcaRecoveryStore store,
            string slot,
            ITelemetryContext telemetry)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new ArgumentException("A stable recovery slot is required.", nameof(slot));
            }
            m_identifier = new SecretIdentifier("pull-dca-key-pair:" + slot, store.StoreType);
            m_context = ServiceMessageContext.Create(telemetry ?? throw new ArgumentNullException(nameof(telemetry)));
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaNewKeyPairRecoveryAttempt?> GetPendingAsync(
            PullDcaNewKeyPairRecoveryScope scope,
            CancellationToken cancellationToken = default)
        {
            if (scope is null)
            {
                throw new ArgumentNullException(nameof(scope));
            }
            scope = PullDcaConnection.SnapshotRecoveryScope(scope);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                PullDcaNewKeyPairRecoveryAttempt? current = await ReadAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (current is not null)
                    {
                        PullDcaConnection.RequireSameRecoveryScope(current.Binding.Scope, scope);
                    }
                    PullDcaNewKeyPairRecoveryAttempt? result = current;
                    current = null;
                    return result;
                }
                finally
                {
                    Clear(current);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask RecordDispatchAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            CancellationToken cancellationToken = default)
        {
            return TransitionAsync(binding, null, PullDcaNewKeyPairRecoveryState.Dispatching,
                default, default, default, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask RecordStartedAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            CancellationToken cancellationToken = default)
        {
            return TransitionAsync(binding,
                PullDcaNewKeyPairRecoveryState.Dispatching, PullDcaNewKeyPairRecoveryState.Started,
                default, default, default, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask RecordReceivedAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            ByteString certificate,
            ArrayOf<ByteString> issuerCertificates,
            ByteString privateKeyPfx,
            CancellationToken cancellationToken = default)
        {
            return TransitionAsync(binding,
                PullDcaNewKeyPairRecoveryState.Started, PullDcaNewKeyPairRecoveryState.Received,
                certificate, issuerCertificates, privateKeyPfx, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask RetireLostRequestAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            CancellationToken cancellationToken = default)
        {
            return TransitionAsync(binding, PullDcaNewKeyPairRecoveryState.Started, null,
                default, default, default, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask CompleteAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            ByteString installedCertificate,
            CancellationToken cancellationToken = default)
        {
            if (installedCertificate.IsEmpty)
            {
                throw new ArgumentException("The verified installed certificate is required.",
                    nameof(installedCertificate));
            }
            return TransitionAsync(binding, PullDcaNewKeyPairRecoveryState.Received, null,
                installedCertificate, default, default, cancellationToken);
        }

        /// <summary>
        /// Removes the exact unknown-Start checkpoint after an operator has reconciled the registrar.
        /// Do not call this while the workflow is running. Started and Received cannot be reset here.
        /// </summary>
        public ValueTask ResetReconciledDispatchAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            CancellationToken cancellationToken = default)
        {
            return TransitionAsync(binding, PullDcaNewKeyPairRecoveryState.Dispatching, null,
                default, default, default, cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        /// <summary>
        /// Verifies the current checkpoint and applies one durable transition.
        /// </summary>
        private async ValueTask TransitionAsync(
            PullDcaNewKeyPairRecoveryBinding binding,
            PullDcaNewKeyPairRecoveryState? prior,
            PullDcaNewKeyPairRecoveryState? next,
            ByteString certificate,
            ArrayOf<ByteString> issuers,
            ByteString pfx,
            CancellationToken ct)
        {
            binding = PullDcaConnection.SnapshotRecoveryBinding(binding);
            if (binding.AttemptId == Guid.Empty ||
                binding.RequestId.IsNull != (next == PullDcaNewKeyPairRecoveryState.Dispatching ||
                    (prior == PullDcaNewKeyPairRecoveryState.Dispatching && next is null)))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "The key-pair recovery binding is incomplete.");
            }
            byte[]? entry = next is { } state
                ? Encode(new PullDcaNewKeyPairRecoveryAttempt(binding, state, certificate, issuers, pfx))
                : null;
            try
            {
                certificate = certificate.Copy();
                await m_gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    PullDcaNewKeyPairRecoveryAttempt? current = await ReadAsync(ct).ConfigureAwait(false);
                    try
                    {
                        if (current is not null)
                        {
                            PullDcaConnection.RequireSameRecoveryScope(current.Binding.Scope, binding.Scope);
                        }
                        if (current?.State != prior ||
                            (current is not null &&
                                (current.Binding.AttemptId != binding.AttemptId ||
                                    (prior != PullDcaNewKeyPairRecoveryState.Dispatching &&
                                        current.Binding.RequestId != binding.RequestId) ||
                                    (prior == PullDcaNewKeyPairRecoveryState.Received &&
                                        current.Certificate != certificate))))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState,
                                "The key-pair checkpoint does not match the requested transition.");
                        }
                        if (entry is not null)
                        {
                            await m_store.SetAsync(m_identifier, entry, ct).ConfigureAwait(false);
                        }
                        else if (!await m_store.RemoveAsync(m_identifier, ct).ConfigureAwait(false))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState,
                                "The key-pair checkpoint could not be retired.");
                        }
                    }
                    finally
                    {
                        Clear(current);
                    }
                }
                finally
                {
                    m_gate.Release();
                }
            }
            finally
            {
                if (entry is not null)
                {
                    CryptoUtils.ZeroMemory(entry);
                }
            }
        }

        /// <summary>
        /// Reads a detached checkpoint from the protected backend.
        /// </summary>
        private async ValueTask<PullDcaNewKeyPairRecoveryAttempt?> ReadAsync(CancellationToken ct)
        {
            using ISecret? secret = await m_store.GetAsync(m_identifier, ct).ConfigureAwait(false);
            return secret is null ? null : Decode(secret.Bytes);
        }

        /// <summary>
        /// Encodes public fields and appends the PFX to a caller-cleared buffer.
        /// </summary>
        private byte[] Encode(PullDcaNewKeyPairRecoveryAttempt attempt)
        {
            PullDcaNewKeyPairRecoveryBinding binding = attempt.Binding;
            PullDcaNewKeyPairRecoveryScope scope = binding.Scope;
            ValidatePayload(
                attempt.State, attempt.Certificate, attempt.IssuerCertificates, attempt.PrivateKeyPfx.Span);
            using var header = new MemoryStream();
            using (var encoder = new BinaryEncoder(header, m_context, leaveOpen: true))
            {
                encoder.WriteInt32(null, 1);
                encoder.WriteInt32(null, (int)attempt.State);
                encoder.WriteGuid(null, binding.AttemptId);
                encoder.WriteNodeId(null, binding.RequestId);
                encoder.WriteString(null, scope.Registrar.EndpointUrl.AbsoluteUri);
                encoder.WriteNodeId(null, scope.Registrar.RegistrarId);
                encoder.WriteNodeId(null, scope.Registrar.CertificateDirectoryId);
                encoder.WriteNodeId(null, scope.ApplicationId);
                encoder.WriteNodeId(null, scope.Profile.CertificateGroupId);
                encoder.WriteNodeId(null, scope.Profile.CertificateTypeId);
                encoder.WriteByteString(null, scope.ClientChannelCertificate);
                encoder.WriteByteString(null, scope.RegistrarChannelCertificate);
                encoder.WriteString(null, scope.RegistrarApplicationUri);
                encoder.WriteStringArray(null, scope.NamespaceUris);
                encoder.WriteByteString(null, attempt.Certificate);
                encoder.WriteByteStringArray(null, attempt.IssuerCertificates);
                encoder.WriteInt32(null, attempt.PrivateKeyPfx.Length);
            }
            int headerLength = checked((int)header.Length);
            if (headerLength > kMaximumEntryBytes - attempt.PrivateKeyPfx.Span.Length)
            {
                throw new InvalidDataException("The key-pair checkpoint exceeds its size limit.");
            }
            var entry = new byte[headerLength + attempt.PrivateKeyPfx.Span.Length];
            header.GetBuffer().AsSpan(0, headerLength).CopyTo(entry);
            // Append the PFX after encoding public fields so encoder storage never holds it.
            attempt.PrivateKeyPfx.Span.CopyTo(entry.AsSpan(headerLength));
            return entry;
        }

        /// <summary>
        /// Restores a versioned checkpoint and rejects malformed bindings and payloads.
        /// </summary>
        private PullDcaNewKeyPairRecoveryAttempt Decode(ReadOnlySpan<byte> entry)
        {
            if (entry.Length > kMaximumEntryBytes)
            {
                throw new InvalidDataException("The key-pair checkpoint exceeds its size limit.");
            }
            byte[] bytes = entry.ToArray();
            try
            {
                using var buffer = new MemoryStream(bytes, writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                if (decoder.ReadInt32(null) != 1)
                {
                    throw new InvalidDataException("The key-pair checkpoint version is not supported.");
                }
                var state = (PullDcaNewKeyPairRecoveryState)decoder.ReadInt32(null);
                Guid attemptId = decoder.ReadGuid(null);
                NodeId requestId = decoder.ReadNodeId(null);
                if (attemptId == Guid.Empty ||
                    requestId.IsNull != (state == PullDcaNewKeyPairRecoveryState.Dispatching) ||
                    !Uri.TryCreate(decoder.ReadString(null), UriKind.Absolute, out Uri? endpoint))
                {
                    throw new InvalidDataException("The key-pair checkpoint binding is invalid.");
                }
                var registrar = new PullDcaRegistrar(endpoint, decoder.ReadNodeId(null), decoder.ReadNodeId(null));
                NodeId application = decoder.ReadNodeId(null);
                var profile = new PullDcaCertificateProfile(decoder.ReadNodeId(null), decoder.ReadNodeId(null));
                var scope = new PullDcaNewKeyPairRecoveryScope(registrar, application, profile,
                    decoder.ReadByteString(null), decoder.ReadByteString(null),
                    decoder.ReadString(null) ?? throw new InvalidDataException("The registrar URI is missing."),
                    ReadNamespaces(decoder));
                ByteString certificate = decoder.ReadByteString(null);
                ArrayOf<ByteString> issuers = decoder.ReadByteStringArray(null);
                int pfxLength = decoder.ReadInt32(null);
                if (pfxLength != bytes.Length - decoder.Position)
                {
                    throw new InvalidDataException("The key-pair checkpoint length is inconsistent.");
                }
                ReadOnlySpan<byte> pfx = bytes.AsSpan(decoder.Position);
                ValidatePayload(state, certificate, issuers, pfx);
                return new PullDcaNewKeyPairRecoveryAttempt(
                    new PullDcaNewKeyPairRecoveryBinding(scope, requestId, attemptId), state, certificate, issuers,
                    pfx.IsEmpty ? default : ByteString.From(pfx));
            }
            finally
            {
                CryptoUtils.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// Restores namespace indexes without dropping or renumbering entries.
        /// </summary>
        private static ArrayOf<string> ReadNamespaces(BinaryDecoder decoder)
        {
            ArrayOf<string?> namespaces = decoder.ReadStringArray(null);
            if (namespaces.Count == 0)
            {
                throw new InvalidDataException("The recovery namespace table is missing.");
            }
            var result = new string[namespaces.Count];
            for (int index = 0; index < namespaces.Count; index++)
            {
                result[index] = namespaces[index] ??
                    throw new InvalidDataException("The recovery namespace table contains a null entry.");
            }
            return result;
        }

        /// <summary>
        /// Ensures only a received checkpoint contains certificate and key material.
        /// </summary>
        private static void ValidatePayload(
            PullDcaNewKeyPairRecoveryState state,
            ByteString certificate,
            ArrayOf<ByteString> issuers,
            ReadOnlySpan<byte> pfx)
        {
            bool valid = state switch
            {
                PullDcaNewKeyPairRecoveryState.Dispatching or PullDcaNewKeyPairRecoveryState.Started =>
                    certificate.IsEmpty && pfx.IsEmpty && issuers.Count == 0,
                PullDcaNewKeyPairRecoveryState.Received => !certificate.IsEmpty && !pfx.IsEmpty,
                _ => false
            };
            if (!valid)
            {
                throw new InvalidDataException("The key-pair checkpoint payload is inconsistent.");
            }
            foreach (ByteString issuer in issuers)
            {
                if (issuer.IsEmpty)
                {
                    throw new InvalidDataException("The key-pair checkpoint contains an empty issuer.");
                }
            }
        }

        /// <summary>
        /// Clears private material in a checkpoint that will not be returned.
        /// </summary>
        private static void Clear(PullDcaNewKeyPairRecoveryAttempt? attempt)
        {
            if (attempt is not null)
            {
                PullDcaConnection.ClearPrivateKeyMaterial(attempt.PrivateKeyPfx);
            }
        }

        private const int kMaximumEntryBytes = 16 * 1024 * 1024;

        /// <summary>The protected backend owned by the host.</summary>
        private readonly IProtectedPullDcaRecoveryStore m_store;

        /// <summary>The stable device recovery slot.</summary>
        private readonly SecretIdentifier m_identifier;

        /// <summary>The encoding context for the versioned checkpoint.</summary>
        private readonly IServiceMessageContext m_context;

        /// <summary>Serializes reads and transitions in this adapter.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
