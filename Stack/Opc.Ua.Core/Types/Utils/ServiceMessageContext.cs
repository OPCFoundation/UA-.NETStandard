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
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ServiceMessageContext()
        {
            Initialize(false);
        }

        private ServiceMessageContext(bool shared)
            : this()
        {
            Initialize(shared);
        }

        private void Initialize(bool shared)
        {
            MaxStringLength = DefaultEncodingLimits.MaxStringLength;
            MaxByteStringLength = DefaultEncodingLimits.MaxByteStringLength;
            MaxArrayLength = DefaultEncodingLimits.MaxArrayLength;
            MaxMessageSize = DefaultEncodingLimits.MaxMessageSize;
            MaxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels;
            MaxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries;
            m_namespaceUris = new NamespaceTable(shared);
            m_serverUris = new StringTable(shared);
            m_factory = EncodeableFactory.GlobalFactory;
        }

        /// <summary>
        /// The default context for the process (used only during XML serialization).
        /// </summary>
        public static ServiceMessageContext GlobalContext { get; } = new ServiceMessageContext(true);

        /// <summary>
        /// The default context for the thread (used only during XML serialization).
        /// </summary>
        public static ServiceMessageContext ThreadContext
        {
            get => GlobalContext;
            set { }
        }

        /// <inheritdoc/>
        public int MaxStringLength { get; set; }

        /// <inheritdoc/>
        public int MaxArrayLength { get; set; }

        /// <inheritdoc/>
        public int MaxByteStringLength { get; set; }

        /// <inheritdoc/>
        public int MaxMessageSize { get; set; }

        /// <inheritdoc/>
        public int MaxEncodingNestingLevels { get; set; }

        /// <inheritdoc/>
        public int MaxDecoderRecoveries { get; set; }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris
        {
            get => m_namespaceUris;
            set
            {
                if (value == null)
                {
                    m_namespaceUris = GlobalContext.NamespaceUris;
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
                    m_serverUris = GlobalContext.ServerUris;
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
                    m_factory = GlobalContext.Factory;
                    return;
                }

                m_factory = value;
            }
        }

        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private IEncodeableFactory m_factory;
    }
}
