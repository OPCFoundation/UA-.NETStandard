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
using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Server.Registry
{
    /// <summary>
    /// Stable NodeManager that exposes the well-known AASRegistry Object and projects registry snapshots.
    /// </summary>
    public sealed class AasRegistryNodeManager : AsyncCustomNodeManager, IConformanceContributor
    {
        /// <summary>
        /// Initializes a registry NodeManager.
        /// </summary>
        public AasRegistryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IAasRegistryService registry)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<AasRegistryNodeManager>(),
                Opc.Ua.Aas.V3.Namespaces.AasV3,
                XRegistryWellKnown.XRegistryNamespaceUri)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_projection = new AasRegistryProjection(
                SystemContext,
                server.NamespaceUris,
                async (node, ct) => await AddPredefinedNodeAsync(node, ct).ConfigureAwait(false),
                async (nodeId, ct) => await DeleteNodeAsync(SystemContext, nodeId, ct).ConfigureAwait(false),
                Registry);
        }

        /// <summary>
        /// Gets the hosted registry service.
        /// </summary>
        public IAasRegistryService Registry { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// The registry half is claimed unconditionally because the projection
        /// publishes the root, its groups and the resource files, derives every
        /// identifier per clause 6.5.3, keeps versions as the lifecycle record,
        /// answers both discovery methods and applies the disclosure tiers.
        /// Materialization and export follow the Materialize Method and the
        /// Environment file the AASRegistryType declares. Packages are claimed
        /// only when a package group is actually present, and clause 10 requires
        /// AAS-PackageIntegrity to accompany AAS-Packages.
        /// </remarks>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new List<QualifiedName>
                {
                    new("AAS-Registry"),
                    new("AAS-RegistryIdentity"),
                    new("AAS-RegistryVersioning"),
                    new("AAS-Discovery"),
                    new("AAS-DisclosureTiers"),
                    new("AAS-UpdateableRegistry"),
                    new("AAS-EnvironmentExport")
                };
                if (HasPackageStore())
                {
                    units.Add(new QualifiedName("AAS-Packages"));
                    units.Add(new QualifiedName("AAS-PackageIntegrity"));
                }
                return new ArrayOf<QualifiedName>(units.ToArray());
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Clause 10 defines conformance units but assigns no server profile
        /// URIs, and the IDTA identifier of Annex G applies only to a Server
        /// that also implements the HTTP binding, which this one does not.
        /// </remarks>
        public ArrayOf<string> ServerProfiles => [];

        private bool HasPackageStore()
        {
            foreach (AasRegistryGroup group in Registry.Current.GroupsById.Values)
            {
                if (group.Kind == AasRegistryEntityKind.PackageStore)
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection nodes = new NodeStateCollection()
                .AddOpcUaXRegistry(context)
                .AddOpcUaAasV3(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            NodeId registryNodeId = ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.ObjectIds.AASRegistry, Server.NamespaceUris);
            if (predefinedNode is AASRegistryState registry && predefinedNode.NodeId == registryNodeId)
            {
                m_registryNode = registry;
                registry.EventNotifier = EventNotifiers.SubscribeToEvents;
                registry.LookupShellsByAssetLink!.OnCallMethod2Async = OnLookupShellsByAssetLinkAsync;
                registry.GetSubmodel!.OnCallMethod2Async = OnGetSubmodelAsync;
                registry.Materialize!.OnCallMethod2Async = OnMaterializeAsync;
                XRegistryProjectionEngine.LinkMethodArguments(registry, context);
            }
            return new ValueTask<NodeState>(predefinedNode);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            await Registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            Registry.Changed += OnRegistryChanged;
            if (m_registryNode is not null)
            {
                await m_projection.AttachAsync(m_registryNode, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            Registry.Changed -= OnRegistryChanged;
            m_projection.Dispose();
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_projection.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnRegistryChanged(object? sender, AasRegistryChangedEventArgs e)
        {
            _ = Task.Run(async () => await m_projection.ReconcileAsync(CancellationToken.None).ConfigureAwait(false));
        }

        private ValueTask<ServiceResult> OnLookupShellsByAssetLinkAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            string name = GetString(inputArguments, 0);
            string value = GetString(inputArguments, 1);
            outputArguments.Clear();
            outputArguments.Add(new Variant(Registry.LookupShellsByAssetLink(name, value, context)));
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        private async ValueTask<ServiceResult> OnGetSubmodelAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            string submodelIdentifier = GetString(inputArguments, 0);
            AasGetSubmodelResult result = await Registry
                .GetSubmodelAsync(submodelIdentifier, context, cancellationToken)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                outputArguments.Clear();
                return result.StatusCode;
            }
            outputArguments.Clear();
            outputArguments.Add(new Variant(result.Document));
            outputArguments.Add(new Variant(result.Format));
            outputArguments.Add(new Variant(result.ContentType));
            return ServiceResult.Good;
        }

        private static ValueTask<ServiceResult> OnMaterializeAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ServiceResult>(
                ServiceResult.Create(StatusCodes.BadNotSupported, "The updateable-registry profile is not enabled."));
        }

        private static string GetString(ArrayOf<Variant> inputArguments, int index)
        {
            return index < inputArguments.Count &&
                inputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is string value
                    ? value
                    : string.Empty;
        }

        private readonly AasRegistryProjection m_projection;
        private AASRegistryState? m_registryNode;
    }
}
