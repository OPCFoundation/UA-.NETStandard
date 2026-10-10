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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Di;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Read-only DI discovery, topology, nameplate, functional-group and lock tools.
    /// </summary>
    [McpServerToolType]
    public sealed class DiReadTools
    {
        /// <summary>
        /// Initializes tools that resolve a fresh client from the current named session on every call.
        /// </summary>
        public DiReadTools(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Discovers devices through the existing recursive DI discovery helper.
        /// </summary>
        [McpServerTool(Name = "di_discover_devices", ReadOnly = true, Destructive = false)]
        [Description("Discovers DeviceType instances and subtypes using the DI discovery client, rooted at Objects. " +
            "The helper searches at most three nested non-device folders. Returns a bounded live page; " +
            "use di_browse_topology for explicit deeper topology traversal. A missing DI namespace is an error.")]
        public Task<CallToolResult> DiscoverDevicesAsync(
            [Description("Zero-based live offset, 0..1000000.")] int offset = 0,
            [Description("Maximum devices, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                return await McpCompanionTools.PageAsync(
                    DiDiscoveryClient.EnumerateDevicesAsync(session, m_sessionManager.Telemetry, token),
                    DiJson.Device, offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Browses a standard DI container or the direct children of an explicit topology object.
        /// </summary>
        [McpServerTool(Name = "di_browse_topology", ReadOnly = true, Destructive = false)]
        [Description("Lists direct object children of DeviceSet, NetworkSet, DeviceTopology, or an explicit parent. " +
            "Children requires parentNodeId; other scopes reject it. Returns actual NodeIds and type definitions " +
            "in a live page. It does not recursively traverse or assume a missing container is empty.")]
        public Task<CallToolResult> BrowseTopologyAsync(
            DiTopologyScope scope = DiTopologyScope.DeviceSet,
            [Description("Local parent NodeId whose immediate children should be listed.")]
            string? parentNodeId = null,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum children, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiJson.RequireDefined(scope, nameof(scope));
                if (scope != DiTopologyScope.Children && parentNodeId is not null)
                {
                    throw new ArgumentException("parentNodeId applies only to Children.", nameof(parentNodeId));
                }
                NodeId explicitParent = scope == DiTopologyScope.Children
                    ? DiJson.ParseNodeId(parentNodeId ??
                        throw new ArgumentException("Children requires parentNodeId.", nameof(parentNodeId)))
                    : NodeId.Null;
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                var client = new DiTopologyClient(session, m_sessionManager.Telemetry);
                NodeId parent = scope switch
                {
                    DiTopologyScope.DeviceSet => client.DeviceSetId,
                    DiTopologyScope.NetworkSet => client.NetworkSetId,
                    DiTopologyScope.DeviceTopology => client.DeviceTopologyId,
                    _ => explicitParent
                };
                await DiModelAccess.RequireObjectAsync(session, parent, token).ConfigureAwait(false);
                IAsyncEnumerable<TopologyEntry> children = scope switch
                {
                    DiTopologyScope.DeviceSet => client.EnumerateDevicesAsync(token),
                    DiTopologyScope.NetworkSet => client.EnumerateNetworksAsync(token),
                    _ => client.EnumerateChildrenAsync(parent, token)
                };
                JsonObject page = await McpCompanionTools.PageAsync(
                    children, DiJson.Topology, offset, maxResults, token).ConfigureAwait(false);
                page["parentNodeId"] = parent.ToString();
                page["scope"] = scope.ToString();
                return page;
            }, ct);
        }

        /// <summary>
        /// Reads identification, functional groups and actual optional control-facet child identities.
        /// </summary>
        [McpServerTool(Name = "di_read_device", ReadOnly = true, Destructive = false)]
        [Description("Reads DI identification, a live page of functional groups, and actual Lock, TransferServices " +
            "and SoftwareUpdate child NodeIds. Missing optional endpoints have supported=false. Pass lockNodeId " +
            "to di_acquire_lock/di_release_lock, not the device NodeId. Never acquires a lock or starts an update. " +
            "Identification uses the client summary; di_read_property retains exact UA types and quality.")]
        public Task<CallToolResult> ReadDeviceAsync(
            [Description("Local device NodeId returned by DI discovery; e.g. ns=2;s=Device1.")]
            string deviceNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum functional groups, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId nodeId = DiJson.ParseNodeId(deviceNodeId);
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                DiDeviceClient client = await DiDeviceClient.ForDeviceAsync(
                    session, nodeId, m_sessionManager.Telemetry, token).ConfigureAwait(false);
                DeviceIdentification identification = await client.ReadIdentificationAsync(token).ConfigureAwait(false);
                JsonObject groups = await McpCompanionTools.PageAsync(
                    client.BrowseFunctionalGroupsAsync(token), DiJson.Group, offset, maxResults, token)
                    .ConfigureAwait(false);
                LockingServicesTypeClient? locking = await client.Proxy
                    .GetLockAsync(m_sessionManager.Telemetry, token).ConfigureAwait(false);
                NodeId lockId = locking is null ? NodeId.Null : locking.ObjectId;
                NodeId transferId = await DiModelAccess.ResolveChildAsync(
                    client.Proxy, "TransferServices", token).ConfigureAwait(false);
                NodeId updateId = await DiModelAccess.ResolveChildAsync(
                    client.Proxy, "SoftwareUpdate", token).ConfigureAwait(false);
                return new JsonObject
                {
                    ["deviceNodeId"] = nodeId.ToString(),
                    ["identification"] = DiJson.Identification(identification),
                    ["functionalGroups"] = groups,
                    ["lockNodeId"] = DiJson.Node(lockId),
                    ["transferServicesNodeId"] = DiJson.Node(transferId),
                    ["softwareUpdateNodeId"] = DiJson.Node(updateId),
                    ["lock"] = DiJson.Facet(lockId),
                    ["transferServices"] = DiJson.Facet(transferId),
                    ["softwareUpdate"] = DiJson.Facet(updateId)
                };
            }, ct);
        }

        /// <summary>
        /// Reads one finite, known nameplate property anchored to the generated device proxy.
        /// </summary>
        [McpServerTool(Name = "di_read_property", ReadOnly = true, Destructive = false)]
        [Description("Reads one standard DI device/component nameplate property using the typed proxy's object. " +
            "Returns its actual property NodeId and UA DataValue, preserving localized text, integer zero, null, " +
            "quality and timestamps. Missing properties are supported=false; bad read status is a tool error.")]
        public Task<CallToolResult> ReadPropertyAsync(
            [Description("Local device NodeId returned by DI discovery; e.g. ns=2;s=Device1.")]
            string deviceNodeId,
            DiDeviceProperty property,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiJson.RequireDefined(property, nameof(property));
                NodeId nodeId = DiJson.ParseNodeId(deviceNodeId);
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                DiDeviceClient client = await DiDeviceClient.ForDeviceAsync(
                    session, nodeId, m_sessionManager.Telemetry, token).ConfigureAwait(false);
                JsonObject result = await DiModelAccess.ReadValueAsync(client.Proxy, property.ToString(), token)
                    .ConfigureAwait(false);
                result["deviceNodeId"] = nodeId.ToString();
                result["property"] = property.ToString();
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads a functional group's UI element and direct object children.
        /// </summary>
        [McpServerTool(Name = "di_read_functional_group", ReadOnly = true, Destructive = false)]
        [Description("Reads the optional UIElement value and a live page of direct object children of a " +
            "functional group from di_read_device. UIElement retains its UA type. No configuration is changed.")]
        public Task<CallToolResult> ReadFunctionalGroupAsync(
            [Description("Local functional-group NodeId returned by discovery.")]
            string groupNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum object children, 1..500.")] int maxResults = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId nodeId = DiJson.ParseNodeId(groupNodeId);
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                await DiModelAccess.RequireObjectAsync(session, nodeId, token).ConfigureAwait(false);
                var proxy = new FunctionalGroupTypeClient(session, nodeId, m_sessionManager.Telemetry);
                var topology = new DiTopologyClient(session, m_sessionManager.Telemetry);
                return new JsonObject
                {
                    ["groupNodeId"] = nodeId.ToString(),
                    ["uiElement"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.UIElement, token)
                        .ConfigureAwait(false),
                    ["children"] = await McpCompanionTools.PageAsync(
                        topology.EnumerateChildrenAsync(nodeId, token), DiJson.Topology, offset, maxResults, token)
                        .ConfigureAwait(false)
                };
            }, ct);
        }

        /// <summary>
        /// Reads lock state without claiming, renewing or releasing the lock.
        /// </summary>
        [McpServerTool(Name = "di_read_lock", ReadOnly = true, Destructive = false)]
        [Description("Reads Locked, LockingClient, LockingUser and RemainingLockTime on the actual Lock child. " +
            "Never changes ownership. Values include type, quality and timestamps.")]
        public Task<CallToolResult> ReadLockAsync(
            [Description("Actual DI Lock object NodeId, not the device or product NodeId.")]
            string lockNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId nodeId = DiJson.ParseNodeId(lockNodeId);
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                await DiModelAccess.RequireObjectAsync(session, nodeId, token).ConfigureAwait(false);
                var proxy = new LockingServicesTypeClient(session, nodeId, m_sessionManager.Telemetry);
                return new JsonObject
                {
                    ["lockNodeId"] = nodeId.ToString(),
                    ["locked"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.Locked, token)
                        .ConfigureAwait(false),
                    ["lockingClient"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.LockingClient, token)
                        .ConfigureAwait(false),
                    ["lockingUser"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.LockingUser, token)
                        .ConfigureAwait(false),
                    ["remainingLockTime"] = await DiModelAccess.ReadValueAsync(
                        proxy, Di.BrowseNames.RemainingLockTime, token).ConfigureAwait(false)
                };
            }, ct);
        }

        /// <summary>
        /// The host-owned registry; no session or model proxy is retained.
        /// </summary>
        private readonly OpcUaSessionManager m_sessionManager;
    }
}
