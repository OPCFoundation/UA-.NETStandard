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
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Used to add the service message context to the operation context.
    /// IMPORTANT: DO NOT USE. This is a temporary measure to land
    /// context during deserialization and will be deprecated in the
    /// near future.
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public class AmbientMessageContext
    {
        /// <summary>
        /// Initializes the object with the message context to use.
        /// </summary>
        private AmbientMessageContext(IServiceMessageContext messageContext)
        {
            MessageContext = messageContext;
        }

        /// <summary>
        /// Returns an ambient telemetry context associated with the
        /// current operation context.
        /// </summary>
        public static ITelemetryContext Telemetry => s_current.Value?.MessageContext?.Telemetry;

        /// <summary>
        /// Returns the message context associated with the current operation context.
        /// </summary>
        public static IServiceMessageContext CurrentContext
        {
            get
            {
                AmbientMessageContext extension = s_current.Value;

                if (extension == null)
                {
                    // Create a root context if none has been defined yet.
                    // This root context will not have telemetry context
                    // associated with it. All loggers etc will use the
                    // default telemetry context
                    var messageContext = new ServiceMessageContext(null);
                    s_current.Value = new AmbientMessageContext(messageContext);
                    return messageContext;
                }

                return extension.MessageContext;
            }
        }

        /// <summary>
        /// The message context to use.
        /// </summary>
        public IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Set the context for a specific using scope
        /// </summary>
        public static IDisposable SetScopedContext(IServiceMessageContext messageContext)
        {
            AmbientMessageContext previousContext = s_current.Value;

            s_current.Value = new AmbientMessageContext(messageContext);

            // If no root context, use the message context passed as root
            return new Restore(previousContext);
        }

        /// <summary>
        /// Clone the current context as a new scope
        /// </summary>
        public static IDisposable SetScopedContext(ITelemetryContext telemetry)
        {
            AmbientMessageContext previousContext = s_current.Value;

            s_current.Value = new AmbientMessageContext(
                previousContext == null ?
                    new ServiceMessageContext(telemetry) :
                    new ServiceMessageContext(previousContext.MessageContext, telemetry));

            return new Restore(previousContext);
        }

        /// <summary>
        /// Disposable wrapper for reseting the context to
        /// the previous value on exiting the using scope
        /// </summary>
        private sealed class Restore : IDisposable
        {
            private readonly AmbientMessageContext m_context;

            public Restore(AmbientMessageContext context)
            {
                m_context = context;
            }

            public void Dispose()
            {
                if (m_context != null)
                {
                    s_current.Value = m_context;
                }
            }
        }

        private static readonly AsyncLocal<AmbientMessageContext> s_current = new();
    }
}
