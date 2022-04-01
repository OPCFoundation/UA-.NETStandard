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
    #region MessageContextExtension Class
    /// <summary>
    /// Uses to add the service message context to the operation context.
    /// </summary>
    public class MessageContextExtension
    {
        /// <summary>
        /// Initializes the object with the message context to use.
        /// </summary>
        public MessageContextExtension(IServiceMessageContext messageContext)
        {
            MessageContext = messageContext;
        }

        /// <summary>
        /// Returns the message context associated with the current operation context.
        /// </summary>
        public static MessageContextExtension Current => null;

        /// <summary>
        /// Returns the message context associated with the current operation context.
        /// </summary>
        public static IServiceMessageContext CurrentContext
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
        public IServiceMessageContext MessageContext { get; private set; }
    }
    #endregion
}
