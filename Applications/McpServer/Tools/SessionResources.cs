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
using System.Globalization;
using System.Linq;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP resources exposing OPC UA session information.
    /// </summary>
    [McpServerResourceType]
    public sealed class SessionResources
    {
        /// <summary>
        /// Lists all active OPC UA sessions.
        /// </summary>
        [McpServerResource(
            UriTemplate = "opcua://sessions",
            Name = "Active Sessions",
            MimeType = "application/json")]
        [Description("List all active OPC UA sessions with their connection status, " +
            "endpoint URLs, and security configuration.")]
        public static string ListSessions(OpcUaSessionManager sessionManager)
        {
            var sessions = sessionManager.GetAllSessions();
            var result = sessions.Select(s => new Dictionary<string, object?>
            {
                ["name"] = s.Name,
                ["endpointUrl"] = s.Endpoint.EndpointUrl,
                ["securityMode"] = s.Endpoint.SecurityMode.ToString(),
                ["authType"] = s.AuthType,
                ["isConnected"] = s.IsConnected,
                ["connectedAt"] = s.ConnectedAt.ToString("o", CultureInfo.InvariantCulture),
            }).ToList();

            return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
            {
                ["sessionCount"] = result.Count,
                ["sessions"] = result,
            });
        }

        /// <summary>
        /// Gets full details of a named OPC UA session.
        /// </summary>
        [McpServerResource(
            UriTemplate = "opcua://sessions/{name}",
            Name = "Session Details",
            MimeType = "application/json")]
        [Description("Get full details of a named OPC UA session including endpoint, " +
            "security, session ID, and namespace table.")]
        public static string GetSession(OpcUaSessionManager sessionManager, string name)
        {
            var info = sessionManager.GetSessionInfo(name);
            if (info == null)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = $"Session '{name}' not found.",
                });
            }

            return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
            {
                ["name"] = info.Name,
                ["endpointUrl"] = info.Endpoint.EndpointUrl,
                ["securityMode"] = info.Endpoint.SecurityMode.ToString(),
                ["securityPolicyUri"] = info.Endpoint.SecurityPolicyUri,
                ["authType"] = info.AuthType,
                ["isConnected"] = info.IsConnected,
                ["sessionId"] = info.Session.SessionId.ToString(),
                ["sessionName"] = info.Session.SessionName,
                ["connectedAt"] = info.ConnectedAt.ToString("o", CultureInfo.InvariantCulture),
                ["namespaces"] = info.Session.NamespaceUris.ToArray()!
                    .Select((uri, idx) => new Dictionary<string, object?>
                    {
                        ["index"] = idx,
                        ["uri"] = uri
                    })
                    .ToList(),
                ["serverUris"] = info.Session.ServerUris?.ToArray(),
            });
        }

        /// <summary>
        /// Gets the namespace table for a named session.
        /// </summary>
        [McpServerResource(
            UriTemplate = "opcua://sessions/{name}/namespaces",
            Name = "Namespace Table",
            MimeType = "application/json")]
        [Description("Get the server namespace table for a named session.")]
        public static string GetNamespaces(OpcUaSessionManager sessionManager, string name)
        {
            var info = sessionManager.GetSessionInfo(name);
            if (info == null)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = $"Session '{name}' not found.",
                });
            }

            return OpcUaJsonHelper.Serialize(info.Session.NamespaceUris.ToArray()!
                .Select((uri, idx) => new Dictionary<string, object?>
                {
                    ["index"] = idx,
                    ["uri"] = uri
                })
                .ToList());
        }
    }
}
