// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
