/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for OPC UA Method Service Set (Part 4, Section 5.11).
    /// </summary>
    [McpServerToolType]
    public sealed class MethodServiceTools
    {
        /// <summary>
        /// Call one or more methods on the OPC UA server.
        /// </summary>
        public static Task<string> CallAsync(
            OpcUaSessionManager sessionManager,
            string objectId,
            string methodId,
            string[]? inputArguments,
            string? sessionName,
            CancellationToken ct)
        {
            return CallAsync(sessionManager, objectId, methodId, inputArguments, sessionName, null, ct);
        }

        /// <summary>
        /// Calls a method with legacy strings or explicitly typed UA JSON arguments.
        /// </summary>
        [McpServerTool(Name = "Call")]
        [Description("Call one or more methods on the OPC UA server. Each method call specifies the object and " +
            "method NodeIds plus input arguments. Returns JSON with responseHeader, statusCode, outputArguments, " +
            "and inputArgumentResults for the call; on failure returns {error:true, statusCode, message}.")]
        public static async Task<string> CallAsync(
            OpcUaSessionManager sessionManager,
            [Description("Object node ID on which the method is defined")] string objectId,
            [Description("Method node ID to call")] string methodId,
            [Description("Input argument values as strings (optional)")] string[]? inputArguments = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            [Description("Optional UA JSON Variant array, e.g. [{\"UaType\":6,\"Value\":0}]. " +
                "Use instead of inputArguments for numbers or structures; load custom types first.")]
            string? inputArgumentsJson = null,
            CancellationToken ct = default)
        {
            OpcUaSessionManager.SessionInfo sessionInfo = sessionManager.GetSessionInfoOrThrow(sessionName);

            try
            {
                McpMutationSession operation = await McpMutationSession.CaptureAsync(sessionManager, sessionInfo, ct)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lifetime = operation.ConfigureAwait(false);
                sessionInfo = operation.Access;
                Client.ISession session = sessionInfo.Session;
                ArrayOf<Variant> inputArgs = OpcUaJsonHelper.ParseCallArguments(
                    inputArguments, inputArgumentsJson, session.MessageContext);

                ArrayOf<CallMethodRequest> methodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = OpcUaJsonHelper.ParseNodeId(objectId),
                        MethodId = OpcUaJsonHelper.ParseNodeId(methodId),
                        InputArguments = inputArgs
                    }
                ];

                await sessionManager.UaOperationGuard.AuthorizeCallsAsync(sessionInfo, methodsToCall, ct)
                    .ConfigureAwait(false);
                operation.VerifyBeforeDispatch(ct);
                CallResponse response = await session.CallAsync(
                    null,
                    methodsToCall,
                    ct).ConfigureAwait(false);

                CallMethodResult result = response.Results[0];
                List<object?> outputArgs = result.OutputArguments.IsNull
                    ? []
                    : [.. result.OutputArguments.ToArray()!.Select(
                        v => OpcUaJsonHelper.VariantToObject(v, session.MessageContext))];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(result.StatusCode),
                    ["outputArguments"] = outputArgs,
                    ["inputArgumentResults"] = result.InputArgumentResults.IsNull
                        ? null
                        : result.InputArgumentResults.ToArray()!
                            .Select(OpcUaJsonHelper.StatusCodeToString)
                            .ToList()
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message
                });
            }
        }
    }
}
