/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

namespace Opc.Ua
{
    /// <summary>
	/// Stores context information associated with a UA server that is used during message processing.
	/// </summary>
	public class ServiceMessageContext
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ServiceMessageContext()
        {
            m_maxStringLength = UInt16.MaxValue;
            m_maxByteStringLength = UInt16.MaxValue * 16;
            m_maxArrayLength = UInt16.MaxValue;
            m_maxMessageSize = UInt16.MaxValue * 32;
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_factory = EncodeableFactory.GlobalFactory;
            m_maxEncodingNestingLevels = 200;
        }

        private ServiceMessageContext(bool shared) : this()
        {
            m_maxStringLength = UInt16.MaxValue;
            m_maxByteStringLength = UInt16.MaxValue * 16;
            m_maxArrayLength = UInt16.MaxValue;
            m_maxMessageSize = UInt16.MaxValue * 32;
            m_namespaceUris = new NamespaceTable(shared);
            m_serverUris = new StringTable(shared);
            m_factory = EncodeableFactory.GlobalFactory;
            m_maxEncodingNestingLevels = 200;
        }
        #endregion

        #region Static Members
        /// <summary>
        /// The default context for the process (used only during XML serialization).
        /// </summary>
        public static ServiceMessageContext GlobalContext => s_globalContext;


        /// <summary>
        /// The default context for the thread (used only during XML serialization).
        /// </summary>
        public static ServiceMessageContext ThreadContext
        {
            get
            {
                return s_globalContext;
            }

            set
            {
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Returns the object used to synchronize access to the context.
        /// </summary>
        public object SyncRoot => m_lock;

        /// <summary>
        /// The maximum length for any string, byte string or xml element.
        /// </summary>
        public int MaxStringLength
        {
            get { lock (m_lock) { return m_maxStringLength; } }
            set { lock (m_lock) { m_maxStringLength = value; } }
        }

        /// <summary>
        /// The maximum length for any array.
        /// </summary>
        public int MaxArrayLength
        {
            get { lock (m_lock) { return m_maxArrayLength; } }
            set { lock (m_lock) { m_maxArrayLength = value; } }
        }

        /// <summary>
        /// The maximum length for any ByteString.
        /// </summary>
        public int MaxByteStringLength
        {
            get { lock (m_lock) { return m_maxByteStringLength; } }
            set { lock (m_lock) { m_maxByteStringLength = value; } }
        }

        /// <summary>
        /// The maximum length for any Message.
        /// </summary>
        public int MaxMessageSize
        {
            get { lock (m_lock) { return m_maxMessageSize; } }
            set { lock (m_lock) { m_maxMessageSize = value; } }
        }

        /// <summary>
        /// The maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        public uint MaxEncodingNestingLevels
        {
            get { lock (m_lock) { return m_maxEncodingNestingLevels; } }
        }

        /// <summary>
        /// The table of namespaces used by the server.
        /// </summary>
        public NamespaceTable NamespaceUris
        {
            get
            {
                return m_namespaceUris;
            }

            set
            {
                lock (m_lock)
                {
                    if (value == null)
                    {
                        m_namespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris;
                        return;
                    }

                    m_namespaceUris = value;
                }
            }
        }

        /// <summary>
        /// The table of servers used by the server.
        /// </summary>
        public StringTable ServerUris
        {
            get
            {
                return m_serverUris;
            }

            set
            {
                lock (m_lock)
                {
                    if (value == null)
                    {
                        m_serverUris = ServiceMessageContext.GlobalContext.ServerUris;
                        return;
                    }

                    m_serverUris = value;
                }
            }
        }

        /// <summary>
        /// The factory used to create encodeable objects.
        /// </summary>
        public EncodeableFactory Factory
        {
            get
            {
                return m_factory;
            }

            set
            {
                lock (m_lock)
                {
                    if (value == null)
                    {
                        m_factory = ServiceMessageContext.GlobalContext.Factory;
                        return;
                    }

                    m_factory = value;
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private int m_maxStringLength;
        private int m_maxByteStringLength;
        private int m_maxArrayLength;
        private int m_maxMessageSize;
        private uint m_maxEncodingNestingLevels;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private EncodeableFactory m_factory;

        private static ServiceMessageContext s_globalContext = new ServiceMessageContext(true);
        #endregion
    }
}
