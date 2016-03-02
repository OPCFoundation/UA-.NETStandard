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

using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Stores various configuration parameters used by the channel.
    /// </summary>
    public class TcpChannelQuotas
    {
        #region Constructors
        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public TcpChannelQuotas()
        {
            m_messageContext = ServiceMessageContext.GlobalContext;
            m_maxMessageSize = TcpMessageLimits.DefaultMaxMessageSize;
            m_maxBufferSize = TcpMessageLimits.DefaultMaxMessageSize;
            m_channelLifetime = TcpMessageLimits.DefaultChannelLifetime;
            m_securityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The context to use when encoding/decoding messages.
        /// </summary>
        public ServiceMessageContext MessageContext
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
        public X509CertificateValidator CertificateValidator
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
        private ServiceMessageContext m_messageContext;
        private X509CertificateValidator m_certificateValidator;
        #endregion
    }
}
