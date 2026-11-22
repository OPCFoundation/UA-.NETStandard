// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public static ITelemetryContext? Telemetry => s_current.Value?.MessageContext?.Telemetry;

        /// <summary>
        /// Returns the message context associated with the current operation context.
        /// </summary>
        public static IServiceMessageContext CurrentContext
        {
            get
            {
                AmbientMessageContext? extension = s_current.Value;

                if (extension == null)
                {
                    // Create a root context if none has been defined yet.
                    // This root context will not have telemetry context
                    // associated with it. All loggers etc will use the
                    // default telemetry context
#pragma warning disable CS0618 // Type or member is obsolete
                    var messageContext = new ServiceMessageContext(null!);
#pragma warning restore CS0618 // Type or member is obsolete
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
            AmbientMessageContext? previousContext = s_current.Value;

            s_current.Value = new AmbientMessageContext(messageContext);

            // If no root context, use the message context passed as root
            return new Restore(previousContext);
        }

        /// <summary>
        /// Clone the current context as a new scope
        /// </summary>
        public static IDisposable SetScopedContext(ITelemetryContext telemetry)
        {
            AmbientMessageContext? previousContext = s_current.Value;

#pragma warning disable CS0618 // Type or member is obsolete
            s_current.Value = new AmbientMessageContext(
                previousContext == null ?
                    new ServiceMessageContext(telemetry) :
                    new ServiceMessageContext(previousContext.MessageContext, telemetry));
#pragma warning restore CS0618 // Type or member is obsolete

            return new Restore(previousContext);
        }

        /// <summary>
        /// Disposable wrapper for reseting the context to
        /// the previous value on exiting the using scope
        /// </summary>
        private sealed class Restore : IDisposable
        {
            private readonly AmbientMessageContext? m_context;

            public Restore(AmbientMessageContext? context)
            {
                m_context = context;
            }

            public void Dispose()
            {
                // restore a missing previous context too, otherwise the
                // scoped context outlives the scope.
                s_current.Value = m_context!;
            }
        }

        private static readonly AsyncLocal<AmbientMessageContext> s_current = new();
    }
}
