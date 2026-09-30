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
using System.Collections;
using System.Collections.Generic;

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// The values read from one OPC 40223 group, keyed by browse name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The large OPC 40223 groups are open sets of optional variables -
    /// <c>DesignType</c> alone declares 78, <c>MeasurementsType</c> 50,
    /// <c>SystemRequirementsType</c> 37 - and a real pump publishes a handful
    /// of them. A record with one property per variable would therefore be
    /// mostly <see langword="null"/>, and would have to be revised on every
    /// revision of the specification. This set is filled from what the server
    /// actually publishes instead, so it covers the whole group by
    /// construction.
    /// </para>
    /// <para>
    /// Keys are browse names without namespace index, which is what the
    /// generated <see cref="BrowseNames"/> constants carry - use those rather
    /// than string literals, so a rename in the model breaks the build instead
    /// of silently returning nothing.
    /// </para>
    /// </remarks>
    public sealed class PumpValueSet : IReadOnlyCollection<PumpValue>
    {
        /// <summary>An empty set.</summary>
        public static readonly PumpValueSet Empty = new(NodeId.Null, []);

        private readonly Dictionary<string, PumpValue> m_values;
        private readonly Dictionary<string, PumpValueSet> m_groups;

        /// <summary>
        /// Creates a value set for the group rooted at <paramref name="nodeId"/>.
        /// </summary>
        /// <param name="nodeId">The group object the values were read from.</param>
        /// <param name="values">The values read.</param>
        public PumpValueSet(NodeId nodeId, IEnumerable<PumpValue> values)
            : this(nodeId, values, [])
        {
        }

        /// <summary>
        /// Creates a value set that also carries the groups nested below it.
        /// </summary>
        /// <param name="nodeId">The group object the values were read from.</param>
        /// <param name="values">The values read.</param>
        /// <param name="groups">
        /// The nested groups, keyed by browse name - the vibration
        /// measurements below <c>Measurements</c>, for instance.
        /// </param>
        public PumpValueSet(
            NodeId nodeId,
            IEnumerable<PumpValue> values,
            IEnumerable<KeyValuePair<string, PumpValueSet>> groups)
        {
            if (groups is null)
            {
                throw new ArgumentNullException(nameof(groups));
            }
            m_groups = new Dictionary<string, PumpValueSet>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, PumpValueSet> group in groups)
            {
                m_groups[group.Key] = group.Value;
            }
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            NodeId = nodeId;
            m_values = new Dictionary<string, PumpValue>(StringComparer.Ordinal);
            foreach (PumpValue value in values)
            {
                // Last one wins: a group that somehow publishes the same browse
                // name twice is malformed, and throwing here would make the
                // whole read fail over one duplicate.
                m_values[value.Name] = value;
            }
            Names = m_values.Keys.ToArrayOf();
        }

        /// <summary>
        /// Gets the group object the values were read from, or
        /// <see cref="NodeId.Null"/> for <see cref="Empty"/>.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Gets how many values the group published.
        /// </summary>
        public int Count => m_values.Count;

        /// <summary>
        /// Gets whether the group published nothing.
        /// </summary>
        public bool IsEmpty => m_values.Count == 0;

        /// <summary>
        /// Gets the browse names the group published.
        /// </summary>
        public ArrayOf<string> Names { get; }

        /// <summary>
        /// Gets the objects nested below the group that carry values of their
        /// own, keyed by browse name - for example the
        /// <c>VibrationMeasurementType</c> instances an OPC 40223
        /// <c>Measurements</c> group holds through its <c>&lt;Vibration&gt;</c>
        /// placeholder.
        /// </summary>
        public IReadOnlyDictionary<string, PumpValueSet> Groups => m_groups;

        /// <summary>
        /// Gets the nested group published under
        /// <paramref name="browseName"/>, or <see langword="null"/>.
        /// </summary>
        /// <param name="browseName">The nested object's browse name.</param>
        public PumpValueSet? Group(string browseName)
        {
            if (browseName is null)
            {
                throw new ArgumentNullException(nameof(browseName));
            }
            return m_groups.TryGetValue(browseName, out PumpValueSet? group) ? group : null;
        }

        /// <summary>
        /// Gets the value published under <paramref name="browseName"/>, or
        /// <see langword="null"/> when the group does not publish it.
        /// </summary>
        /// <param name="browseName">
        /// A browse name, normally one of the generated
        /// <see cref="BrowseNames"/> constants.
        /// </param>
        public PumpValue? this[string browseName]
        {
            get
            {
                if (browseName is null)
                {
                    throw new ArgumentNullException(nameof(browseName));
                }
                return m_values.TryGetValue(browseName, out PumpValue? value) ? value : null;
            }
        }

        /// <summary>
        /// Gets whether the group published <paramref name="browseName"/>.
        /// </summary>
        /// <param name="browseName">The browse name to probe.</param>
        public bool Contains(string browseName)
        {
            return browseName is not null && m_values.ContainsKey(browseName);
        }

        /// <summary>
        /// Gets the numeric reading published under
        /// <paramref name="browseName"/>, or <see langword="null"/>.
        /// </summary>
        /// <param name="browseName">The browse name to read.</param>
        public double? GetDouble(string browseName)
        {
            return this[browseName]?.AsDouble();
        }

        /// <summary>
        /// Gets the boolean published under <paramref name="browseName"/>, or
        /// <see langword="null"/>.
        /// </summary>
        /// <param name="browseName">The browse name to read.</param>
        public bool? GetBoolean(string browseName)
        {
            return this[browseName]?.AsBoolean();
        }

        /// <summary>
        /// Gets the text published under <paramref name="browseName"/>, or
        /// <see langword="null"/>.
        /// </summary>
        /// <param name="browseName">The browse name to read.</param>
        public string? GetString(string browseName)
        {
            return this[browseName]?.AsString();
        }

        /// <summary>
        /// Gets the string array published under
        /// <paramref name="browseName"/>, or <see cref="ArrayOf{T}.Null"/>.
        /// </summary>
        /// <param name="browseName">The browse name to read.</param>
        public ArrayOf<string> GetStringArray(string browseName)
        {
            PumpValue? value = this[browseName];
            return value is null ? ArrayOf<string>.Null : value.AsStringArray();
        }

        /// <summary>
        /// Gets the unsigned integer published under
        /// <paramref name="browseName"/>, or <see langword="null"/>.
        /// </summary>
        /// <param name="browseName">The browse name to read.</param>
        public uint? GetUInt32(string browseName)
        {
            return this[browseName]?.AsUInt32();
        }

        /// <summary>
        /// Gets the UTC date published under <paramref name="browseName"/>, or
        /// <see langword="null"/>.
        /// </summary>
        /// <param name="browseName">The browse name to read.</param>
        public DateTime? GetDateTime(string browseName)
        {
            return this[browseName]?.AsDateTime();
        }

        /// <summary>
        /// Gets the enumeration published under <paramref name="browseName"/>,
        /// or <see langword="null"/>.
        /// </summary>
        /// <typeparam name="TEnum">The generated OPC 40223 enumeration.</typeparam>
        /// <param name="browseName">The browse name to read.</param>
        public TEnum? GetEnum<TEnum>(string browseName) where TEnum : struct, Enum
        {
            return this[browseName]?.AsEnum<TEnum>();
        }

        /// <inheritdoc/>
        public IEnumerator<PumpValue> GetEnumerator()
        {
            return m_values.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
