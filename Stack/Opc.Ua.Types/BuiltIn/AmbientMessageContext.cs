/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
    public sealed class AmbientMessageContext
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
