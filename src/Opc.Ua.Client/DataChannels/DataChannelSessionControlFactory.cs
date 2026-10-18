/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Client.DataChannels
{
    /// <summary>
    /// Injectable factory for Session-bound control helpers and independently enforced local policies.
    /// </summary>
    public sealed class DataChannelSessionControlFactory(TimeProvider timeProvider)
    {
        /// <summary>
        /// Creates an authorizer to inject into the data-context factory before connecting.
        /// </summary>
        public SessionDataChannelAuthorizer CreateAuthorizer(ISession session, IDataChannelAuthorizer localPolicy)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return new SessionDataChannelAuthorizer(
                session, localPolicy, m_timeProvider, session.MessageContext.Telemetry);
        }

        /// <summary>
        /// Creates a fail-closed, initially unbound authorizer for a shared transport's later Session.
        /// </summary>
        public SessionDataChannelAuthorizer CreateAuthorizer(
            IDataChannelAuthorizer localPolicy,
            ITelemetryContext telemetry)
        {
            return new SessionDataChannelAuthorizer(localPolicy, m_timeProvider, telemetry);
        }

        /// <summary>
        /// Creates a generated-proxy helper for the same Session and authorizer.
        /// </summary>
        public DataChannelSessionControl Create(ISession session, SessionDataChannelAuthorizer authorizer)
        {
            return new DataChannelSessionControl(session, authorizer, m_timeProvider);
        }

        private readonly TimeProvider m_timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Fluent opt-in to Session control; it does not install or select a data carrier.
    /// </summary>
    public static class DataChannelClientBuilderExtensions
    {
        /// <summary>
        /// Registers the per-Session control factory using the host's injected clock.
        /// </summary>
        public static IOpcUaClientBuilder AddDataChannelSessionControl(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.TryAddSingleton<DataChannelSessionControlFactory>();
            return builder;
        }
    }
}
