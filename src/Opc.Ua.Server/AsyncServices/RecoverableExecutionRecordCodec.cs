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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Encodes a bounded, versioned local journal record without adding a wire DataType or executing saved payloads.
    /// </summary>
    internal sealed class RecoverableExecutionRecordCodec
    {
        /// <summary>
        /// Captures the current interpretation witness and existing finite message limit for protected records.
        /// </summary>
        public RecoverableExecutionRecordCodec(IServiceMessageContext context, ByteString witness)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (context.MaxMessageSize <= 0 ||
                context.MaxMessageSize > (int.MaxValue - 8192 - kProtectionOverhead - kEnvelopeHeaderSize) / 4 ||
                witness.Length <= 0 ||
                witness.Length > context.MaxMessageSize)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            m_witness = new ByteString(witness.Span.ToArray());
            MaximumRecordBytes = (4 * context.MaxMessageSize) + 8192;
        }

        /// <summary>
        /// Gets the bounded local record size derived from existing input, result and metadata reservations.
        /// </summary>
        public int MaximumRecordBytes { get; }

        /// <summary>
        /// Gets the maximum reserved protected slot, including bounded protection overhead and framing.
        /// </summary>
        public int MaximumProtectedRecordBytes => MaximumRecordBytes + kProtectionOverhead + kEnvelopeHeaderSize;

        /// <summary>
        /// Reserves the complete future record slot at admission so a later outcome cannot need more store capacity.
        /// Released records retain only their fixed-size history slot.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ByteString Protect(
            RecoverableExecutionDurableRecord record, IRecordProtector protector, ByteString protectionContext)
        {
            ByteString plaintext = Encode(record);
            int capacity = record.IsReleased ? plaintext.Length : (4 * record.MessageLimit) + 8192;
            if (plaintext.Length > capacity)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            ByteString encrypted = protector.Protect(protectionContext, plaintext);
            if (encrypted.Length <= 0 || encrypted.Length > capacity + kProtectionOverhead)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            byte[] envelope = new byte[capacity + kProtectionOverhead + kEnvelopeHeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(envelope, kEnvelopeFormat);
            BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(4), encrypted.Length);
            encrypted.Span.CopyTo(envelope.AsSpan(kEnvelopeHeaderSize));
            return new ByteString(envelope);
        }

        /// <summary>
        /// Authenticates the private record and verifies its reserved slot before returning interpreted metadata.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public RecoverableExecutionDurableRecord Unprotect(
            ByteString envelope, IRecordProtector protector, ByteString protectionContext)
        {
            if (envelope.Length < kEnvelopeHeaderSize ||
                envelope.Length > MaximumProtectedRecordBytes ||
                BinaryPrimitives.ReadUInt32LittleEndian(envelope.Span) != kEnvelopeFormat)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            int length = BinaryPrimitives.ReadInt32LittleEndian(envelope.Span[4..]);
            if (length <= 0 || length > envelope.Length - kEnvelopeHeaderSize)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            foreach (byte padding in envelope.Span[(kEnvelopeHeaderSize + length)..])
            {
                if (padding != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
            }
            var encrypted = new ByteString(envelope.Span.Slice(kEnvelopeHeaderSize, length).ToArray());
            if (!protector.TryUnprotect(protectionContext, encrypted, out ByteString plaintext))
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            RecoverableExecutionDurableRecord record = Decode(plaintext);
            int capacity = record.IsReleased ? plaintext.Length : (4 * record.MessageLimit) + 8192;
            if (envelope.Length != capacity + kProtectionOverhead + kEnvelopeHeaderSize)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            return record;
        }

        /// <summary>
        /// Derives a non-bearer storage key without writing a raw token or owner identifier into the key.
        /// </summary>
        public static string GetKey(string prefix, RecoverableExecutionOwnerId owner, Uuid clientId)
        {
            byte[] key = Encoding.UTF8.GetBytes(
                $"{owner.AuthorityId}|{owner.ApplicationId}|{owner.UserId}|{clientId}");
            return prefix + Convert.ToBase64String(SHA256.HashData(key));
        }

        /// <summary>
        /// Serializes local metadata and opaque payload bytes; the caller must protect the returned record.
        /// </summary>
        public ByteString Encode(RecoverableExecutionDurableRecord record)
        {
            Validate(record);
            using var stream = new RecoverableExecutionBufferStream(MaximumRecordBytes);
            try
            {
                using var encoder = new BinaryEncoder(stream, m_context, leaveOpen: true);
                encoder.WriteUInt32(null, kFormat);
                encoder.WriteByteString(null, record.TypeWitness);
                encoder.WriteGuid(null, record.Owner.Id.AuthorityId);
                encoder.WriteGuid(null, record.Owner.Id.ApplicationId);
                encoder.WriteGuid(null, record.Owner.Id.UserId);
                encoder.WriteByteString(null, record.Owner.ContinuityContext);
                encoder.WriteGuid(null, record.ClientId);
                encoder.WriteByteString(null, record.Token);
                encoder.WriteGuid(null, record.Incarnation);
                encoder.WriteNodeId(null, record.IssuingSessionId);
                encoder.WriteNodeId(null, record.ServiceId);
                encoder.WriteInt32(null, record.MessageLimit);
                encoder.WriteInt32(null, (int)record.Mapping);
                encoder.WriteInt64(null, record.Execution.Ticks);
                encoder.WriteInt64(null, record.Retention.Ticks);
                encoder.WriteInt32(null, (int)record.Phase);
                encoder.WriteInt32(null, (int)record.State);
                encoder.WriteInt32(null, (int)record.Kind);
                encoder.WriteStatusCode(null, record.Status);
                WriteTime(encoder, record.AdmittedAt);
                WriteTime(encoder, record.OutcomeAt);
                WriteTime(encoder, record.ReleasedAt);
                encoder.WriteBoolean(null, record.DispatchStarted);
                encoder.WriteBoolean(null, record.WorkFinished);
                encoder.WriteBoolean(null, record.CancellationRequested);
                encoder.WriteUInt64(null, record.Revision);
                encoder.WriteUInt32(null, (uint)record.DiagnosticsMask);
                encoder.WriteInt32(null, record.Roles.Count);
                foreach (NodeId role in record.Roles)
                {
                    encoder.WriteNodeId(null, role);
                }
                encoder.WriteInt32(null, record.Permissions.Count);
                foreach (RecoverableNodePermission permission in record.Permissions)
                {
                    encoder.WriteNodeId(null, permission.NodeId);
                    encoder.WriteUInt32(null, (uint)permission.Permission);
                }
                encoder.WriteByteString(null, record.Request);
                encoder.WriteByteString(null, record.Response);
                _ = encoder.Close();
                return new ByteString(stream.ToArray());
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadResponseTooLarge)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
            }
            finally
            {
                Array.Clear(stream.GetBuffer(), 0, stream.Capacity);
            }
        }

        /// <summary>
        /// Verifies the local format and interpretation witness before reading saved NodeIds or any payload.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public RecoverableExecutionDurableRecord Decode(ByteString encoded)
        {
            if (encoded.Length <= 0 || encoded.Length > MaximumRecordBytes)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            using var decoder = new BinaryDecoder(encoded.Span.ToArray(), m_context);
            if (decoder.ReadUInt32(null) != kFormat || decoder.ReadByteString(null) != m_witness)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            var ownerId = new RecoverableExecutionOwnerId(
                decoder.ReadGuid(null), decoder.ReadGuid(null), decoder.ReadGuid(null));
            var owner = new RecoverableExecutionOwner(ownerId, decoder.ReadByteString(null));
            var record = new RecoverableExecutionDurableRecord
            {
                Owner = owner,
                ClientId = decoder.ReadGuid(null),
                Token = decoder.ReadByteString(null),
                Incarnation = decoder.ReadGuid(null),
                IssuingSessionId = decoder.ReadNodeId(null),
                ServiceId = decoder.ReadNodeId(null),
                MessageLimit = decoder.ReadInt32(null),
                Mapping = (RequestEncoding)decoder.ReadInt32(null),
                Execution = TimeSpan.FromTicks(decoder.ReadInt64(null)),
                Retention = TimeSpan.FromTicks(decoder.ReadInt64(null)),
                Phase = (RecoverableExecutionCommitPhase)decoder.ReadInt32(null),
                State = (ServiceOperationState)decoder.ReadInt32(null),
                Kind = (ServiceOperationOutcome)decoder.ReadInt32(null),
                Status = decoder.ReadStatusCode(null),
                AdmittedAt = ReadTime(decoder),
                OutcomeAt = ReadTime(decoder),
                ReleasedAt = ReadTime(decoder),
                DispatchStarted = decoder.ReadBoolean(null),
                WorkFinished = decoder.ReadBoolean(null),
                CancellationRequested = decoder.ReadBoolean(null),
                Revision = decoder.ReadUInt64(null),
                DiagnosticsMask = (DiagnosticsMasks)decoder.ReadUInt32(null),
                TypeWitness = m_witness,
                Roles = ReadRoles(decoder),
                Permissions = ReadPermissions(decoder),
                Request = decoder.ReadByteString(null),
                Response = decoder.ReadByteString(null)
            };
            if (decoder.Position != encoded.Length)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            Validate(record);
            return record;
        }

        /// <summary>
        /// Rejects invalid phases and inconsistent ownership, outcome, time and size facts instead of repairing them.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void Validate(RecoverableExecutionDurableRecord record)
        {
            if (record.Owner == null ||
                record.ClientId == Uuid.Empty ||
                record.Incarnation == Uuid.Empty ||
                record.Owner.Id.AuthorityId == Uuid.Empty ||
                record.Owner.Id.ApplicationId == Uuid.Empty ||
                record.Owner.Id.UserId == Uuid.Empty ||
                record.Token.Length is <= 0 or > 64 ||
                record.Owner.ContinuityContext.Length <= 0 ||
                record.TypeWitness != m_witness ||
                record.ServiceId.IsNull ||
                record.ServiceId.NamespaceIndex != 0 ||
                record.MessageLimit <= 0 ||
                record.MessageLimit > m_context.MaxMessageSize ||
                record.Revision == 0 ||
                record.Roles.Count > m_context.MaxMessageSize / 32 ||
                record.Permissions.Count > m_context.MaxMessageSize / 32 ||
                record.Request.Length > record.MessageLimit ||
                record.Response.Length > record.MessageLimit ||
                record.Owner.ContinuityContext.Length > record.MessageLimit ||
                record.Execution <= TimeSpan.Zero ||
                record.Execution.Ticks > int.MaxValue * TimeSpan.TicksPerMillisecond ||
                record.Retention <= TimeSpan.Zero ||
                record.Retention.Ticks > int.MaxValue * TimeSpan.TicksPerMillisecond ||
                record.Mapping is not (RequestEncoding.Binary or RequestEncoding.Json) ||
                record.Phase is <
                    RecoverableExecutionCommitPhase.Admission or > RecoverableExecutionCommitPhase.Released)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            switch (record.Phase)
            {
                case RecoverableExecutionCommitPhase.Admission:
                    Require(record.State == ServiceOperationState.Pending &&
                        record.Kind == ServiceOperationOutcome.None &&
                        record.Status == StatusCodes.Good &&
                        !record.DispatchStarted &&
                        !record.WorkFinished &&
                        record.AdmittedAt == default &&
                        record.OutcomeAt == default &&
                        record.ReleasedAt == default &&
                        record.Response.IsNull &&
                        record.Request.Length > 0);
                    break;
                case RecoverableExecutionCommitPhase.Pending:
                    Require(record.State == ServiceOperationState.Pending &&
                        record.Kind == ServiceOperationOutcome.None &&
                        record.Status == StatusCodes.Good &&
                        record.OutcomeAt == default &&
                        record.ReleasedAt == default &&
                        record.Response.IsNull &&
                        record.Request.Length > 0);
                    ValidateTime(record.AdmittedAt, record.Execution);
                    break;
                case RecoverableExecutionCommitPhase.OutcomePreparing:
                    Require(record.State == ServiceOperationState.Pending &&
                        record.OutcomeAt == default &&
                        record.ReleasedAt == default &&
                        record.Request.Length > 0);
                    ValidateOutcome(record);
                    break;
                case RecoverableExecutionCommitPhase.OutcomeAvailable:
                    Require(record.State == ServiceOperationState.OutcomeAvailable &&
                        record.ReleasedAt == default &&
                        record.Request.Length > 0);
                    ValidateOutcome(record);
                    ValidateTime(record.OutcomeAt, record.Retention);
                    break;
                case RecoverableExecutionCommitPhase.Released:
                    Require(record.IsReleased &&
                        record.Response.IsNull &&
                        record.Request.IsNull &&
                        record.Permissions.Count == 0 &&
                        record.Kind is >=
                            ServiceOperationOutcome.None and <= ServiceOperationOutcome.ExecutionIndeterminate);
                    ValidateTime(record.ReleasedAt, record.Retention);
                    break;
            }
            Uuid domain = record.AdmittedAt.DomainId;
            foreach (RecoverableExecutionTime time in new[]
            {
                record.AdmittedAt, record.OutcomeAt, record.ReleasedAt
            })
            {
                if (time.DomainId != Uuid.Empty)
                {
                    Require(domain == Uuid.Empty || domain == time.DomainId);
                    domain = time.DomainId;
                }
            }
            if (record.AdmittedAt.DomainId != Uuid.Empty && record.OutcomeAt.DomainId != Uuid.Empty)
            {
                Require(record.OutcomeAt.Ticks >= record.AdmittedAt.Ticks);
            }
            if (record.ReleasedAt.DomainId != Uuid.Empty)
            {
                Require(record.ReleasedAt.Ticks >= record.AdmittedAt.Ticks &&
                    record.ReleasedAt.Ticks >= record.OutcomeAt.Ticks);
                if (record.State is ServiceOperationState.Acknowledged or ServiceOperationState.Expired)
                {
                    Require(record.Kind != ServiceOperationOutcome.None && record.OutcomeAt.DomainId != Uuid.Empty);
                }
                if (record.State == ServiceOperationState.Expired)
                {
                    ValidateTime(record.OutcomeAt, record.Retention);
                    Require(record.ReleasedAt.Ticks == record.OutcomeAt.Ticks + record.Retention.Ticks);
                }
            }
            foreach (NodeId role in record.Roles)
            {
                Require(!role.IsNull);
            }
            var permissions = new HashSet<RecoverableNodePermission>();
            foreach (RecoverableNodePermission permission in record.Permissions)
            {
                Require(!permission.NodeId.IsNull && permissions.Add(permission));
            }
        }

        private static void ValidateOutcome(RecoverableExecutionDurableRecord record)
        {
            Require(record.Kind is >= ServiceOperationOutcome.ServiceResponse and <=
                ServiceOperationOutcome.ExecutionIndeterminate);
            Require(record.Kind == ServiceOperationOutcome.ServiceResponse
                ? record.Response.Length > 0 : record.Response.IsNull);
            if (record.Kind == ServiceOperationOutcome.ExecutionDeadlineExceeded)
            {
                Require(record.Status == StatusCodes.BadTimeout);
            }
            else if (record.Kind == ServiceOperationOutcome.ExecutionIndeterminate)
            {
                Require(record.Status == StatusCodes.BadUnexpectedError);
            }
            else if (record.Kind == ServiceOperationOutcome.ResultUnavailable)
            {
                Require(
                    record.Status == StatusCodes.BadResponseTooLarge || record.Status == StatusCodes.BadEncodingError);
            }
        }

        private static void ValidateTime(RecoverableExecutionTime time, TimeSpan duration)
        {
            _ = time.ElapsedUntil(time);
            Require(time.Ticks <= long.MaxValue - duration.Ticks);
        }

        private static void WriteTime(BinaryEncoder encoder, RecoverableExecutionTime time)
        {
            encoder.WriteGuid(null, time.DomainId);
            encoder.WriteInt64(null, time.Ticks);
        }

        private static RecoverableExecutionTime ReadTime(BinaryDecoder decoder)
        {
            Uuid domain = decoder.ReadGuid(null);
            long ticks = decoder.ReadInt64(null);
            if (domain == Uuid.Empty)
            {
                Require(ticks == 0);
                return default;
            }
            return new RecoverableExecutionTime(domain, ticks);
        }

        private ArrayOf<NodeId> ReadRoles(BinaryDecoder decoder)
        {
            int count = ReadCount(decoder);
            var roles = new NodeId[count];
            for (int i = 0; i < roles.Length; i++)
            {
                roles[i] = decoder.ReadNodeId(null);
            }
            return roles;
        }

        private ArrayOf<RecoverableNodePermission> ReadPermissions(BinaryDecoder decoder)
        {
            int count = ReadCount(decoder);
            var permissions = new RecoverableNodePermission[count];
            for (int i = 0; i < permissions.Length; i++)
            {
                permissions[i] = new RecoverableNodePermission(
                    decoder.ReadNodeId(null), (PermissionType)decoder.ReadUInt32(null));
            }
            return permissions;
        }

        private int ReadCount(BinaryDecoder decoder)
        {
            int count = decoder.ReadInt32(null);
            Require(count >= 0 && count <= m_context.MaxMessageSize / 32);
            return count;
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
        }

        private readonly IServiceMessageContext m_context;
        private readonly ByteString m_witness;
        private const uint kFormat = 0x52530401;
        private const uint kEnvelopeFormat = 0x52534501;
        private const int kProtectionOverhead = 4096;
        private const int kEnvelopeHeaderSize = 8;
    }
}
