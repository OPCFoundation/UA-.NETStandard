// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
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
    /// deterministic for the same replica-set seed, notifier, event type, source, source timestamp, severity, and
    /// message. It intentionally excludes per-replica <c>ReceiveTime</c>. Events that lack stable distinguishing
    /// fields should set <c>EventId</c> explicitly or use a stronger application-level event identity.
    /// </remarks>
    public sealed class DeterministicEventIdProvider : IEventIdProvider
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

            // Hash the length-prefixed field bytes incrementally rather than
            // building one large string. SHA-256 is kept deliberately: an EventId
            // collision would make replicas emit the same id for distinct events
            // and cause clients to drop a real event, so collision resistance
            // matters more than shaving the per-event hash cost.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendField(hash, m_replicaSetSeed);
            AppendField(hash, notifier.NodeId.ToString());
            AppendField(hash, (eventState.EventType?.Value ?? eventState.GetDefaultTypeDefinitionId(context)).ToString());
            AppendField(hash, (eventState.SourceNode?.Value ?? notifier.NodeId).ToString());
            AppendField(hash, eventState.Time?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(hash, eventState.Severity?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AppendField(hash, eventState.Message?.Value.Text ?? string.Empty);
            return ByteString.From(hash.GetHashAndReset());
        }

        private static void AppendField(IncrementalHash hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            // Length-prefix so distinct field boundaries cannot collide.
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }

        private readonly string m_replicaSetSeed;
    }
}
