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
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for managing the OPC UA session connection and endpoint discovery.
    /// </summary>
    [McpServerToolType]
    public sealed class ConnectionTools
    {
        /// <summary>
        /// Discover available endpoints of an OPC UA server.
        /// </summary>
        [McpServerTool(Name = "GetEndpoints")]
        [Description("Discover available endpoints of an OPC UA server, including security modes, " +
            "policies, and supported authentication methods. Does not require an active session. " +
            "Call this before Connect to see what's available.")]
        public static async Task<string> GetEndpointsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Server discovery URL, e.g. 'opc.tcp://localhost:62541/Quickstarts/ReferenceServer'")] string endpointUrl,
            CancellationToken ct = default)
        {
            try
            {
                ArrayOf<EndpointDescription> endpoints =
                    await sessionManager.DiscoverEndpointsAsync(endpointUrl, ct).ConfigureAwait(false);

                List<Dictionary<string, object?>> results =
                    [.. (endpoints.ToArray() ?? Array.Empty<EndpointDescription>()).Select(ep =>
                    new Dictionary<string, object?>
                    {
                        ["endpointUrl"] = ep.EndpointUrl,
                        ["securityMode"] = ep.SecurityMode.ToString(),
                        ["securityPolicyUri"] = ep.SecurityPolicyUri,
                        ["transportProfileUri"] = ep.TransportProfileUri,
                        ["securityLevel"] = ep.SecurityLevel,
                        ["userIdentityTokens"] =
                            (ep.UserIdentityTokens.ToArray() ?? Array.Empty<UserTokenPolicy>())
                            .Select(t => new Dictionary<string, object?>
                            {
                                ["tokenType"] = t.TokenType.ToString(),
                                ["policyId"] = t.PolicyId,
                                ["securityPolicyUri"] = t.SecurityPolicyUri
                            }).ToList()
                    })];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["endpoints"] = results
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = "BadUnexpectedError",
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Connect to an OPC UA server.
        /// </summary>
        [McpServerTool(Name = "Connect")]
        [Description("Connect to an OPC UA server. For simplest usage, provide just the endpointUrl " +
            "and it will auto-select the most secure endpoint with anonymous authentication. " +
            "Use GetEndpoints first to discover available security configurations and auth methods, " +
            "then specify securityMode/securityPolicy to select a specific endpoint.")]
        public static async Task<string> ConnectAsync(
            OpcUaSessionManager sessionManager,
            [Description("The OPC UA server endpoint URL")] string endpointUrl,
            [Description("Security mode filter: 'None', 'Sign', or 'SignAndEncrypt'. " +
                "If omitted, selects the most secure available endpoint.")] string? securityMode = null,
            [Description("Security policy filter: e.g. 'Basic256Sha256', 'Aes128_Sha256_RsaOaep', " +
                "'Aes256_Sha256_RsaPss', 'None'. Used with securityMode to select a specific endpoint.")] string? securityPolicy = null,
            [Description("Authentication type: 'Anonymous' (default), 'Username', or 'Certificate'")] string authType = "Anonymous",
            [Description("Username for 'Username' authentication")] string? username = null,
            [Description("Password for 'Username' authentication")] string? password = null,
            [Description("Auto-accept untrusted server certificates (for testing only, default: false)")] bool autoAcceptCerts = false,
            [Description("Session name (auto-generated from endpoint URL if omitted)")] string? name = null,
            CancellationToken ct = default)
        {
            try
            {
                return await sessionManager.ConnectAsync(
                    name, endpointUrl, securityMode, securityPolicy,
                    authType, username, password, autoAcceptCerts, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = "BadUnexpectedError",
                    ["message"] = ex.Message,
                    ["exceptionType"] = ex.GetType().Name,
                    ["innerMessage"] = ex.InnerException?.Message,
                });
            }
        }

        /// <summary>
        /// Disconnect from the current OPC UA server.
        /// </summary>
        [McpServerTool(Name = "Disconnect")]
        [Description("Disconnect from the current OPC UA server session.")]
        public static Task<string> DisconnectAsync(
            OpcUaSessionManager sessionManager,
            [Description("Session name to disconnect (defaults to the only active session)")] string? name = null,
            CancellationToken ct = default)
        {
            return sessionManager.DisconnectAsync(name, ct);
        }

        /// <summary>
        /// Get the current connection status.
        /// </summary>
        [McpServerTool(Name = "GetConnectionStatus")]
        [Description("Check if connected to an OPC UA server and return session information.")]
        public static string GetConnectionStatus(
            OpcUaSessionManager sessionManager,
            [Description("Session name to check (defaults to all sessions)")] string? name = null)
        {
            return sessionManager.GetConnectionStatus(name);
        }
    }
}
