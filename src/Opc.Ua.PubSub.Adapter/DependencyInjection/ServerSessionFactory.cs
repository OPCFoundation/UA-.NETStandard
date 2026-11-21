// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Default <see cref="IServerSessionFactory"/> implementation that
    /// creates <see cref="ServerSession"/> instances wrapping a modern
    /// managed session.
    /// </summary>
    public sealed class ServerSessionFactory : IServerSessionFactory
    {
        /// <summary>
        /// Creates a factory whose sessions resolve security policy URIs
        /// against <see cref="SecurityPolicies.Default"/>.
        /// </summary>
        public ServerSessionFactory()
            : this(securityPolicies: null)
        {
        }

        /// <summary>
        /// Creates a factory whose sessions resolve security policy URIs
        /// against the supplied registry, so a policy the application
        /// contributed through <c>AddSecurityPolicy</c> is negotiable by the
        /// sessions this adapter opens. Passing <see langword="null"/> falls
        /// back to <see cref="SecurityPolicies.Default"/>.
        /// </summary>
        public ServerSessionFactory(ISecurityPolicyRegistry? securityPolicies)
        {
            SecurityPolicyRegistry = securityPolicies;
        }

        /// <summary>
        /// The security policy registry forwarded to created sessions, or
        /// <see langword="null"/> when <see cref="SecurityPolicies.Default"/>
        /// is used.
        /// </summary>
        public ISecurityPolicyRegistry? SecurityPolicyRegistry { get; }

        /// <inheritdoc/>
        public IServerSession Create(
            ServerConnectionOptions options,
            ITelemetryContext telemetry)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new ServerSession(options, telemetry, SecurityPolicyRegistry);
        }
    }
}
