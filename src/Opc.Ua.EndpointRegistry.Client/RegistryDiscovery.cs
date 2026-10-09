using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.RegistryClients
{
    internal static class RegistryDiscovery
    {
        public static NodeId Resolve(ISession session, ExpandedNodeId id)
        {
            if (id.NamespaceUri is null || session.NamespaceUris.GetIndex(id.NamespaceUri) <= 0)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The server does not expose the requested registry namespace.");
            }
            return ExpandedNodeId.ToNodeId(id, session.NamespaceUris);
        }

        public static async Task<NodeId> ChildAsync(
            ISession session, NodeId parent, string name, string namespaceUri, CancellationToken cancellationToken)
        {
            int index = session.NamespaceUris.GetIndex(namespaceUri);
            if (index <= 0)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The server does not expose the requested member namespace.");
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(null,
            [
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(name, (ushort)index)
                            }
                        ]
                    }
                }
            ], cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Missing browse-path result.");
            }
            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue ||
                result.Targets[0].TargetId.ServerIndex != 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound, "The registry member is not uniquely local.");
            }
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
        }

        public static async ValueTask<RegistrySnapshotClient> OpenAsync(
            ISession session, NativeRegistryAccessTypeClient access, NodeId accessNode,
            RegistrySnapshotOpenRequestDataType request, uint maxBytes, CancellationToken cancellationToken)
        {
            NodeId limitsNode = await ChildAsync(session, accessNode, XRegistry.BrowseNames.SnapshotLimits,
                XRegistry.Namespaces.xRegistry, cancellationToken).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(limitsNode, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out RegistrySnapshotLimitsDataType? limits, session.MessageContext) ||
                limits is null || limits.MaxReadItems == 0 || limits.MaxReadBytes == 0)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid native SnapshotLimits.");
            }
            return await RegistrySnapshotClient.OpenAsync(access, request, limits.MaxReadItems,
                maxBytes == 0 ? limits.MaxReadBytes : System.Math.Min(maxBytes, limits.MaxReadBytes),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
