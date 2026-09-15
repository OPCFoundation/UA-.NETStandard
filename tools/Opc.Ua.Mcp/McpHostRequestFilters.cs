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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Applies the executable's connection policy without changing embeddable library defaults.
    /// </summary>
    internal static class McpHostRequestFilters
    {
        /// <summary>
        /// Requires encrypted connections unless the caller explicitly selects another security mode.
        /// </summary>
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> RequireExplicitUnsecuredMode(
            McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return (request, ct) =>
            {
                if (request.MatchedPrimitive is McpServerTool tool &&
                    tool.ProtocolTool.Name == "Connect" &&
                    request.Params is CallToolRequestParams parameters)
                {
                    bool useNone = false;
                    if (parameters.Arguments?.TryGetValue("securityMode", out JsonElement explicitMode) == true)
                    {
                        string? value = explicitMode.ValueKind == JsonValueKind.String
                            ? explicitMode.GetString()
                            : null;
                        if (!string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(value, "Sign", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(value, "SignAndEncrypt", StringComparison.OrdinalIgnoreCase))
                        {
                            return ValueTask.FromResult(new CallToolResult
                            {
                                IsError = true,
                                Content =
                                [
                                    new TextContentBlock
                                    {
                                        Text = "Connect requires securityMode to be 'None', 'Sign', or " +
                                            "'SignAndEncrypt'. Omit the argument to require SignAndEncrypt; " +
                                            "null and empty values are not valid modes. " +
                                            "Use GetEndpoints to see available configurations."
                                    }
                                ]
                            });
                        }
                        useNone = string.Equals(value, "None", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        var arguments = parameters.Arguments == null
                            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            : new Dictionary<string, JsonElement>(parameters.Arguments, StringComparer.Ordinal);
                        using JsonDocument mode = JsonDocument.Parse("\"SignAndEncrypt\"");
                        arguments["securityMode"] = mode.RootElement.Clone();
                        request.Params = new CallToolRequestParams
                        {
                            Name = parameters.Name,
                            Arguments = arguments,
                            Meta = parameters.Meta,
                            InputResponses = parameters.InputResponses,
                            RequestState = parameters.RequestState
                        };
                    }
                    bool autoAccept = parameters.Arguments?.TryGetValue(
                        "autoAcceptCerts", out JsonElement acceptance) == true &&
                        acceptance.ValueKind == JsonValueKind.True;
                    if (useNone || autoAccept)
                    {
                        IServiceProvider services = request.Services ??
                            throw new InvalidOperationException(
                                "MCP request services are required to audit security relaxations.");
                        ILogger logger = services.GetRequiredService<ITelemetryContext>()
                            .CreateLogger("Opc.Ua.Mcp.Program");
                        if (useNone)
                        {
                            logger.UnsecuredConnectionRequested();
                        }
                        if (autoAccept)
                        {
                            logger.UntrustedCertificateAcceptanceRequested();
                        }
                    }
                }

                return next(request, ct);
            };
        }
    }
}
