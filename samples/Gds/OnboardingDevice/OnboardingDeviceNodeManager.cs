/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;

namespace OnboardingDevice
{
    /// <summary>
    /// Creates the one sample-owned device object; no Part 22 Resources infrastructure is implemented here.
    /// </summary>
    internal sealed class OnboardingDeviceNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Borrows the exact runtime registered by the fluent device composition.
        /// </summary>
        public OnboardingDeviceNodeManagerFactory(IProvisionableDeviceRuntime runtime)
        {
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [OnboardingDeviceNodeManager.NamespaceUri];

        /// <inheritdoc/>
        public async ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnboardingDeviceNodeManager? manager = null;
            try
            {
                manager = new OnboardingDeviceNodeManager(server, configuration, m_runtime);
                IAsyncNodeManager result = await ValueTask.FromResult<IAsyncNodeManager>(manager)
                    .ConfigureAwait(false);
                manager = null;
                return result;
            }
            finally
            {
                manager?.Dispose();
            }
        }

        /// <summary>The host-owned singleton device runtime.</summary>
        private readonly IProvisionableDeviceRuntime m_runtime;
    }

    /// <summary>
    /// Publishes a generated ProvisionableDevice as an ordinary object under Objects for remote exercise.
    /// Normative Resources placement is gated on the verified Part 22 implementation merge; this temporary
    /// placement neither creates a Resources folder nor claims full Part 21 or Part 22 profile compliance.
    /// </summary>
    internal sealed class OnboardingDeviceNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Uses the shared server, model context and device runtime without creating another application.
        /// </summary>
        public OnboardingDeviceNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IProvisionableDeviceRuntime runtime)
            : base(server, configuration, server.Telemetry.CreateLogger<OnboardingDeviceNodeManager>())
        {
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            NamespaceUris = [NamespaceUri];
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ushort ns = context.NamespaceUris.GetIndexOrAppend(NamespaceUri);
            var device = new ProvisionableDeviceState(null);
            device.SetRegistrarEndpoints = new SetRegistrarEndpointsMethodState(device);
            device.Create(
                context, new NodeId("Device", ns),
                new QualifiedName("ProvisionableDevice", ns),
                new LocalizedText("Connected singleton provisionable device"),
                assignNodeIds: false);
            var mapping = new Dictionary<NodeId, NodeId>();
            var assigned = new HashSet<NodeId>();
            AssignStableIdentifiers(context, device, "Device", ns, mapping, assigned);
            device.UpdateReferenceTargets(context, mapping);
            device.BindToRuntime(context, m_runtime);
            PropertyState<bool> singleton = device.IsSingleton ??
                throw new InvalidOperationException("The generated device has no IsSingleton property.");
            RequestTicketsMethodState requestTickets = device.RequestTickets ??
                throw new InvalidOperationException("The generated device has no RequestTickets method.");
            if (device.SetRegistrarEndpoints == null)
            {
                throw new InvalidOperationException("The optional SetRegistrarEndpoints method was not initialized.");
            }
            device.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)PermissionType.Browse
                }
            ];
            singleton.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];
            requestTickets.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                }
            ];
            device.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            return ValueTask.FromResult(new NodeStateCollection { device });
        }

        /// <summary>
        /// Gives every generated child a URI-qualified sample identifier independent of namespace table ordering.
        /// The generated standard browse names and method signatures are not replaced with sample wire methods.
        /// </summary>
        private static void AssignStableIdentifiers(
            ISystemContext context,
            NodeState node,
            string path,
            ushort ns,
            Dictionary<NodeId, NodeId> mapping,
            HashSet<NodeId> assigned)
        {
            var id = new NodeId(path, ns);
            if (!assigned.Add(id))
            {
                throw new InvalidOperationException("The generated device has duplicate sample browse paths.");
            }
            if (!node.NodeId.IsNull)
            {
                mapping[node.NodeId] = id;
            }
            node.NodeId = id;
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                string name = child.BrowseName.Name ??
                    throw new InvalidOperationException("A generated device child has no browse name.");
                AssignStableIdentifiers(
                    context, child, path + "/" + Uri.EscapeDataString(name), ns, mapping, assigned);
            }
        }

        /// <summary>The stable sample namespace, not the standard namespace's normative device instance.</summary>
        internal const string NamespaceUri = "urn:opcfoundation.org:UA:Samples:OnboardingDevice";

        /// <summary>The same runtime used for tickets and registrar candidate persistence.</summary>
        private readonly IProvisionableDeviceRuntime m_runtime;
    }
}
