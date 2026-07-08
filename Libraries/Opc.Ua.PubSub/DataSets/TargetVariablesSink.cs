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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Subscriber-side sink that materialises a decoded
    /// DataSetMessage into a host's address space using a
    /// <see cref="TargetVariablesDataType"/> configuration. For each
    /// inbound field the sink resolves the matching
    /// <see cref="FieldTargetDataType"/> entry (positionally) and
    /// applies the (possibly overridden) <see cref="DataValue"/> to
    /// the configured node attribute through the injected
    /// <see cref="ITargetVariableWriter"/>. Override semantics are
    /// delegated to
    /// <see cref="OverrideValueHandlingResolver"/>.
    /// </summary>
    /// <remarks>
    /// Implements the TargetVariables variant of SubscribedDataSet
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.10">
    /// Part 14 §6.2.10 SubscribedDataSet</see>.
    /// </remarks>
    public sealed class TargetVariablesSink : ISubscribedDataSetSink
    {
        private readonly ITargetVariableWriter m_writer;
        private readonly ArrayOf<FieldTargetDataType> m_targets;
        private readonly DataValue[] m_lastGood;
        private readonly Lock m_gate = new();

        /// <summary>
        /// Initializes a new <see cref="TargetVariablesSink"/>.
        /// </summary>
        /// <param name="configuration">TargetVariables configuration
        /// holding the per-field
        /// <see cref="FieldTargetDataType"/> entries.</param>
        /// <param name="writer">Pluggable provider used to apply each
        /// resolved <see cref="DataValue"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown if either
        /// argument is <see langword="null"/>.</exception>
        public TargetVariablesSink(
            TargetVariablesDataType configuration,
            ITargetVariableWriter writer)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_writer = writer ?? throw new ArgumentNullException(nameof(writer));
            m_targets = configuration.TargetVariables;
            m_lastGood = new DataValue[m_targets.Count];
        }

        /// <summary>
        /// Number of target slots configured on this sink.
        /// </summary>
        public int TargetCount => m_targets.Count;

        /// <inheritdoc/>
        public async ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields,
            CancellationToken cancellationToken = default)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            cancellationToken.ThrowIfCancellationRequested();

            int count = m_targets.Count;
            var resolved = new (FieldTargetDataType Target, DataValue Value, int Index)[count];
            for (int i = 0; i < count; i++)
            {
                FieldTargetDataType target = m_targets[i];
                DataSetField? incoming = FindField(fields, target, i);
                DataValue lastGood;
                lock (m_gate)
                {
                    lastGood = m_lastGood[i];
                }
                DataValue effective = OverrideValueHandlingResolver.Resolve(
                    target.OverrideValueHandling,
                    target.OverrideValue,
                    incoming,
                    lastGood);
                resolved[i] = (target, effective, i);
            }

            for (int i = 0; i < count; i++)
            {
                (FieldTargetDataType target, DataValue value, int idx) = resolved[i];
                if (value.IsNull)
                {
                    continue;
                }
                StatusCode status = await m_writer.WriteAsync(
                    target.TargetNodeId,
                    target.AttributeId,
                    target.WriteIndexRange,
                    value,
                    cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsGood(status))
                {
                    lock (m_gate)
                    {
                        m_lastGood[idx] = value;
                    }
                }
            }
        }

        private static DataSetField? FindField(
            IReadOnlyList<DataSetField> fields,
            FieldTargetDataType target,
            int positionalIndex)
        {
            if (!target.DataSetFieldId.IsNullOrEmpty())
            {
                string fieldIdText = target.DataSetFieldId.ToString();
                for (int j = 0; j < fields.Count; j++)
                {
                    if (string.Equals(fields[j].Name, fieldIdText, StringComparison.Ordinal))
                    {
                        return fields[j];
                    }
                }
            }
            if (positionalIndex >= 0 && positionalIndex < fields.Count)
            {
                return fields[positionalIndex];
            }
            return null;
        }
    }

    internal static class UuidExtensions
    {
        public static bool IsNullOrEmpty(this Uuid value)
        {
            return value == Uuid.Empty;
        }
    }
}
