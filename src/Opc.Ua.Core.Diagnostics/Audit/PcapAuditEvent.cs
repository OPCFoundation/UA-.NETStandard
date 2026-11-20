/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Pcap.Audit
{
    /// <summary>
    /// Describes one security-sensitive Pcap binding operation. The kind
    /// identifies the operation; the timestamp is the UTC event time; the
    /// optional session id correlates manager and MCP tool events; the
    /// optional resource path names the pcap, keylog, or session folder; the
    /// optional remote endpoint records a peer, listener, or replay target;
    /// and properties hold additional non-secret structured metadata.
    /// </summary>
    public sealed record class PcapAuditEvent
    {
        /// <summary>
        /// Constructs a Pcap audit event.
        /// </summary>
        public PcapAuditEvent(
            PcapAuditEventKind kind,
            DateTimeOffset timestamp,
            string? sessionId,
            string? resourcePath,
            string? remoteEndpoint,
            IReadOnlyDictionary<string, string>? properties)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Audit event kind must be defined.");
            }

            if (timestamp == default)
            {
                throw new ArgumentException("Audit event timestamp must be set.", nameof(timestamp));
            }

            Kind = kind;
            Timestamp = timestamp;
            SessionId = sessionId;
            ResourcePath = resourcePath;
            RemoteEndpoint = remoteEndpoint;
            Properties = properties;
        }

        /// <summary>
        /// Gets the security-sensitive operation category.
        /// </summary>
        public PcapAuditEventKind Kind { get; init; }

        /// <summary>
        /// Gets the UTC time when the event occurred.
        /// </summary>
        public DateTimeOffset Timestamp { get; init; }

        /// <summary>
        /// Gets the capture or replay session id, when one exists.
        /// </summary>
        public string? SessionId { get; init; }

        /// <summary>
        /// Gets the pcap, keylog, or session-folder path involved in the operation.
        /// </summary>
        public string? ResourcePath { get; init; }

        /// <summary>
        /// Gets the peer, listener, or replay target endpoint involved in the operation.
        /// </summary>
        public string? RemoteEndpoint { get; init; }

        /// <summary>
        /// Gets non-secret structured metadata for the operation.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Properties { get; init; }
    }
}
