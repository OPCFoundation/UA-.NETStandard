/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
        public ServiceMessageContext MessageContext => m_messageContext;

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
