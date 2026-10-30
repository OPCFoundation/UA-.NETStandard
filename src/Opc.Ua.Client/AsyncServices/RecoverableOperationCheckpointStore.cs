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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Persists authenticated-encrypted Client recovery checkpoints with verified durable, conditional commits.
    /// </summary>
    /// <remarks>
    /// The store and protector are caller-owned. Use a distinct key prefix per application recovery store.
    /// Reopening requires the same protector keys, intact storage and a qualified matching type-interpretation witness.
    /// Version 1 records without that witness are rejected. Matching namespace and type IDs alone is insufficient.
    /// Failed writes may have committed; callers must reload and reconcile, never submit again.
    /// No authentication token or user credential is saved. Original request/result data remain encrypted.
    /// </remarks>
    public sealed class RecoverableOperationCheckpointStore
    {
        /// <summary>
        /// Creates directly constructible recovery storage using the existing shared-store and protection contracts.
        /// </summary>
        public RecoverableOperationCheckpointStore(
            ISharedKeyValueStore store, IRecordProtector protector, string keyPrefix = "rse-client/",
            RecoverableExecutionTypeCompatibility? typeCompatibility = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_durability = store as ISharedKeyValueStoreDurability ??
                throw new ArgumentException("Client checkpoints require a durability-qualified store.", nameof(store));
            m_consistency = store as ISharedKeyValueStoreConsistency ??
                throw new ArgumentException(
                    "Client checkpoints require conditional, linearizable storage.", nameof(store));
            if (protector is NullRecordProtector)
            {
                throw new ArgumentException("Client checkpoints require authenticated encryption.", nameof(protector));
            }
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                throw new ArgumentException(
                    "A nonempty, application-specific store prefix is required.", nameof(keyPrefix));
            }
            m_prefix = keyPrefix;
            m_typeCompatibility = typeCompatibility ?? new RecoverableExecutionTypeCompatibility();
        }

        /// <summary>
        /// Lists saved local operation IDs. A missing ID is not evidence that remote work did not execute.
        /// Loading a listed operation separately verifies its protected record and authenticated owner.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ArrayOf<Uuid>> GetOperationIdsAsync(CancellationToken ct = default)
        {
            await m_durability.VerifyDurabilityAsync(ct).ConfigureAwait(false);
            var ids = new List<Uuid>();
            await foreach (KeyValuePair<string, ByteString> record in m_store.ScanAsync(m_prefix, ct)
                .ConfigureAwait(false))
            {
                if (!record.Key.StartsWith(m_prefix, StringComparison.Ordinal) ||
                    !Guid.TryParse(record.Key[m_prefix.Length..], out Guid id) ||
                    id == Guid.Empty)
                {
                    throw InvalidRecord();
                }
                ids.Add(new Uuid(id));
            }
            return ids.ToArrayOf();
        }

        /// <summary>
        /// Commits a new immutable version only when no concurrent collector changed the protected record.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<ByteString> SaveAsync(
            RecoverableOperationCheckpoint checkpoint, ByteString expected,
            IServiceMessageContext context, CancellationToken ct)
        {
            string key = GetKey(checkpoint.ClientOperationId);
            await VerifyAsync(key, ct).ConfigureAwait(false);
            ByteString expectedWitness = expected.IsNull ? default : ReadProtectedWitness(expected, key, context);
            if (!checkpoint.TypeWitness.IsNull &&
                !expectedWitness.IsNull &&
                checkpoint.TypeWitness != expectedWitness)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "A checkpoint update cannot replace its admitted type interpretation.");
            }
            ByteString witness = checkpoint.TypeWitness.IsNull ? expectedWitness : checkpoint.TypeWitness;
            RecoverableExecutionTypeContext types = witness.IsNull
                ? await m_typeCompatibility.CaptureAsync(context, ct).ConfigureAwait(false)
                : await m_typeCompatibility.RestoreAsync(context, witness, ct).ConfigureAwait(false);
            ByteString encoded = Encode(checkpoint, types);
            ByteString protectedRecord = m_protector.Protect(
                RecordProtectionContext.Create(kRecordType, key), encoded);
            if (protectedRecord.IsEmpty || protectedRecord == encoded)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                    "The configured protector did not protect the recovery checkpoint.");
            }
            if (!await m_store.CompareAndSwapAsync(key, expected, protectedRecord, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The recovery checkpoint changed concurrently; reload it before retrying recovery.");
            }
            return protectedRecord;
        }

        /// <summary>
        /// Loads and validates a saved checkpoint without allowing a missing or corrupt record to authorize replay.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<(RecoverableOperationCheckpoint Checkpoint, ByteString Version)> LoadAsync(
            Uuid clientOperationId, IServiceMessageContext context, CancellationToken ct)
        {
            string key = GetKey(clientOperationId);
            await VerifyAsync(key, ct).ConfigureAwait(false);
            (bool found, ByteString protectedRecord) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
            if (!found)
            {
                throw new ServiceResultException(StatusCodes.BadNoData,
                    "No Client recovery checkpoint is available. This does not establish non-execution.");
            }
            if (!m_protector.TryUnprotect(
                RecordProtectionContext.Create(kRecordType, key), protectedRecord, out ByteString plaintext))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                    "The Client recovery checkpoint could not be authenticated.");
            }
            ByteString witness = ReadWitness(plaintext, context);
            RecoverableExecutionTypeContext types = await m_typeCompatibility.RestoreAsync(context, witness, ct)
                .ConfigureAwait(false);
            RecoverableOperationCheckpoint checkpoint = Decode(plaintext, types);
            if (checkpoint.ClientOperationId != clientOperationId)
            {
                throw InvalidRecord();
            }
            return (checkpoint, protectedRecord.Copy());
        }

        /// <summary>
        /// Pins the shared interpretation contract before a persistent operation can be submitted.
        /// </summary>
        internal ValueTask<RecoverableExecutionTypeContext> CaptureTypeContextAsync(
            IServiceMessageContext context, CancellationToken ct)
        {
            return m_typeCompatibility.CaptureAsync(context, ct);
        }

        /// <summary>
        /// Rechecks the live factory, including mutations behind the same facade, without decoding saved payloads.
        /// </summary>
        internal ValueTask<RecoverableExecutionTypeContext> VerifyTypeContextAsync(
            IServiceMessageContext context, ByteString witness, CancellationToken ct)
        {
            return m_typeCompatibility.RestoreAsync(context, witness, ct);
        }

        /// <summary>
        /// Verifies live durability and the exact key's conditional-write qualification before each operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask VerifyAsync(string key, CancellationToken ct)
        {
            if (!m_consistency.IsLinearizable(key))
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable,
                    "The checkpoint key no longer has linearizable conditional-write guarantees.");
            }
            await m_durability.VerifyDurabilityAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Encodes SDK-owned recovery state using existing bounded, NativeAOT-safe OPC UA codecs.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static ByteString Encode(
            RecoverableOperationCheckpoint checkpoint, RecoverableExecutionTypeContext types)
        {
            IServiceMessageContext context = types.MessageContext;
            if (!RecoverableExecutionBinding.MatchesTables(context.NamespaceUris, checkpoint.NamespaceUris) ||
                !RecoverableExecutionBinding.MatchesTables(context.ServerUris, checkpoint.ServerUris))
            {
                throw InvalidRecord();
            }
            IServiceRequest request = CoreUtils.Clone(checkpoint.Request)!;
            request.RequestHeader.AuthenticationToken = NodeId.Null;
            ByteString requestPayload = types.Encode(request);
            ByteString operationPayload = checkpoint.Operation == null ? default : types.Encode(checkpoint.Operation);
            ByteString responsePayload = checkpoint.OriginalResponse == null
                ? default : types.Encode(checkpoint.OriginalResponse);
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteString(null, kRecordType);
                encoder.WriteByteString(null, types.Witness);
                encoder.WriteStringArray(null, checkpoint.NamespaceUris);
                encoder.WriteStringArray(null, checkpoint.ServerUris);
                encoder.WriteGuid(null, checkpoint.ClientOperationId);
                encoder.WriteInt32(null, (int)checkpoint.RecoveryMode);
                encoder.WriteByteString(null, checkpoint.Identity.LogicalServerIdentity);
                encoder.WriteByteString(null, checkpoint.Identity.ApplicationIdentity);
                encoder.WriteByteString(null, checkpoint.Identity.UserIdentity);
                encoder.WriteNodeId(null, checkpoint.SessionId);
                encoder.WriteNodeId(null, checkpoint.ServiceId);
                encoder.WriteDouble(null, checkpoint.ExecutionAllowance);
                encoder.WriteDouble(null, checkpoint.ResultRetentionTime);
                encoder.WriteByteString(null, checkpoint.ServerToken);
                encoder.WriteInt32(null, (int)checkpoint.Acceptance);
                encoder.WriteBoolean(null, checkpoint.RequiresApplicationAcceptance);
                encoder.WriteBoolean(null, checkpoint.RequiresMethodProjection);
                encoder.WriteByteString(null, requestPayload);
                encoder.WriteByteString(null, operationPayload);
                encoder.WriteByteString(null, responsePayload);
            }
            if (context.MaxMessageSize > 0 && stream.Length > context.MaxMessageSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return ByteString.From(stream.ToArray());
        }

        /// <summary>
        /// Rejects changed namespace interpretation before decoding any saved original payload.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static RecoverableOperationCheckpoint Decode(
            ByteString plaintext, RecoverableExecutionTypeContext types)
        {
            IServiceMessageContext context = types.MessageContext;
            if (plaintext.IsEmpty || (context.MaxMessageSize > 0 && plaintext.Length > context.MaxMessageSize))
            {
                throw InvalidRecord();
            }
            using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, context, true);
            if (!string.Equals(decoder.ReadString(null), kRecordType, StringComparison.Ordinal) ||
                decoder.ReadByteString(null) != types.Witness)
            {
                throw InvalidRecord();
            }
            ArrayOf<string?> namespaces = decoder.ReadStringArray(null);
            ArrayOf<string?> servers = decoder.ReadStringArray(null);
            if (!Matches(context.NamespaceUris, namespaces) || !Matches(context.ServerUris, servers))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The checkpoint namespace or Server URI context does not match the activated Session.");
            }
            Uuid id = decoder.ReadGuid(null);
            var mode = (ServiceOperationRecoveryMode)decoder.ReadInt32(null);
            var identity = new RecoverableExecutionIdentity(
                decoder.ReadByteString(null), decoder.ReadByteString(null), decoder.ReadByteString(null));
            NodeId sessionId = decoder.ReadNodeId(null);
            NodeId serviceId = decoder.ReadNodeId(null);
            double execution = decoder.ReadDouble(null);
            double retention = decoder.ReadDouble(null);
            ByteString token = decoder.ReadByteString(null);
            var acceptance = (RecoverableCheckpointAcceptance)decoder.ReadInt32(null);
            bool requiresApplicationAcceptance = decoder.ReadBoolean(null);
            bool requiresMethodProjection = decoder.ReadBoolean(null);
            ByteString requestPayload = decoder.ReadByteString(null);
            ByteString operationPayload = decoder.ReadByteString(null);
            ByteString responsePayload = decoder.ReadByteString(null);
            IServiceRequest request = types.Decode<IServiceRequest>(requestPayload);
            if (id == Uuid.Empty ||
                mode is < ServiceOperationRecoveryMode.Session or > ServiceOperationRecoveryMode.Durable ||
                sessionId.IsNull ||
                serviceId.IsNull ||
                !PositiveFinite(execution) ||
                !PositiveFinite(retention) ||
                (!token.IsNull && token.Length is <= 0 or > 64) ||
                acceptance is < RecoverableCheckpointAcceptance.SubmissionAttempted or
                    > RecoverableCheckpointAcceptance.Acknowledged ||
                request.RequestHeader == null ||
                !request.RequestHeader.AuthenticationToken.IsNull ||
                ExpandedNodeId.ToNodeId(request.TypeId, context.NamespaceUris) != serviceId ||
                stream.Position != stream.Length)
            {
                throw InvalidRecord();
            }
            ServiceOperationInfoDataType? metadata = operationPayload.IsNull
                ? null : types.Decode<ServiceOperationInfoDataType>(operationPayload);
            IServiceResponse? originalResponse = responsePayload.IsNull
                ? null : types.Decode<IServiceResponse>(responsePayload);
            if (acceptance >= RecoverableCheckpointAcceptance.OutcomeValidated &&
                (metadata == null ||
                    metadata.State != ServiceOperationState.OutcomeAvailable ||
                    metadata.OutcomeKind == ServiceOperationOutcome.None ||
                    metadata.OutcomeKind == ServiceOperationOutcome.ServiceResponse !=
                        (originalResponse != null)))
            {
                throw InvalidRecord();
            }
            return new RecoverableOperationCheckpoint(
                id, mode, identity, sessionId, serviceId, context.NamespaceUris.ToArrayOf(),
                context.ServerUris.ToArrayOf(), execution, retention, request, token, metadata, originalResponse,
                acceptance, requiresApplicationAcceptance, requiresMethodProjection, types.Witness);
        }

        /// <summary>
        /// Authenticates an earlier CAS version before trusting its pinned interpretation witness.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ByteString ReadProtectedWitness(
            ByteString protectedRecord, string key, IServiceMessageContext context)
        {
            if (!m_protector.TryUnprotect(
                RecordProtectionContext.Create(kRecordType, key), protectedRecord, out ByteString plaintext))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                    "The previous Client recovery checkpoint could not be authenticated.");
            }
            return ReadWitness(plaintext, context);
        }

        /// <summary>
        /// Reads only primitive format/witness fields. No saved ExtensionObject or original payload is decoded here.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static ByteString ReadWitness(ByteString plaintext, IServiceMessageContext context)
        {
            if (plaintext.IsEmpty || (context.MaxMessageSize > 0 && plaintext.Length > context.MaxMessageSize))
            {
                throw InvalidRecord();
            }
            using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, context, true);
            if (!string.Equals(decoder.ReadString(null), kRecordType, StringComparison.Ordinal))
            {
                throw InvalidRecord();
            }
            return decoder.ReadByteString(null);
        }

        /// <summary>
        /// Checks a persisted table without normalizing or remapping its indices.
        /// </summary>
        private static bool Matches(StringTable actual, ArrayOf<string?> expected)
        {
            if (actual.Count != expected.Count)
            {
                return false;
            }
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i] == null ||
                    !string.Equals(actual.GetString((uint)i), expected[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Validates exact saved budgets without resetting their remote deadlines.
        /// </summary>
        private static bool PositiveFinite(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Binds authentication to the complete application-prefixed operation key.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="id"/></exception>
        private string GetKey(Uuid id)
        {
            if (id == Uuid.Empty)
            {
                throw new ArgumentException("A nonzero Client operation ID is required.", nameof(id));
            }
            return m_prefix + id;
        }

        /// <summary>
        /// Creates a bounded validation error with no secret material.
        /// </summary>
        private static ServiceResultException InvalidRecord()
        {
            return new ServiceResultException(StatusCodes.BadDecodingError, "Invalid Client recovery checkpoint.");
        }

        private const string kRecordType = "OpcUa.RSE.Client/0.4/v2";
        private readonly ISharedKeyValueStore m_store;
        private readonly ISharedKeyValueStoreDurability m_durability;
        private readonly ISharedKeyValueStoreConsistency m_consistency;
        private readonly IRecordProtector m_protector;
        private readonly string m_prefix;
        private readonly RecoverableExecutionTypeCompatibility m_typeCompatibility;
    }

    /// <summary>
    /// Distinguishes uncertain submission, validated data, durable local acceptance and confirmed acknowledgement.
    /// </summary>
    internal enum RecoverableCheckpointAcceptance
    {
        SubmissionAttempted = 1,
        OutcomeValidated = 2,
        Accepted = 3,
        Acknowledged = 4
    }

    /// <summary>
    /// SDK-owned local recovery state, never a competing manager wire structure.
    /// </summary>
    internal sealed record RecoverableOperationCheckpoint(
        Uuid ClientOperationId,
        ServiceOperationRecoveryMode RecoveryMode,
        RecoverableExecutionIdentity Identity,
        NodeId SessionId,
        NodeId ServiceId,
        ArrayOf<string> NamespaceUris,
        ArrayOf<string> ServerUris,
        double ExecutionAllowance,
        double ResultRetentionTime,
        IServiceRequest Request,
        ByteString ServerToken,
        ServiceOperationInfoDataType? Operation,
        IServiceResponse? OriginalResponse,
        RecoverableCheckpointAcceptance Acceptance,
        bool RequiresApplicationAcceptance = false,
        bool RequiresMethodProjection = false,
        ByteString TypeWitness = default);
}
