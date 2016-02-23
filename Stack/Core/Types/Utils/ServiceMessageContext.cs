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
            m_maxStringLength     = UInt16.MaxValue;
            m_maxByteStringLength = UInt16.MaxValue*16;
            m_maxArrayLength      = UInt16.MaxValue;
            m_maxMessageSize      = UInt16.MaxValue*32;
            m_namespaceUris       = new NamespaceTable();
            m_serverUris          = new StringTable();
            m_factory             = EncodeableFactory.GlobalFactory;
        }

        private ServiceMessageContext(bool shared) : this()
        {
            m_maxStringLength     = UInt16.MaxValue;
            m_maxByteStringLength = UInt16.MaxValue*16;
            m_maxArrayLength      = UInt16.MaxValue;
            m_maxMessageSize      = UInt16.MaxValue*32;
            m_namespaceUris       = new NamespaceTable(shared);
            m_serverUris          = new StringTable(shared);
            m_factory             = EncodeableFactory.GlobalFactory;
        }
        #endregion
        
		#region Static Members
        /// <summary>
        /// The default context for the process (used only during XML serialization).
        /// </summary>
        public static ServiceMessageContext GlobalContext
        {
            get { return s_globalContext; }
        }
        

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
		public object SyncRoot
		{
			get { return m_lock; }
		}

        /// <summary>
        /// The maximum length for any string, byte string or xml element.
        /// </summary>
        public int MaxStringLength
        {
            get { lock (m_lock) { return m_maxStringLength;  } }
            set { lock (m_lock) { m_maxStringLength = value; } }
        }

        /// <summary>
        /// The maximum length for any array.
        /// </summary>
        public int MaxArrayLength
        {
            get { lock (m_lock) { return m_maxArrayLength;  } }
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
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private EncodeableFactory m_factory;

        private static ServiceMessageContext s_globalContext = new ServiceMessageContext(true);
        #endregion
    }
}
