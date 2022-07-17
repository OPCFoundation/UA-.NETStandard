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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Stores various configuration parameters used by the channel.
    /// </summary>
    public class ChannelQuotas
    {
        #region Constructors
        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public ChannelQuotas()
        {
            m_messageContext = ServiceMessageContext.GlobalContext;
            m_maxMessageSize = TcpMessageLimits.DefaultMaxMessageSize;
            m_maxBufferSize = TcpMessageLimits.DefaultMaxBufferSize;
            m_channelLifetime = TcpMessageLimits.DefaultChannelLifetime;
            m_securityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The context to use when encoding/decoding messages.
        /// </summary>
        public IServiceMessageContext MessageContext
        {
            get
            {
                lock (m_lock)
                {
                    return m_messageContext;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_messageContext = value;
                }
            }
        }

        /// <summary>
        /// Validator to use when handling certificates.
        /// </summary>
        public ICertificateValidator CertificateValidator
        {
            get
            {
                lock (m_lock)
                {
                    return m_certificateValidator;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_certificateValidator = value;
                }
            }
        }

        /// <summary>
        /// The maximum size for a message sent or received.
        /// </summary>
        public int MaxMessageSize
        {
            get
            {
                lock (m_lock)
                {
                    return m_maxMessageSize;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_maxMessageSize = value;
                }
            }
        }

        /// <summary>
        /// The maximum size for the send or receive buffers.
        /// </summary>
        public int MaxBufferSize
        {
            get
            {
                lock (m_lock)
                {
                    return m_maxBufferSize;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_maxBufferSize = value;
                }
            }
        }

        /// <summary>
        /// The default lifetime for the channel in milliseconds.
        /// </summary>
        public int ChannelLifetime
        {
            get
            {
                lock (m_lock)
                {
                    return m_channelLifetime;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_channelLifetime = value;
                }
            }
        }

        /// <summary>
        /// The default lifetime for a security token in milliseconds.
        /// </summary>
        public int SecurityTokenLifetime
        {
            get
            {
                lock (m_lock)
                {
                    return m_securityTokenLifetime;
                }
            }

            set
            {
                lock (m_lock)
                {
                    m_securityTokenLifetime = value;
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private int m_maxMessageSize;
        private int m_maxBufferSize;
        private int m_channelLifetime;
        private int m_securityTokenLifetime;
        private IServiceMessageContext m_messageContext;
        private ICertificateValidator m_certificateValidator;
        #endregion
    }
}
