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
using System.Diagnostics;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// A thread safe table of string constants.
    /// </summary>
    public class StringTable
    {
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public StringTable()
        {
            m_strings = [];

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Creates an empty collection which is marked as shared.
        /// </summary>
        public StringTable(bool shared)
        {
            m_strings = [];

#if DEBUG
            m_shared = shared;
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Copies the table
        /// </summary>
        /// <param name="table"></param>
        public StringTable(StringTable table)
        {
            Update(table.m_strings);
#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Copies a list of strings.
        /// </summary>
        public StringTable(IEnumerable<string> strings)
        {
            Update(strings);

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

#if DEBUG
        /// <summary>
        /// Returns a unique identifier for the table instance. Used to debug problems with shared tables.
        /// </summary>
        public int InstanceId { get; }
#endif

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="strings"/> is <c>null</c>.</exception>
        public void Update(IEnumerable<string> strings)
        {
            if (strings == null)
            {
                throw new ArgumentNullException(nameof(strings));
            }

            lock (m_syncRoot)
            {
                m_strings = [.. strings];

#if DEBUG
                if (m_shared)
                {
                    for (int ii = 0; ii < m_strings.Count; ii++)
                    {
                        Debug.WriteLine(
                            "WARNING: Adding '{0}' to shared StringTable #{1}.",
                            m_strings[ii],
                            InstanceId);
                    }
                }
#endif
            }
        }

        /// <summary>
        /// Adds a string to the end of the table.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public int Append(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

#if DEBUG
            if (m_shared)
            {
                Debug.WriteLine(
                    "WARNING: Adding '{0}' to shared StringTable #{1}.",
                    value,
                    InstanceId);
            }
#endif

            lock (m_syncRoot)
            {
                m_strings.Add(value);
                return m_strings.Count - 1;
            }
        }

        /// <summary>
        /// Returns the namespace uri at the specified index.
        /// </summary>
        public string GetString(uint index)
        {
            lock (m_syncRoot)
            {
                if (index < m_strings.Count)
                {
                    return m_strings[(int)index];
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the index of the specified namespace uri.
        /// </summary>
        public int GetIndex(string value)
        {
            lock (m_syncRoot)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return -1;
                }

                return m_strings.IndexOf(value);
            }
        }

        /// <summary>
        /// Returns the index of the specified namespace uri, adds it if it does not exist.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ushort GetIndexOrAppend(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (m_syncRoot)
            {
                int index = m_strings.IndexOf(value);

                if (index == -1)
                {
#if DEBUG
                    if (m_shared)
                    {
                        Debug.WriteLine(
                            "WARNING: Adding '{0}' to shared StringTable #{1}.",
                            value,
                            InstanceId);
                    }
#endif

                    m_strings.Add(value);
                    return (ushort)(m_strings.Count - 1);
                }

                return (ushort)index;
            }
        }

        /// <summary>
        /// Returns the contexts of the table.
        /// </summary>
        public string[] ToArray()
        {
            lock (m_syncRoot)
            {
                return [.. m_strings];
            }
        }

        /// <summary>
        /// Returns the number of entries in the table.
        /// </summary>
        public int Count
        {
            get
            {
                lock (m_syncRoot)
                {
                    return m_strings.Count;
                }
            }
        }

        /// <summary>
        /// Creates a mapping between the URIs in a source table and the indexes in the current table.
        /// </summary>
        /// <param name="source">The string table to map.</param>
        /// <param name="updateTable">if set to <c>true</c> if missing URIs should be added to the current tables.</param>
        /// <returns>A list of indexes in the current table.</returns>
        public ushort[] CreateMapping(StringTable source, bool updateTable)
        {
            if (source == null)
            {
                return null;
            }

            ushort[] mapping = new ushort[source.Count];

            for (uint ii = 0; ii < source.Count; ii++)
            {
                string uri = source.GetString(ii);

                int index = GetIndex(uri);

                if (index < 0)
                {
                    if (!updateTable)
                    {
                        mapping[ii] = ushort.MaxValue;
                        continue;
                    }

                    index = Append(uri);
                }

                mapping[ii] = (ushort)index;
            }

            return mapping;
        }

        /// <summary>
        /// Access the string table from sub classes
        /// </summary>
        protected List<string> m_strings;
        private readonly Lock m_syncRoot = new();

#if DEBUG
        /// <summary>
        /// Whether the table is shared.
        /// </summary>
        protected bool m_shared;
        private static int s_globalInstanceCount;
#endif
    }

    /// <summary>
    /// The table of namespace uris for a server.
    /// </summary>
    public class NamespaceTable : StringTable
    {
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public NamespaceTable()
        {
            Append(Namespaces.OpcUa);
        }

        /// <summary>
        /// Creates an empty collection which is marked as shared.
        /// </summary>
        public NamespaceTable(bool shared)
        {
            Append(Namespaces.OpcUa);
#if DEBUG
            m_shared = shared;
#endif
        }

        /// <summary>
        /// Create a copy
        /// </summary>
        /// <param name="namespaceTable"></param>
        public NamespaceTable(NamespaceTable namespaceTable)
            : base(namespaceTable)
        {
            Debug.Assert(m_strings[0] == Namespaces.OpcUa);
        }

        /// <summary>
        /// Copies a list of strings.
        /// </summary>
        public NamespaceTable(IEnumerable<string> namespaceUris)
        {
            Update(namespaceUris);
        }

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namespaceUris"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public new void Update(IEnumerable<string> namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            base.Update(namespaceUris);

            // check that first entry is the UA namespace.
            if (m_strings[0] != Namespaces.OpcUa)
            {
                throw new ArgumentException(
                    "The first namespace in the table must be the OPC-UA namespace.");
            }
        }
    }
}
