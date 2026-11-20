// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Maps a trusted MCP caller scope to an opaque host-local diagnostics owner key.
    /// </summary>
    internal static class PubSubDiagnosticsCaller
    {
        /// <summary>
        /// Gets the stable host-local key for the current caller or trusted in-process host.
        /// </summary>
        /// <param name="callers">Trusted request identity context.</param>
        /// <returns>A non-empty opaque owner key.</returns>
        /// <exception cref="ServiceResultException">
        /// The request has no authenticated caller.
        /// </exception>
        internal static string GetOwnerKey(McpCallerContext callers)
        {
            ArgumentNullException.ThrowIfNull(callers);
            if (callers.HasRequestScope && callers.Current is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied,
                    "PubSub diagnostics require an authenticated MCP caller.");
            }

            McpCallerIdentity? caller = callers.Current;
            return caller is null
                ? "trusted-host"
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"caller:{caller.Issuer.Length}:{caller.Issuer}:{caller.Subject.Length}:{caller.Subject}");
        }

        /// <summary>
        /// Gets the caller key from a configured service provider, using trusted-host scope for direct host calls.
        /// </summary>
        /// <param name="services">The MCP host services.</param>
        /// <returns>A non-empty opaque owner key.</returns>
        internal static string GetOwnerKey(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return GetOwnerKey(services.GetService(typeof(McpCallerContext)) as McpCallerContext ??
                new McpCallerContext());
        }
    }
}
