/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Server
{
    public sealed partial class EndpointRegistryNodeManager
    {
        /// <summary>
        /// Finds an active catalog Object by committed Xid. Returns no invented target:
        /// the entity must be committed and its projected node must exist.
        /// </summary>
        public BaseObjectState? FindCatalogObject(string xid, EndpointRegistryRoot root = EndpointRegistryRoot.Generic)
        {
            Catalog? catalog = Find(root);
            if (catalog is null)
            {
                return null;
            }
            try
            {
                catalog.Host.ReadRecord(catalog.Host.Current, xid);
            }
            catch (ServiceResultException)
            {
                return null;
            }
            BaseObjectState? current = catalog.Root;
            string[] parts = xid.Substring(1).Split('/');
            for (int index = 0; index < parts.Length; index++)
            {
                string name = index % 2 != 0 ? parts[index] : parts[index] switch
                {
                    "endpoints" => BrowseNames.Endpoints,
                    "messagegroups" => BrowseNames.MessageGroups,
                    "messages" => BrowseNames.Messages,
                    "versions" => XRegistry.BrowseNames.Versions,
                    _ => part
                };
                current = current?.FindChild(SystemContext, new QualifiedName(name, NamespaceIndexes[0]))
                    as BaseObjectState;
            }
            return current;
        }

        /// <summary>Returns the portable identity of an active local catalog entity.</summary>
        public RegistryEntityReferenceDataType? FindCatalogReference(
            string xid, string role, EndpointRegistryRoot root = EndpointRegistryRoot.Generic)
        {
            Catalog? catalog = Find(root);
            BaseObjectState? node = FindCatalogObject(xid, root);
            return catalog is null || node is null ? null : new RegistryEntityReferenceDataType
            {
                ApplicationUri = m_applicationUri,
                RegistryNode = new ExpandedNodeId(catalog.Root.NumericId, 0, Namespaces.EndpointRegistry),
                Xid = xid,
                Role = role,
                HasNativeTarget = true,
                NativeTarget = NodeId.ToExpandedNodeId(node.NodeId, Server.NamespaceUris)
            };
        }

        /// <summary>
        /// Sets the optional server-owned native PubSub association property on an existing Object.
        /// Does not commit metadata or configure any transport.
        /// </summary>
        public async ValueTask SetPubSubBindingsAsync(
            BaseObjectState node,
            ArrayOf<PubSubBindingSnapshotDataType> snapshots,
            CancellationToken cancellationToken = default)
        {
            if (node is EndpointGroupState container)
            {
                if (container.PubSubBindings is null)
                {
                    container.AddPubSubBindings(SystemContext);
                    container.PubSubBindings!.NodeId = new NodeId(Guid.NewGuid(), NamespaceIndexes[0]);
                    await AddPredefinedNodeAsync(SystemContext, container.PubSubBindings, cancellationToken)
                        .ConfigureAwait(false);
                }
                XRegistryProjectionEngine.SetValue(container.PubSubBindings, snapshots);
                container.ClearChangeMasks(SystemContext, false);
            }
        }
    }
}
