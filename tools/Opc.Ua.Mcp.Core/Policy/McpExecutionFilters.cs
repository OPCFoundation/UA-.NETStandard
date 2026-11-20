/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Establishes trusted transport identity without accepting caller claims from tool arguments.
    /// </summary>
    internal static class McpExecutionFilters
    {
        /// <summary>
        /// Creates a revocable asynchronous request scope for capability and ownership checks.
        /// </summary>
        internal static McpRequestHandler<CallToolRequestParams, CallToolResult> CaptureCaller(
            McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                IServiceProvider services = request.Services ??
                    throw new InvalidOperationException("The MCP request has no service provider.");
                McpCallerContext callers = services.GetRequiredService<McpCallerContext>();
                try
                {
                    McpCallerIdentity? identity = GetIdentity(request.User, services);
                    using IDisposable scope = callers.EnterRequest(identity);
                    return await next(request, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    return McpCompanionTools.Result(new JsonObject
                    {
                        ["error"] = true,
                        ["statusCode"] = exception.StatusCode.ToString(),
                        ["statusCodeValue"] = exception.StatusCode.Code,
                        ["message"] = exception.Message
                    });
                }
            };
        }

        /// <summary>
        /// Creates a revocable caller scope for MCP resource-list requests.
        /// </summary>
        internal static McpRequestHandler<ListResourcesRequestParams, ListResourcesResult>
            CaptureCallerForResourceList(
                McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                IServiceProvider services = request.Services ??
                    throw new InvalidOperationException("The MCP request has no service provider.");
                McpCallerContext callers = services.GetRequiredService<McpCallerContext>();
                using IDisposable scope = callers.EnterRequest(GetIdentity(request.User, services));
                return await next(request, ct).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Creates a revocable caller scope for MCP resource-read requests.
        /// </summary>
        internal static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult>
            CaptureCallerForResourceRead(
                McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                IServiceProvider services = request.Services ??
                    throw new InvalidOperationException("The MCP request has no service provider.");
                McpCallerContext callers = services.GetRequiredService<McpCallerContext>();
                using IDisposable scope = callers.EnterRequest(GetIdentity(request.User, services));
                return await next(request, ct).ConfigureAwait(false);
            };
        }

        private static McpCallerIdentity? GetIdentity(
            System.Security.Claims.ClaimsPrincipal? principal,
            IServiceProvider services)
        {
            McpExecutionOptions options = services.GetRequiredService<McpExecutionOptions>();
            if (principal is not null)
            {
                return McpCallerContext.FromPrincipal(principal);
            }
            return services.GetService<IMcpOperationPolicy>() is McpExecutionPolicy policy
                ? policy.LocalCaller
                : options.LocalCallerId is { } local
                    ? new McpCallerIdentity(local, McpCallerContext.LocalIssuer, IsLocal: true)
                    : null;
        }
    }
}
