/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
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
        /// Copies a list of strings.
        /// </summary>
        public StringTable(IEnumerable<string> strings)
        {
            Update(strings);

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// The synchronization object.
        /// </summary>
        public object SyncRoot { get; } = new();

#if DEBUG
        /// <summary>
        /// Returns a unique identifier for the table instance. Used to debug problems with shared tables.
        /// </summary>
        public int InstanceId { get; }
#endif

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        public void Update(IEnumerable<string> strings)
        {
            if (strings == null)
            {
                throw new ArgumentNullException(nameof(strings));
            }

            lock (SyncRoot)
            {
                m_strings = [.. strings];

#if DEBUG
                if (m_shared)
                {
                    for (int ii = 0; ii < m_strings.Count; ii++)
                    {
                        Utils.LogWarning(
                            "WARNING: Adding '{0}' to shared StringTable #{1}.",
                            m_strings[ii],
                            InstanceId
                        );
                    }
                }
#endif
            }
        }

        /// <summary>
        /// Adds a string to the end of the table.
        /// </summary>
        public int Append(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

#if DEBUG
            if (m_shared)
            {
                Utils.LogWarning("WARNING: Adding '{0}' to shared StringTable #{1}.", value, InstanceId);
            }
#endif

            lock (SyncRoot)
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
            lock (SyncRoot)
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
            lock (SyncRoot)
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
        public ushort GetIndexOrAppend(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (SyncRoot)
            {
                int index = m_strings.IndexOf(value);

                if (index == -1)
                {
#if DEBUG
                    if (m_shared)
                    {
                        Utils.LogWarning("WARNING: Adding '{0}' to shared StringTable #{1}.", value, InstanceId);
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
            lock (SyncRoot)
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
                lock (SyncRoot)
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

        private List<string> m_strings;

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
        /// Copies a list of strings.
        /// </summary>
        public NamespaceTable(IEnumerable<string> namespaceUris)
        {
            Update(namespaceUris);
        }

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        public new void Update(IEnumerable<string> namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            // check that first entry is the UA namespace.
            int ii = 0;

            foreach (string namespaceUri in namespaceUris)
            {
                if (ii == 0 && namespaceUri != Namespaces.OpcUa)
                {
                    throw new ArgumentException("The first namespace in the table must be the OPC-UA namespace.");
                }

                ii++;

                if (ii == 2)
                {
                    break;
                }
            }

            base.Update(namespaceUris);
        }
    }
}
