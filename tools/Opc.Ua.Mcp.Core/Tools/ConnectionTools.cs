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
        [Description("Discover endpoints before connecting; no session required. Returns JSON endpoints " +
            "with URL, security mode/policy and user token types, or {error:true, statusCode, message}.")]
        public static async Task<string> GetEndpointsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Discovery URL, e.g. opc.tcp://localhost:4840.")] string endpointUrl,
            CancellationToken ct = default)
        {
            try
            {
                ArrayOf<EndpointDescription> endpoints =
                    await sessionManager.DiscoverEndpointsAsync(endpointUrl, ct).ConfigureAwait(false);

                List<Dictionary<string, object?>> results =
                    [.. (endpoints.ToArray() ?? []).Select(ep =>
                    new Dictionary<string, object?>
                    {
                        ["endpointUrl"] = ep.EndpointUrl,
                        ["securityMode"] = ep.SecurityMode.ToString(),
                        ["securityPolicyUri"] = ep.SecurityPolicyUri,
                        ["transportProfileUri"] = ep.TransportProfileUri,
                        ["securityLevel"] = ep.SecurityLevel,
                        ["userIdentityTokens"] =
                            (ep.UserIdentityTokens.ToArray() ?? [])
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
        /// Connects using the established anonymous or username authentication arguments.
        /// </summary>
        [McpServerTool(Name = "Connect")]
        [Description("Connect anonymously or with username/password; defaults to the strongest endpoint. " +
            "For X.509 use ConnectCertificate. Returns a confirmation with session name/ID and security settings, " +
            "or JSON {error:true, statusCode, message}.")]
        public static Task<string> ConnectAsync(
            OpcUaSessionManager sessionManager,
            [Description("Server URL, e.g. opc.tcp://localhost:4840.")] string endpointUrl,
            [Description("None, Sign or SignAndEncrypt; default strongest.")]
            string? securityMode = null,
            [Description("Policy, e.g. Basic256Sha256; default automatic.")]
            string? securityPolicy = null,
            [Description("Anonymous (default) or Username.")] string authType = "Anonymous",
            [Description("Username when authType is Username.")] string? username = null,
            [Description("Password when authType is Username.")] string? password = null,
            [Description("Trust unknown server certificates for testing only; default false.")]
            bool autoAcceptCerts = false,
            [Description("Optional session name; default derived from endpoint.")] string? name = null,
            CancellationToken ct = default)
        {
            return ConnectAsync(sessionManager, endpointUrl, securityMode, securityPolicy,
                authType, username, password, autoAcceptCerts, name, certificateIdentity: null, ct);
        }

        /// <summary>
        /// Connects with a named, host-provisioned X.509 identity and strict server-certificate validation.
        /// </summary>
        [McpServerTool(Name = "ConnectCertificate")]
        [Description("Connect with a host-approved X.509 user identity. Server certificates must be trusted; " +
            "no keys, passwords or store paths are accepted. Returns session name, ID and security settings, " +
            "or JSON {error:true, statusCode, message}.")]
        public static Task<string> ConnectCertificateAsync(
            OpcUaSessionManager sessionManager,
            [Description("Server URL, e.g. opc.tcp://localhost:4840.")] string endpointUrl,
            [Description("Exact host-configured identity name, e.g. observer.")] string certificateIdentity,
            [Description("None, Sign or SignAndEncrypt; default strongest.")] string? securityMode = null,
            [Description("Policy, e.g. Basic256Sha256; default automatic.")]
            string? securityPolicy = null,
            [Description("Optional session name; default derived from endpoint.")] string? name = null,
            CancellationToken ct = default)
        {
            return ConnectAsync(sessionManager, endpointUrl, securityMode, securityPolicy,
                "Certificate", null, null, false, name, certificateIdentity, ct);
        }

        /// <summary>
        /// Retains the C# connection overload accepting every authentication option.
        /// </summary>
        public static async Task<string> ConnectAsync(
            OpcUaSessionManager sessionManager,
            string endpointUrl,
            string? securityMode,
            string? securityPolicy,
            string authType,
            string? username,
            string? password,
            bool autoAcceptCerts,
            string? name,
            string? certificateIdentity,
            CancellationToken ct = default)
        {
            try
            {
                return await sessionManager.ConnectAsync(
                    name, endpointUrl, securityMode, securityPolicy,
                    authType, username, password, autoAcceptCerts, certificateIdentity, ct)
                    .ConfigureAwait(false);
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
                    ["message"] = ex.Message,
                    ["exceptionType"] = ex.GetType().Name,
                    ["innerMessage"] = ex.InnerException?.Message
                });
            }
        }

        /// <summary>
        /// Disconnect from the current OPC UA server.
        /// </summary>
        [McpServerTool(Name = "Disconnect")]
        [Description("Close one OPC UA session. Returns a confirmation naming the disconnected session.")]
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
        [Description("Inspect one or all sessions without changing them. Returns status lines with endpoint, " +
            "session name/ID and server URI, or 'Not connected.'.")]
        public static string GetConnectionStatus(
            OpcUaSessionManager sessionManager,
            [Description("Session name to check (defaults to all sessions)")] string? name = null)
        {
            return sessionManager.GetConnectionStatus(name);
        }
    }
}
