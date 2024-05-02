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

namespace Opc.Ua
{
    /// <summary>
	/// Stores context information associated with a session is used during message processing.
	/// </summary>
	public class ServiceMessageContext : IServiceMessageContext
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ServiceMessageContext()
        {
            Initialize(false);
        }

        private ServiceMessageContext(bool shared) : this()
        {
            Initialize(shared);
        }

        private void Initialize(bool shared)
        {
            m_maxStringLength = DefaultEncodingLimits.MaxStringLength;
            m_maxByteStringLength = DefaultEncodingLimits.MaxByteStringLength;
            m_maxArrayLength = DefaultEncodingLimits.MaxArrayLength;
            m_maxMessageSize = DefaultEncodingLimits.MaxMessageSize;
            m_maxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels;
            m_maxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries;
            m_namespaceUris = new NamespaceTable(shared);
            m_serverUris = new StringTable(shared);
            m_factory = EncodeableFactory.GlobalFactory;
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
            get => s_globalContext;

            set
            {
            }
        }
        #endregion

        #region Public Properties
        /// <inheritdoc/>
        public int MaxStringLength
        {
            get => m_maxStringLength;
            set { m_maxStringLength = value; }
        }

        /// <inheritdoc/>
        public int MaxArrayLength
        {
            get => m_maxArrayLength;
            set { m_maxArrayLength = value; }
        }

        /// <inheritdoc/>
        public int MaxByteStringLength
        {
            get => m_maxByteStringLength;
            set { m_maxByteStringLength = value; }
        }

        /// <inheritdoc/>
        public int MaxMessageSize
        {
            get => m_maxMessageSize;
            set { m_maxMessageSize = value; }
        }

        /// <inheritdoc/>
        public int MaxEncodingNestingLevels
        {
            get => m_maxEncodingNestingLevels;
            set { m_maxEncodingNestingLevels = value; }
        }

        /// <inheritdoc/>
        public int MaxDecoderRecoveries
        {
            get => m_maxDecoderRecoveries;
            set { m_maxDecoderRecoveries = value; }
        }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris
        {
            get => m_namespaceUris;

            set
            {
                if (value == null)
                {
                    m_namespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris;
                    return;
                }
                m_namespaceUris = value;
            }
        }

        /// <inheritdoc/>
        public StringTable ServerUris
        {
            get => m_serverUris;

            set
            {
                if (value == null)
                {
                    m_serverUris = ServiceMessageContext.GlobalContext.ServerUris;
                    return;
                }

                m_serverUris = value;
            }
        }

        /// <inheritdoc/>
        public IEncodeableFactory Factory
        {
            get => m_factory;

            set
            {
                if (value == null)
                {
                    m_factory = ServiceMessageContext.GlobalContext.Factory;
                    return;
                }

                m_factory = value;
            }
        }
        #endregion

        #region Private Fields
        private int m_maxStringLength;
        private int m_maxByteStringLength;
        private int m_maxArrayLength;
        private int m_maxMessageSize;
        private int m_maxEncodingNestingLevels;
        private int m_maxDecoderRecoveries;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private IEncodeableFactory m_factory;

        private static ServiceMessageContext s_globalContext = new ServiceMessageContext(true);
        #endregion
    }
}
