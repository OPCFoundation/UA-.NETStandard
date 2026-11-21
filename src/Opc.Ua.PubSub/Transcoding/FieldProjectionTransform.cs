// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Projects the fields of every DataSetMessage by name: in
    /// <c>include</c> mode only the named fields are kept, re-ordered to
    /// match the supplied name order; in <c>exclude</c> mode the named
    /// fields are dropped and the remaining order is preserved.
    /// </summary>
    /// <remarks>
    /// Field selection and re-ordering per the SubscribedDataSet /
    /// PublishedDataSet field-selection model of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3</see>. Field name matching is ordinal.
    /// </remarks>
    public sealed class FieldProjectionTransform : IPubSubMessageTransform
    {
        private readonly List<string> m_fieldNames;
        private readonly HashSet<string>? m_excludeSet;
        private readonly bool m_exclude;

        /// <summary>
        /// Initializes a new <see cref="FieldProjectionTransform"/>.
        /// </summary>
        /// <param name="fieldNames">
        /// Field names to include (ordered) or exclude.
        /// </param>
        /// <param name="exclude">
        /// <see langword="false"/> (default) to keep only
        /// <paramref name="fieldNames"/> in the given order;
        /// <see langword="true"/> to drop them.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="fieldNames"/> is
        /// <see langword="null"/>.
        /// </exception>
        public FieldProjectionTransform(IEnumerable<string> fieldNames, bool exclude = false)
        {
            if (fieldNames is null)
            {
                throw new ArgumentNullException(nameof(fieldNames));
            }
            m_fieldNames = [.. fieldNames];
            m_exclude = exclude;
            m_excludeSet = exclude
                ? new HashSet<string>(m_fieldNames, StringComparer.Ordinal)
                : null;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TransformAsync(
            PubSubNetworkMessage message,
            TranscodeContext context,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (message.DataSetMessages.Count == 0)
            {
                return new ValueTask<PubSubNetworkMessage?>(message);
            }

            var mapped = new List<PubSubDataSetMessage>(message.DataSetMessages.Count);
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                PubSubDataSetMessage dsm = message.DataSetMessages[i];
                mapped.Add(dsm with { Fields = Project(dsm.Fields) });
            }
            return new ValueTask<PubSubNetworkMessage?>(
                message with { DataSetMessages = mapped });
        }

        private ArrayOf<DataSetField> Project(ArrayOf<DataSetField> fields)
        {
            if (m_exclude)
            {
                var kept = new List<DataSetField>(fields.Count);
                for (int i = 0; i < fields.Count; i++)
                {
                    if (!m_excludeSet!.Contains(fields[i].Name))
                    {
                        kept.Add(fields[i]);
                    }
                }
                return kept;
            }

            var byName = new Dictionary<string, DataSetField>(fields.Count, StringComparer.Ordinal);
            for (int i = 0; i < fields.Count; i++)
            {
                byName[fields[i].Name] = fields[i];
            }
            var ordered = new List<DataSetField>(m_fieldNames.Count);
            for (int i = 0; i < m_fieldNames.Count; i++)
            {
                if (byName.TryGetValue(m_fieldNames[i], out DataSetField? field))
                {
                    ordered.Add(field);
                }
            }
            return ordered;
        }
    }
}
