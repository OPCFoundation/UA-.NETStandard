/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.ServiceModel;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// A thread safe table of string constants.
    /// </summary>
    public class StringTable
    {
        #region Constructors
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public StringTable()
        {
            m_strings = new List<string>();

            #if DEBUG
            m_instanceId = Interlocked.Increment(ref m_globalInstanceCount);
            #endif
        }

        /// <summary>
        /// Creates an empty collection which is marked as shared.
        /// </summary>
        public StringTable(bool shared)
        {
            m_strings = new List<string>();

            #if DEBUG
            m_shared = shared;
            m_instanceId = Interlocked.Increment(ref m_globalInstanceCount);
            #endif
        }
        
        /// <summary>
        /// Copies a list of strings.
        /// </summary>
        public StringTable(IEnumerable<string> strings)
        {
            Update(strings);

            #if DEBUG
            m_instanceId = Interlocked.Increment(ref m_globalInstanceCount);
            #endif
        }
        #endregion
        
        #region Public Members
        /// <summary>
        /// The synchronization object.
        /// </summary>
        public object SyncRoot
        {
            get { return m_lock; }
        }

        /// <summary>
        /// Returns a unique identifier for the table instance. Used to debug problems with shared tables.
        /// </summary>
        public int InstanceId
        {
            #if DEBUG
            get { return m_instanceId; }
            #else
            get { return 0; }
            #endif
        }

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        public void Update(IEnumerable<string> strings)
        {
            if (strings == null) throw new ArgumentNullException("strings");

            lock (m_lock)
            {
                m_strings = new List<string>(strings);
                
                #if DEBUG
                if (m_shared)
                {
                    for (int ii = 0; ii < m_strings.Count; ii++)
                    {
                        Utils.Trace("WARNING: Adding '{0}' to shared StringTable #{1}.", m_strings[ii], m_instanceId);
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
            if (String.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }
            
            #if DEBUG
            if (m_shared)
            {
                Utils.Trace("WARNING: Adding '{0}' to shared StringTable #{1}.", value, m_instanceId);
            }
            #endif

            lock (m_lock)
            {
                m_strings.Add(value);
                return m_strings.Count-1;
            }
        }

        /// <summary>
        /// Returns the namespace uri at the specified index.
        /// </summary>
        public string GetString(uint index)
        {
            lock (m_lock)
            {
                if (index >= 0 && index < m_strings.Count)
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
            lock (m_lock)
            {
                if (String.IsNullOrEmpty(value))
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
            if (String.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }

            lock (m_lock)
            {
                int index = m_strings.IndexOf(value);    
            
                if (index == -1)
                {
                    #if DEBUG
                    if (m_shared)
                    {
                        Utils.Trace("WARNING: Adding '{0}' to shared StringTable #{1}.", value, m_instanceId);
                    }
                    #endif

                    m_strings.Add(value);
                    return (ushort)(m_strings.Count-1);
                }

                return (ushort)index;
            }
        }   

        /// <summary>
        /// Returns the contexts of the table.
        /// </summary>
        public string[] ToArray()
        {
            lock (m_lock)
            {
                return m_strings.ToArray();
            }
        }

        /// <summary>
        /// Returns the number of entries in the table.
        /// </summary>
        public int Count
        {
            get
            {
                lock (m_lock)
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
                        mapping[ii] = UInt16.MaxValue;
                        continue;
                    }

                    index = Append(uri);
                }

                mapping[ii] = (ushort)index;
            }

            return mapping;
        }
        #endregion                        

        #region Private Fields
        private object m_lock = new object();
        private List<string> m_strings;

        #if DEBUG
        internal bool m_shared;
        internal int m_instanceId;
        private static int m_globalInstanceCount;
        #endif
        #endregion
    }

    /// <summary>
    /// The table of namespace uris for a server.
    /// </summary>
    public class NamespaceTable : StringTable
    {
        #region Constructors
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
        #endregion
        
        #region Public Members
        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        public new void Update(IEnumerable<string> namespaceUris)
        {
            if (namespaceUris == null) throw new ArgumentNullException("namespaceUris");

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
        #endregion
    }
}
