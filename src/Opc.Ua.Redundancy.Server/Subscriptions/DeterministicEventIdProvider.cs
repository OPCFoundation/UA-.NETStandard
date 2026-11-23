// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Optional EventId synchronization provider for Transparent and HotAndMirrored redundancy.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 §6.6.2.2 requires EventIds to be synchronized for Transparent and HotAndMirrored
    /// <c>RedundantServerSet</c>s so clients do not double-process events after Failover. This provider is
    /// deterministic for the same replica-set seed, event type, source, condition and branch IDs, full-resolution
    /// source timestamp, severity, and message. The automatic path excludes per-replica <c>ReceiveTime</c>
    /// and the routing notifier. If these occurrence fields cannot
    /// uniquely identify each shared transition, supply an explicit application-level EventId.
    /// The fluent overload retains its original hash format for existing explicit registrations.
    /// </remarks>
    public sealed class DeterministicEventIdProvider : IEventIdProvider, IEventOccurrenceIdProvider
    {
        /// <summary>
        /// Creates a deterministic event id provider.
        /// </summary>
        /// <param name="replicaSetSeed">
        /// A stable, non-secret seed shared by all replicas in the transparent set.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="replicaSetSeed"/> is empty.</exception>
        public DeterministicEventIdProvider(string replicaSetSeed)
        {
            if (string.IsNullOrWhiteSpace(replicaSetSeed))
            {
                throw new ArgumentException("A replica-set seed is required.", nameof(replicaSetSeed));
            }

            m_replicaSetSeed = replicaSetSeed;
        }

        /// <inheritdoc/>
        public ByteString CreateEventId(BaseObjectState notifier, ISystemContext context, BaseEventState eventState)
        {
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (eventState == null)
            {
                throw new ArgumentNullException(nameof(eventState));
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendLegacyField(hash, m_replicaSetSeed);
            AppendLegacyField(hash, notifier.NodeId.ToString());
            AppendLegacyField(hash,
                (eventState.EventType?.Value ?? eventState.GetDefaultTypeDefinitionId(context)).ToString());
            AppendLegacyField(hash, (eventState.SourceNode?.Value ?? notifier.NodeId).ToString());
            AppendLegacyField(hash, eventState.Time?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendLegacyField(hash, eventState.Severity?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendLegacyField(hash, eventState.Message?.Value.Text ?? string.Empty);
            return ByteString.From(hash.GetHashAndReset());
        }

        /// <inheritdoc/>
        public ByteString CreateEventId(ISystemContext context, BaseEventState eventState)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (eventState == null)
            {
                throw new ArgumentNullException(nameof(eventState));
            }

            NodeId type = eventState.EventType?.Value ?? default;
            NodeId source = eventState.SourceNode?.Value ?? default;
            var condition = eventState as ConditionState;
            NodeId branch = condition?.BranchId?.Value ?? default;
            NodeId conditionId = condition?.NodeId ?? default;
            if (!branch.IsNull && condition?.Parent is ConditionState root)
            {
                conditionId = root.NodeId;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendField(hash, "opcua-event-id-v1");
            AppendField(hash, m_replicaSetSeed);
            AppendNodeId(hash, context, type.IsNull ? eventState.GetDefaultTypeDefinitionId(context) : type);
            AppendNodeId(hash, context, source);
            AppendField(hash, source.IsNull ? eventState.SourceName?.Value ?? string.Empty : string.Empty);
            AppendNodeId(hash, context, conditionId);
            AppendNodeId(hash, context, branch);
            DateTimeUtc time = eventState.Time?.Value ?? default;
            AppendField(hash, time.Value.ToString(CultureInfo.InvariantCulture));
            AppendField(hash, eventState.Severity?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(hash, eventState.Message?.Value.Text ?? string.Empty);
            return ByteString.From(hash.GetHashAndReset());
        }

        private static void AppendNodeId(IncrementalHash hash, ISystemContext context, NodeId id)
        {
            string? uri = context.NamespaceUris.GetString(id.NamespaceIndex);
            AppendField(hash, uri == null
                ? "index:" + id.NamespaceIndex.ToString(CultureInfo.InvariantCulture)
                : "uri:" + uri);
            AppendField(hash, id.WithNamespaceIndex(0).Format(CultureInfo.InvariantCulture));
        }

        private static void AppendField(IncrementalHash hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            // Length-prefix so distinct field boundaries cannot collide.
            byte[] length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        private static void AppendLegacyField(IncrementalHash hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }

        private readonly string m_replicaSetSeed;
    }
}
