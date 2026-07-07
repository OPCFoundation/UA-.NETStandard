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
    /// Subscriber-side sink that mirrors a decoded DataSetMessage
    /// into an in-memory key-value cache. Unlike
    /// <see cref="TargetVariablesSink"/> the mirror does not project
    /// to an external address space; it only retains the most recent
    /// <see cref="Variant"/> per field name and raises
    /// <see cref="ValuesChanged"/> after each successful
    /// <see cref="WriteAsync"/>. Callers can read the cache through
    /// <see cref="CurrentValues"/>.
    /// </summary>
    /// <remarks>
    /// Implements the SubscribedDataSetMirror variant of
    /// SubscribedDataSet described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.10">
    /// Part 14 §6.2.10 SubscribedDataSet</see>.
    /// </remarks>
    public sealed class MirroredVariablesSink : ISubscribedDataSetSink
    {
        private readonly Dictionary<string, Variant> m_values =
            new(StringComparer.Ordinal);
        private readonly System.Threading.Lock m_gate = new();

        /// <summary>
        /// Initializes a new <see cref="MirroredVariablesSink"/>.
        /// </summary>
        public MirroredVariablesSink()
        {
        }

        /// <summary>
        /// Initializes a new <see cref="MirroredVariablesSink"/>
        /// using <paramref name="configuration"/>. The configuration
        /// is currently informational; the cache is keyed by field
        /// name.
        /// </summary>
        /// <param name="configuration">Mirror configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown if
        /// <paramref name="configuration"/> is
        /// <see langword="null"/>.</exception>
        public MirroredVariablesSink(
            SubscribedDataSetMirrorDataType configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            Configuration = configuration;
        }

        /// <summary>
        /// Configuration the mirror was initialised with, when one
        /// was supplied. <see langword="null"/> when the default
        /// constructor was used.
        /// </summary>
        public SubscribedDataSetMirrorDataType? Configuration { get; }

        /// <summary>
        /// Snapshot of the current cached values keyed by field
        /// name. The dictionary is independent of subsequent
        /// <see cref="WriteAsync"/> calls.
        /// </summary>
        public IReadOnlyDictionary<string, Variant> CurrentValues
        {
            get
            {
                lock (m_gate)
                {
                    return new Dictionary<string, Variant>(m_values,
                        StringComparer.Ordinal);
                }
            }
        }

        /// <summary>
        /// Raised after a successful <see cref="WriteAsync"/> call
        /// once the cache has been updated. The event payload is the
        /// set of field names that were updated.
        /// </summary>
        public event EventHandler<IReadOnlyList<string>>? ValuesChanged;

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields,
            CancellationToken cancellationToken = default)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            cancellationToken.ThrowIfCancellationRequested();

            var updated = new List<string>(fields.Count);
            lock (m_gate)
            {
                foreach (DataSetField field in fields)
                {
                    if (string.IsNullOrEmpty(field.Name))
                    {
                        continue;
                    }
                    m_values[field.Name] = field.Value;
                    updated.Add(field.Name);
                }
            }
            if (updated.Count > 0)
            {
                ValuesChanged?.Invoke(this, updated);
            }
            return default;
        }
    }
}
