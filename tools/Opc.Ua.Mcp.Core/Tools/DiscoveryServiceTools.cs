// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        [Description(
            "Find OPC UA servers available at a given discovery endpoint URL. Does not require an active session. " +
            "Returns JSON with a servers array (applicationUri, productUri, applicationName, applicationType, " +
            "discoveryUrls); on failure returns {error:true, statusCode, message}.")]
        public static async Task<string> FindServersAsync(
            OpcUaSessionManager sessionManager,
            [Description("Discovery endpoint URL, e.g. 'opc.tcp://localhost:4840'")] string discoveryUrl,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                var request = new FindServersRequest
                {
                    EndpointUrl = discoveryUrl
                };

                IServiceResponse genericResponse = await session.TransportChannel.SendRequestAsync(
                    request, ct).ConfigureAwait(false);

                var response = (FindServersResponse)genericResponse;

                var results = response.Servers
                    .ConvertAll(s => new Dictionary<string, object?>
                    {
                        ["applicationUri"] = s.ApplicationUri,
                        ["productUri"] = s.ProductUri,
                        ["applicationName"] = s.ApplicationName.Text,
                        ["applicationType"] = s.ApplicationType.ToString(),
                        ["discoveryUrls"] = s.DiscoveryUrls.ToArray()
                    })
                    .ToList();

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
        [Description(
            "Find OPC UA servers registered on the local network via a Local Discovery Server (LDS). Does not " +
            "require an active session. Returns JSON with a servers array (recordId, serverName, discoveryUrl, " +
            "serverCapabilities) and lastCounterResetTime; on failure returns {error:true, statusCode, message}.")]
        public static async Task<string> FindServersOnNetworkAsync(
            OpcUaSessionManager sessionManager,
            [Description("Discovery endpoint URL of the LDS, e.g. 'opc.tcp://localhost:4840'")] string discoveryUrl,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

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

                var servers = response.Servers
                    .ConvertAll(s => new Dictionary<string, object?>
                    {
                        ["recordId"] = s.RecordId,
                        ["serverName"] = s.ServerName,
                        ["discoveryUrl"] = s.DiscoveryUrl,
                        ["serverCapabilities"] = s.ServerCapabilities.ToArray()
                    })
                    .ToList();

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
        [Description("Register an OPC UA server with a Local Discovery Server using the original RegisterServer " +
            "service. Requires an active session. Use RegisterServer2 instead when the discovery server supports " +
            "it and you need per-configuration results (DiscoveryConfiguration/MdnsDiscoveryConfiguration) or a " +
            "detailed configurationResults array back. Returns JSON with responseHeader only; on failure returns " +
            "{error:true, statusCode, message}.")]
        public static async Task<string> RegisterServerAsync(
            OpcUaSessionManager sessionManager,
            [Description("Server to register - application URI")] string applicationUri,
            [Description("Server name")] string serverName,
            [Description("Discovery URLs of the server to register, e.g. ['opc.tcp://localhost:62541/ReferenceServer']")] string[] discoveryUrls,
            [Description("Whether the server is online (default: true)")] bool isOnline = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

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
        [Description("Register an OPC UA server with a discovery server using the newer RegisterServer2 service, " +
            "which supports additional discovery configuration and returns a per-configuration status. Prefer this " +
            "over the plain RegisterServer when the discovery server supports RegisterServer2. Requires an active " +
            "session. Returns JSON with responseHeader and configurationResults (array of per-configuration status " +
            "codes); on failure returns {error:true, statusCode, message}.")]
        public static async Task<string> RegisterServer2Async(
            OpcUaSessionManager sessionManager,
            [Description("Server to register - application URI")] string applicationUri,
            [Description("Server name")] string serverName,
            [Description("Discovery URLs of the server to register, e.g. ['opc.tcp://localhost:62541/ReferenceServer']")] string[] discoveryUrls,
            [Description("Whether the server is online (default: true)")] bool isOnline = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

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
                    .ToList() ??
                    [];

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
