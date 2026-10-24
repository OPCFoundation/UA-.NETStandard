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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Accumulates the field observations of one DataSet schema lineage so that the Variant and
    /// ExtensionObject unions grow <em>append-only</em> across MinorVersions (§5.8, §6.4).
    /// </summary>
    /// <remarks>
    /// This state is what makes a schema <em>grow</em> rather than be replaced. Building each
    /// schema only from the values in the message at hand would produce a union whose branches are
    /// reordered - or dropped - whenever a Variant field carries a different body type, which
    /// silently changes the branch index of every existing branch and makes previously written
    /// values undecodable under the newer schema. Retaining the first-seen order and only ever
    /// appending keeps every existing branch at its index, which is exactly what §5.8 requires.
    /// </remarks>
    internal sealed class AvroSchemaLineage
    {
        private readonly Dictionary<string, List<AvroSchemaField>> _lineages =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Merges the fields observed in the current message into the lineage and returns the
        /// accumulated observation set.
        /// </summary>
        /// <param name="lineageKey">The key identifying the DataSet lineage.</param>
        /// <param name="observed">The fields observed in the current message.</param>
        /// <returns>The accumulated field observations, in first-seen order.</returns>
        public IReadOnlyList<AvroSchemaField> Accumulate(
            string lineageKey,
            IReadOnlyList<AvroSchemaField> observed)
        {
            if (!_lineages.TryGetValue(lineageKey, out List<AvroSchemaField>? accumulated))
            {
                accumulated = new List<AvroSchemaField>(observed.Count);
                _lineages[lineageKey] = accumulated;
            }

            for (int i = 0; i < observed.Count; i++)
            {
                AvroSchemaField candidate = observed[i];
                if (!Contains(accumulated, candidate))
                {
                    accumulated.Add(candidate);
                }
            }
            return accumulated;
        }

        /// <summary>
        /// Forgets every accumulated lineage, so the next schema starts a new MajorVersion (§6.4).
        /// </summary>
        public void Reset()
        {
            _lineages.Clear();
        }

        private static bool Contains(List<AvroSchemaField> accumulated, AvroSchemaField candidate)
        {
            for (int i = 0; i < accumulated.Count; i++)
            {
                AvroSchemaField existing = accumulated[i];
                if (existing.BuiltInType == candidate.BuiltInType
                    && existing.ValueRank == candidate.ValueRank
                    && existing.Encoding == candidate.Encoding
                    && string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
