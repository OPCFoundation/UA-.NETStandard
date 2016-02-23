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

using System.ServiceModel;

namespace Opc.Ua
{
    #region MessageContextExtension Class
    /// <summary>
    /// Uses to add the service message context to the WCF operation context.
    /// </summary>
    public class MessageContextExtension : IExtension<OperationContext>
    {
        /// <summary>
        /// Initializes the object with the message context to use.
        /// </summary>
        public MessageContextExtension(ServiceMessageContext messageContext)
        {
            m_messageContext = messageContext;
        }

        /// <summary>
        /// Returns the message context associated with the current WCF operation context.
        /// </summary>
        public static MessageContextExtension Current
        {
            get 
            {
                OperationContext context = OperationContext.Current;

                if (context != null)
                {
                    return OperationContext.Current.Extensions.Find<MessageContextExtension>();
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the message context associated with the current WCF operation context.
        /// </summary>
        public static ServiceMessageContext CurrentContext
        {
            get 
            {
                MessageContextExtension extension = MessageContextExtension.Current;

                if (extension != null)
                {
                    return extension.MessageContext;
                }

                return ServiceMessageContext.ThreadContext;
            }
        }
            
        /// <summary>
        /// The message context to use.
        /// </summary>
        public ServiceMessageContext MessageContext
        {
            get 
            { 
                return m_messageContext;
            }
        }

        #region IExtension<OperationContext> Members
        /// <summary cref="IExtension{T}.Attach" />
        public void Attach(OperationContext owner)
        {
        }
        
        /// <summary cref="IExtension{T}.Detach" />
        public void Detach(OperationContext owner)
        {
        }
        #endregion
    
        private ServiceMessageContext m_messageContext;
    }
    #endregion
}
