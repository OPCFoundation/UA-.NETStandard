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

using System;
using System.Threading;

namespace Opc.Ua
{
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
        public static MessageContextExtension Current
        {
            get => s_current.Value;
            private set => s_current.Value = value;
        }

        /// <summary>
        /// Returns the message context associated with the current operation context.
        /// </summary>
        public static IServiceMessageContext CurrentContext
        {
            get
            {
                MessageContextExtension extension = Current;

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
        public IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Set the context for a specific using scope
        /// </summary>
        /// <param name="messageContext"></param>
        /// <returns></returns>
        public static IDisposable SetScopedContext(IServiceMessageContext messageContext)
        {
            MessageContextExtension previousContext = Current;
            Current = new MessageContextExtension(messageContext);

            return new DisposableAction(() => Current = previousContext);
        }

        /// <summary>
        /// Disposable wrapper for reseting the Current context to
        /// the previous value on exiting the using scope
        /// </summary>
        private class DisposableAction : IDisposable
        {
            private readonly Action action;

            public DisposableAction(Action action)
            {
                this.action = action;
            }

            public void Dispose()
            {
                action?.Invoke();
            }
        }

        private static readonly AsyncLocal<MessageContextExtension> s_current = new();
    }
}
