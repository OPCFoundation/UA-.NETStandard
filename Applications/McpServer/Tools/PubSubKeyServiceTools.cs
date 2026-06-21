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
    /// MCP tools for OPC UA PubSub Security Key Service methods (Part 14).
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubKeyServiceTools
    {
        /// <summary>
        /// Get PubSub security keys from the server-side SKS method.
        /// </summary>
        [McpServerTool(Name = "pubsub_get_security_keys")]
        [Description("Call PublishSubscribe.GetSecurityKeys for a security group.")]
        public static Task<string> GetSecurityKeysAsync(
            OpcUaSessionManager sessionManager,
            [Description("Security group identifier")] string securityGroupId,
            [Description("Starting token id")] uint startingTokenId,
            [Description("Requested key count")] uint requestedKeyCount,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                ObjectIds.PublishSubscribe,
                MethodIds.PublishSubscribe_GetSecurityKeys,
                [
                    Variant.From(securityGroupId),
                    Variant.From(startingTokenId),
                    Variant.From(requestedKeyCount)
                ],
                sessionName,
                ct);
        }

        /// <summary>
        /// Add a PubSub security group.
        /// </summary>
        [McpServerTool(Name = "pubsub_add_security_group")]
        [Description("Call PublishSubscribe.SecurityGroups.AddSecurityGroup.")]
        public static Task<string> AddSecurityGroupAsync(
            OpcUaSessionManager sessionManager,
            [Description("Security group name")] string securityGroupName,
            [Description("Key lifetime in milliseconds")] double keyLifetime,
            [Description("Security policy URI")] string securityPolicyUri,
            [Description("Maximum future key count")] uint maxFutureKeyCount,
            [Description("Maximum past key count")] uint maxPastKeyCount,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                ObjectIds.PublishSubscribe_SecurityGroups,
                MethodIds.PublishSubscribe_SecurityGroups_AddSecurityGroup,
                [
                    Variant.From(securityGroupName),
                    Variant.From(keyLifetime),
                    Variant.From(securityPolicyUri),
                    Variant.From(maxFutureKeyCount),
                    Variant.From(maxPastKeyCount)
                ],
                sessionName,
                ct);
        }

        /// <summary>
        /// Remove a PubSub security group.
        /// </summary>
        [McpServerTool(Name = "pubsub_remove_security_group")]
        [Description("Call PublishSubscribe.SecurityGroups.RemoveSecurityGroup.")]
        public static Task<string> RemoveSecurityGroupAsync(
            OpcUaSessionManager sessionManager,
            [Description("Security group NodeId to remove")] string securityGroupNodeId,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                ObjectIds.PublishSubscribe_SecurityGroups,
                MethodIds.PublishSubscribe_SecurityGroups_RemoveSecurityGroup,
                [Variant.From(OpcUaJsonHelper.ParseNodeId(securityGroupNodeId))],
                sessionName,
                ct);
        }

        private static async Task<string> CallAsync(
            OpcUaSessionManager sessionManager,
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            string? sessionName,
            CancellationToken ct)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<CallMethodRequest> methodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = inputArguments
                    }
                ];

                CallResponse response = await session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
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
                        : result.InputArgumentResults.ToArray()!.Select(OpcUaJsonHelper.StatusCodeToString).ToList()
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
