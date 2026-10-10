/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A historical event record stored by an
    /// <see cref="IHistorianEventProvider"/>. Events are keyed by
    /// <see cref="EventId"/> within a notifier and timestamped by
    /// <see cref="SourceTimestamp"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="QualifiedFields"/> preserves the complete
    /// <c>SimpleAttributeOperand</c> identity used for exact projection and
    /// WhereClause evaluation. <see cref="Fields"/> remains as a compatibility
    /// view keyed by the slash-separated browse path.
    /// </para>
    /// </remarks>
    public sealed record HistorianEventRecord(
        ByteString EventId,
        NodeId EventType,
        DateTimeUtc SourceTimestamp,
        ArrayOf<KeyValuePair<string, Variant>> Fields)
    {
        /// <summary>
        /// Event fields keyed by the complete select-clause identity.
        /// </summary>
        public ArrayOf<KeyValuePair<HistorianEventFieldKey, Variant>>
            QualifiedFields
        { get; init; }
            = [];

        /// <summary>
        /// Looks up a legacy browse-path field.
        /// </summary>
        public bool TryGetField(
            string path,
            out Variant value)
        {
            for (int i = 0; i < Fields.Count; i++)
            {
                KeyValuePair<string, Variant> field = Fields[i];
                if (string.Equals(
                    field.Key,
                    path,
                    StringComparison.Ordinal))
                {
                    value = field.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Looks up a field by its complete select-clause identity.
        /// </summary>
        public bool TryGetQualifiedField(
            HistorianEventFieldKey key,
            out Variant value)
        {
            for (int i = 0; i < QualifiedFields.Count; i++)
            {
                KeyValuePair<HistorianEventFieldKey, Variant> field =
                    QualifiedFields[i];
                if (field.Key == key)
                {
                    value = field.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Complete identity of one historical event field.
    /// </summary>
    public readonly record struct HistorianEventFieldKey(
        NodeId TypeDefinitionId,
        ArrayOf<QualifiedName> BrowsePath,
        uint AttributeId,
        string? IndexRange)
    {
        /// <summary>
        /// Creates a key from a select clause.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="operand"/> is <c>null</c>.</exception>
        public static HistorianEventFieldKey FromOperand(
            SimpleAttributeOperand operand)
        {
            if (operand == null)
            {
                throw new ArgumentNullException(nameof(operand));
            }
            return new HistorianEventFieldKey(
                operand.TypeDefinitionId,
                operand.BrowsePath,
                operand.AttributeId,
                operand.IndexRange);
        }

        /// <summary>
        /// Formats the browse path for legacy flat field dictionaries.
        /// </summary>
        public static string BuildPath(ArrayOf<QualifiedName> browsePath)
        {
            if (browsePath.Count == 0)
            {
                return string.Empty;
            }
            if (browsePath.Count == 1)
            {
                return browsePath[0].Name ?? string.Empty;
            }
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < browsePath.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('/');
                }
                builder.Append(browsePath[i].Name);
            }
            return builder.ToString();
        }
    }

    /// <summary>
    /// Validated event-read request envelope passed to
    /// <see cref="IHistorianEventProvider.ReadEventsAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Filter"/> is preserved verbatim. Providers that can
    /// evaluate <c>WhereClause</c> should do so for efficiency; providers
    /// that cannot may return every event in the requested time range
    /// and let the framework evaluate the filter post-fetch.
    /// </para>
    /// </remarks>
    public sealed record HistorianEventReadRequest
    {
        /// <summary>
        /// The notifier (or area) being read.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Effective start time.
        /// </summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>
        /// Effective end time.
        /// </summary>
        public required DateTimeUtc EndTime { get; init; }

        /// <summary>
        /// Maximum events per call. Zero = unbounded.
        /// </summary>
        public uint MaxValues { get; init; }

        /// <summary>
        /// True for forward-in-time reads.
        /// </summary>
        public bool IsForward { get; init; }

        /// <summary>
        /// The event filter from the client request.
        /// </summary>
        public required EventFilter Filter { get; init; }
    }
}
