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
