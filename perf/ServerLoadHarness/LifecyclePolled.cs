// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// The polled source of the lifecycle scenarios (<c>--lc-polled N</c>): N Int32 variables
    /// ns=namespace;i=1..N whose values never change, on an <see cref="AsyncCustomNodeManager"/>
    /// with sampling groups. Unlike the runtime NodeSet managers, which report on change through
    /// monitored nodes, a sampling group reads every monitored variable each sampling interval and
    /// checks the cached read verdict on each read, so this is the sampled path that the
    /// permission-cache invalidation of AddNodes/DeleteNodes (N6) hits.
    /// </summary>
    internal sealed class LifecyclePolledFactory : IAsyncNodeManagerFactory
    {
        public const string Namespace = "urn:opcfoundation.org:perf:lifecycle:polled";

        public LifecyclePolledFactory(int count)
        {
            m_count = count;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespace];

        /// <inheritdoc/>
        // TODO: Remove when CA2000 recognizes lifecycle ownership through ValueTask<IAsyncNodeManager>.
#pragma warning disable CA2000 // Canonical lifecycle preparation takes ownership of the manager.
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IAsyncNodeManager>(new LifecyclePolledNodeManager(server, configuration, m_count));
        }
#pragma warning restore CA2000

        private readonly int m_count;
    }

    /// <summary>
    /// The node manager of <see cref="LifecyclePolledFactory"/>.
    /// </summary>
    internal sealed class LifecyclePolledNodeManager : AsyncCustomNodeManager
    {
        public LifecyclePolledNodeManager(IServerInternal server, ApplicationConfiguration configuration, int count)
            : base(server, configuration, useSamplingGroups: true, LifecyclePolledFactory.Namespace)
        {
            m_count = count;
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            for (int i = 0; i < m_count; i++)
            {
                string name = "Polled" + i.ToString(CultureInfo.InvariantCulture);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId((uint)i + 1, NamespaceIndex),
                    BrowseName = new QualifiedName(name, NamespaceIndex),
                    DisplayName = new LocalizedText(name),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    MinimumSamplingInterval = 1,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new Variant(i),
                    StatusCode = StatusCodes.Good,
                    Timestamp = DateTimeUtc.Now
                };
                node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly int m_count;
    }
}
