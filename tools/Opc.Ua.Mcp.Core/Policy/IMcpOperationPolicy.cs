// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host authority shared by semantic tools, generic UA guards and named runtime ownership.
    /// Server permissions and network-target policy remain additional checks.
    /// </summary>
    public interface IMcpOperationPolicy
    {
        /// <summary>
        /// Reports catalogue-level host enablement, not caller authorization.
        /// </summary>
        bool IsEnabled(McpCapability capability);

        /// <summary>
        /// Requires an enabled capability and an exact trusted caller grant.
        /// </summary>
        McpCallerIdentity Require(McpCapability capability);

        /// <summary>
        /// Additionally requires the exact named UA session in the caller's grant.
        /// </summary>
        McpCallerIdentity RequireSession(McpCapability capability, string sessionName);

        /// <summary>
        /// Requires an explicit host grant to borrow a host-provisioned UA session.
        /// Custom policies must opt in; a caller identity alone does not grant session access.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        McpCallerIdentity RequireSessionAccess(string sessionName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
            throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The caller has no host grant for the selected UA session.");
        }

        /// <summary>
        /// Additionally requires an exact host-configured secret alias in the caller's grant.
        /// </summary>
        McpCallerIdentity RequireSecret(McpCapability capability, string alias);

        /// <summary>
        /// Gets the trusted current identity or refuses an unidentified caller.
        /// </summary>
        McpCallerIdentity RequireCaller();
    }
}
