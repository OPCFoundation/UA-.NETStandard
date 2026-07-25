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
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Validates MCP tool-call arguments before the generated tool binder runs.
    /// </summary>
    internal static class McpRequestFilters
    {
        /// <summary>
        /// Returns an actionable tool error when required arguments are absent.
        /// </summary>
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> ValidateRequiredArguments(
            McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                if (request.MatchedPrimitive is McpServerTool tool)
                {
                    List<string> missingArguments = GetMissingRequiredArguments(
                        tool.ProtocolTool.InputSchema,
                        request.Params?.Arguments);
                    if (missingArguments.Count > 0)
                    {
                        string toolName = request.Params?.Name ?? tool.ProtocolTool.Name;
                        string message =
                            $"Tool '{toolName}' is missing required argument(s): " +
                            $"{string.Join(", ", missingArguments)}. " +
                            "Call the tool again with values for each named argument.";

                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = message }]
                        };
                    }
                }

                return await next(request, ct).ConfigureAwait(false);
            };
        }

        private static List<string> GetMissingRequiredArguments(
            JsonElement inputSchema,
            IDictionary<string, JsonElement>? arguments)
        {
            var missingArguments = new List<string>();
            if (inputSchema.ValueKind != JsonValueKind.Object ||
                !inputSchema.TryGetProperty("required", out JsonElement required) ||
                required.ValueKind != JsonValueKind.Array)
            {
                return missingArguments;
            }

            foreach (JsonElement requiredArgument in required.EnumerateArray())
            {
                if (requiredArgument.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? argumentName = requiredArgument.GetString();
                if (argumentName != null &&
                    (arguments == null || !arguments.ContainsKey(argumentName)))
                {
                    missingArguments.Add(argumentName);
                }
            }

            return missingArguments;
        }
    }
}
