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

using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Stores context information for the current secure channel.
    /// </summary>
    public class SecureChannelContext
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance with the specified property values.
        /// </summary>
        /// <param name="secureChannelId">The secure channel identifier.</param>
        /// <param name="endpointDescription">The endpoint description.</param>
        /// <param name="messageEncoding">The message encoding.</param>
        public SecureChannelContext(
            string              secureChannelId,
            EndpointDescription endpointDescription,
            RequestEncoding     messageEncoding)
        {        
            m_secureChannelId     = secureChannelId;
            m_endpointDescription = endpointDescription;
            m_messageEncoding     = messageEncoding;
        }

        /// <summary>
        /// Initializes a new instance with the context for the current thread.
        /// </summary>
        protected SecureChannelContext()
        {        
            SecureChannelContext context = SecureChannelContext.Current;

            if (context != null)
            {
                m_secureChannelId     = context.SecureChannelId;
                m_endpointDescription = context.EndpointDescription;
                m_messageEncoding     = context.MessageEncoding;
            }
        }
        #endregion
                
        #region Public Properties
        /// <summary>
        /// TThe unique identifier for the secure channel.
        /// </summary>
        /// <value>The secure channel identifier.</value>
        public string SecureChannelId
        {
            get { return m_secureChannelId; }
        }

        /// <summary>
        /// The description of the endpoint used with the channel.
        /// </summary>
        /// <value>The endpoint description.</value>
        public EndpointDescription EndpointDescription
        {
            get { return m_endpointDescription; }
        }

        /// <summary>
        /// The encoding used with the channel.
        /// </summary>
        /// <value>The message encoding.</value>
        public RequestEncoding MessageEncoding
        {
            get { return m_messageEncoding; }
        }     
        #endregion   

		#region Static Members
        /// <summary>
        /// The active secure channel for the thread.
        /// </summary>
        /// <value>The current secure channel context.</value>
        public static SecureChannelContext Current        
        {
            get
            {
                return s_Dataslot.Value;
            }

            set
            {
                s_Dataslot.Value = value;
            }
        }
        #endregion

        #region Private Fields
        private string m_secureChannelId;
        private EndpointDescription m_endpointDescription;
        private RequestEncoding m_messageEncoding;
        private static ThreadLocal<SecureChannelContext> s_Dataslot = new ThreadLocal<SecureChannelContext>();
        #endregion
    }

    /// <summary>
    /// The message encoding used with a request.
    /// </summary>
    public enum RequestEncoding
    {
        /// <summary>
        /// The request used the UA binary encoding.
        /// </summary>
        Binary,

        /// <summary>
        /// The request used the UA XML encoding.
        /// </summary>
        Xml
    }
}
