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
