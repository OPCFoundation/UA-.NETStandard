// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.Roles
{
    /// <summary>
    /// Dependency-injected factory for per-session role-management clients.
    /// </summary>
    public sealed class RoleManagementClientFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public RoleManagementClientFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a new <see cref="RoleManagementClient"/> bound to
        /// <paramref name="session"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public RoleManagementClient Create(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new RoleManagementClient(session);
        }

        /// <summary>
        /// The shared telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }
    }
}
