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
    /// MCP tools for OPC UA Discovery Service Set (Part 4, Section 5.4).
    /// </summary>
    [McpServerToolType]
    public sealed class DiscoveryServiceTools
    {
        /// <summary>
        /// Find servers registered on the network.
        /// </summary>
        [McpServerTool(Name = "FindServers")]
        [Description("Find OPC UA servers available at a given discovery endpoint URL. Does not require an active session.")]
        public static async Task<string> FindServersAsync(
            OpcUaSessionManager sessionManager,
            [Description("Discovery endpoint URL, e.g. 'opc.tcp://localhost:4840'")] string discoveryUrl,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                var request = new FindServersRequest
                {
                    EndpointUrl = discoveryUrl
                };

                IServiceResponse genericResponse = await session.TransportChannel.SendRequestAsync(
                    request, ct).ConfigureAwait(false);

                var response = (FindServersResponse)genericResponse;

                List<Dictionary<string, object?>> results = response.Servers.ToArray()?.Select(s => new Dictionary<string, object?>
                {
                    ["applicationUri"] = s.ApplicationUri,
                    ["productUri"] = s.ProductUri,
                    ["applicationName"] = s.ApplicationName.Text,
                    ["applicationType"] = s.ApplicationType.ToString(),
                    ["discoveryUrls"] = s.DiscoveryUrls.ToArray()
                }).ToList() ?? [];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["servers"] = results
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
            catch (InvalidCastException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = "BadServiceUnsupported",
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Find servers on the network via a discovery server.
        /// </summary>
        [McpServerTool(Name = "FindServersOnNetwork")]
        [Description("Find OPC UA servers registered on the local network via a Local Discovery Server (LDS). Does not require an active session.")]
        public static async Task<string> FindServersOnNetworkAsync(
            OpcUaSessionManager sessionManager,
            [Description("Discovery endpoint URL of the LDS, e.g. 'opc.tcp://localhost:4840'")] string discoveryUrl,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                var request = new FindServersOnNetworkRequest
                {
                    StartingRecordId = 0,
                    MaxRecordsToReturn = 100
                };

                IServiceResponse genericResponse = await session.TransportChannel.SendRequestAsync(
                    request, ct).ConfigureAwait(false);

                var response = (FindServersOnNetworkResponse)genericResponse;

                List<Dictionary<string, object?>> servers = response.Servers.ToArray()?.Select(s => new Dictionary<string, object?>
                {
                    ["recordId"] = s.RecordId,
                    ["serverName"] = s.ServerName,
                    ["discoveryUrl"] = s.DiscoveryUrl,
                    ["serverCapabilities"] = s.ServerCapabilities.ToArray()
                }).ToList() ?? [];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["servers"] = servers,
                    ["lastCounterResetTime"] = response.LastCounterResetTime.ToString("o",
                        System.Globalization.CultureInfo.InvariantCulture)
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
            catch (InvalidCastException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = "BadServiceUnsupported",
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Register the server with a discovery server.
        /// </summary>
        [McpServerTool(Name = "RegisterServer")]
        [Description("Register an OPC UA server with a Local Discovery Server. Requires an active session.")]
        public static async Task<string> RegisterServerAsync(
            OpcUaSessionManager sessionManager,
            [Description("Server to register - application URI")] string applicationUri,
            [Description("Server name")] string serverName,
            [Description("Discovery URLs of the server to register")] string[] discoveryUrls,
            [Description("Whether the server is online (default: true)")] bool isOnline = true,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                var registeredServer = new RegisteredServer
                {
                    ServerUri = applicationUri,
                    ServerNames = [new LocalizedText(serverName)],
                    DiscoveryUrls = new ArrayOf<string>(discoveryUrls),
                    IsOnline = isOnline,
                    ServerType = ApplicationType.Server
                };

                var request = new RegisterServerRequest
                {
                    Server = registeredServer
                };

                IServiceResponse genericResponse = await session.TransportChannel.SendRequestAsync(
                    request, ct).ConfigureAwait(false);

                var response = (RegisterServerResponse)genericResponse;

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader)
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
            catch (InvalidCastException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = "BadServiceUnsupported",
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Register the server with a discovery server (version 2).
        /// </summary>
        [McpServerTool(Name = "RegisterServer2")]
        [Description("Register an OPC UA server with a discovery server using RegisterServer2 (supports discovery configuration). Requires an active session.")]
        public static async Task<string> RegisterServer2Async(
            OpcUaSessionManager sessionManager,
            [Description("Server to register - application URI")] string applicationUri,
            [Description("Server name")] string serverName,
            [Description("Discovery URLs of the server to register")] string[] discoveryUrls,
            [Description("Whether the server is online (default: true)")] bool isOnline = true,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                var registeredServer = new RegisteredServer
                {
                    ServerUri = applicationUri,
                    ServerNames = [new LocalizedText(serverName)],
                    DiscoveryUrls = new ArrayOf<string>(discoveryUrls),
                    IsOnline = isOnline,
                    ServerType = ApplicationType.Server
                };

                var request = new RegisterServer2Request
                {
                    Server = registeredServer,
                    DiscoveryConfiguration = []
                };

                IServiceResponse genericResponse = await session.TransportChannel.SendRequestAsync(
                    request, ct).ConfigureAwait(false);

                var response = (RegisterServer2Response)genericResponse;

                List<string> configResults = response.ConfigurationResults.ToArray()?
                    .Select(OpcUaJsonHelper.StatusCodeToString)
                    .ToList() ?? [];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["configurationResults"] = configResults
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
            catch (InvalidCastException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = "BadServiceUnsupported",
                    ["message"] = ex.Message
                });
            }
        }

    }
}
