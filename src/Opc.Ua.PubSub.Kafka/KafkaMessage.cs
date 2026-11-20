// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// One outbound or inbound Kafka record exchanged through the
    /// adapter. Modelled as a <c>readonly record struct</c> so it can be
    /// moved through bounded channels without per-message allocation.
    /// </summary>
    /// <remarks>
    /// Implements the per-message payload envelope used for the JSON and
    /// UADP body mappings of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. <see cref="Key"/>
    /// selects the target partition (records sharing a key preserve
    /// ordering); <see cref="ContentType"/> is emitted as the
    /// <c>content-type</c> record header.
    /// </remarks>
    /// <param name="Topic">
    /// Topic to produce to / topic the record was consumed from.
    /// </param>
    /// <param name="Key">
    /// Partition key bytes. Empty selects round-robin / sticky
    /// partitioning.
    /// </param>
    /// <param name="Value">Raw record value bytes (the encoder's output).</param>
    /// <param name="ContentType">
    /// Value emitted as the <c>content-type</c> record header (e.g.
    /// <c>application/json</c>, <c>application/opcua+uadp</c>).
    /// </param>
    /// <param name="Headers">
    /// Optional additional record headers beyond <see cref="ContentType"/>,
    /// or <see langword="null"/> when none are present.
    /// </param>
    public readonly record struct KafkaMessage(
        string Topic,
        ReadOnlyMemory<byte> Key,
        ReadOnlyMemory<byte> Value,
        string? ContentType,
        IReadOnlyDictionary<string, string>? Headers);
}
