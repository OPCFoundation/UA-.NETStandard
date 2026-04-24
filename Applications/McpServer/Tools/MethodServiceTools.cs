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

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        [McpServerTool(Name = "Call")]
        [Description("Call one or more methods on the OPC UA server. Each method call specifies the object and method NodeIds plus input arguments.")]
        public static async Task<string> CallAsync(
            OpcUaSessionManager sessionManager,
            [Description("Object node ID on which the method is defined")] string objectId,
            [Description("Method node ID to call")] string methodId,
            [Description("Input argument values as strings (optional)")] string[]? inputArguments = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<Variant> inputArgs = inputArguments != null
                    ? inputArguments.Select(arg => new Variant(arg)).ToArray()
                    : [];

                ArrayOf<CallMethodRequest> methodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = OpcUaJsonHelper.ParseNodeId(objectId),
                        MethodId = OpcUaJsonHelper.ParseNodeId(methodId),
                        InputArguments = inputArgs
                    }
                ];

                CallResponse response = await session.CallAsync(
                    null,
                    methodsToCall,
                    ct).ConfigureAwait(false);

                CallMethodResult result = response.Results[0];
                List<object?> outputArgs = result.OutputArguments.IsNull
                    ? []
                    : [.. result.OutputArguments.ToArray()!.Select(v => OpcUaJsonHelper.VariantToObject(v))];

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
