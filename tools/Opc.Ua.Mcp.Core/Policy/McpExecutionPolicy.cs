/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Immutable default-deny authorization over host enablement and exact caller grants.
    /// It does not infer authority from a profile, a session name or a supplied confirmation flag.
    /// </summary>
    public sealed class McpExecutionPolicy : IMcpOperationPolicy
    {
        /// <summary>
        /// Snapshots host configuration and uses the trusted request context supplied by the hosting transport.
        /// </summary>
        public McpExecutionPolicy(
            McpExecutionOptions options, McpCallerContext callers, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(telemetry);
            m_options = options.Snapshot();
            m_callers = callers ?? throw new ArgumentNullException(nameof(callers));
            m_logger = telemetry.CreateLogger<McpExecutionPolicy>();
            m_enabled = [.. m_options.EnabledCapabilities];
            m_grants = [];
            foreach (McpCallerGrant grant in m_options.CallerGrants)
            {
                m_grants.Add(grant.Caller, grant);
            }
        }

        /// <inheritdoc/>
        public bool IsEnabled(McpCapability capability)
        {
            return Enum.IsDefined(capability) && m_enabled.Contains(capability);
        }

        /// <summary>
        /// Gets the frozen local transport identity without consulting mutable exposed options.
        /// </summary>
        internal McpCallerIdentity? LocalCaller => m_options.LocalCallerId is { } local
            ? new McpCallerIdentity(local, McpCallerContext.LocalIssuer, IsLocal: true)
            : null;

        /// <inheritdoc/>
        public McpCallerIdentity RequireCaller()
        {
            McpCallerIdentity? caller = m_callers.Current;
            if (caller is not null)
            {
                return caller;
            }
            if (!m_callers.HasRequestScope && m_options.LocalCallerId is { } local)
            {
                return new McpCallerIdentity(local, McpCallerContext.LocalIssuer, IsLocal: true);
            }
            throw Denied("No trusted MCP caller identity is available.");
        }

        /// <inheritdoc/>
        public McpCallerIdentity Require(McpCapability capability)
        {
            if (!IsEnabled(capability))
            {
                throw Denied($"The host has not enabled the {capability} capability.");
            }
            McpCallerIdentity caller = RequireCaller();
            if (!m_grants.TryGetValue(caller, out McpCallerGrant? grant) ||
                !Contains(grant.Capabilities, capability))
            {
                throw Denied($"The caller has no grant for the {capability} capability.");
            }
            return caller;
        }

        /// <inheritdoc/>
        public McpCallerIdentity RequireSession(McpCapability capability, string sessionName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
            McpCallerIdentity caller = Require(capability);
            McpCallerGrant grant = m_grants[caller];
            foreach (string allowed in grant.SessionNames)
            {
                if (string.Equals(allowed, sessionName, StringComparison.OrdinalIgnoreCase))
                {
                    return caller;
                }
            }
            throw Denied("The caller has no privileged-operation grant for the selected UA session.");
        }

        /// <inheritdoc/>
        public McpCallerIdentity RequireSecret(McpCapability capability, string alias)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            McpCallerIdentity caller = Require(capability);
            foreach (string allowed in m_grants[caller].SecretAliases)
            {
                if (string.Equals(allowed, alias, StringComparison.Ordinal))
                {
                    return caller;
                }
            }
            throw Denied("The caller has no grant for the selected secret alias.");
        }

        /// <summary>
        /// Performs allocation-free capability membership over the validated grant.
        /// </summary>
        private static bool Contains(ArrayOf<McpCapability> capabilities, McpCapability requested)
        {
            foreach (McpCapability capability in capabilities)
            {
                if (capability == requested)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Records a bounded, secret-free denial and returns the standard UA access refusal.
        /// </summary>
        private ServiceResultException Denied(string reason)
        {
            m_logger.OperationDenied(reason);
            return new ServiceResultException(StatusCodes.BadUserAccessDenied, reason);
        }

        private readonly McpExecutionOptions m_options;
        private readonly McpCallerContext m_callers;
        private readonly ILogger m_logger;
        private readonly HashSet<McpCapability> m_enabled;
        private readonly Dictionary<McpCallerIdentity, McpCallerGrant> m_grants;
    }

    /// <summary>
    /// Source-generated policy audit diagnostics without caller data or credential material.
    /// </summary>
    internal static partial class McpExecutionPolicyLog
    {
        /// <summary>
        /// Reports a host capability refusal.
        /// </summary>
        [LoggerMessage(EventId = McpServerEventIds.McpExecutionPolicy, Level = LogLevel.Warning,
            Message = "MCP operation denied by host policy: {Reason}")]
        public static partial void OperationDenied(this ILogger logger, string reason);
    }
}
