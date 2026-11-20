/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using Opc.Ua.Export;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Coordinates projecting registry documents into the AddressSpace. It parses
    /// and validates each document with <see cref="Wot"/>, builds the TD/TM
    /// dependency closures, converts each closure to one or more NodeSet2
    /// documents and projects them through the <see cref="IWotProjectionHost"/>
    /// (runtime NodeSet Add for first activation, ShadowReload for updates). The
    /// stable registry NodeManager is kept separate. Independent closures commit
    /// independently; a failed or invalid closure retains its previous active
    /// generation. An unchanged closure (same digest, options and binder version)
    /// returns <see cref="WoTOutcomeEnum.Unchanged"/> and emits no model change.
    /// </summary>
    public sealed partial class WotMaterializationCoordinator
    {
        private static void AddPreparedSourceCorrelation(UANodeSet nodeSet, ExpandedNodeId root, WotResource resource)
        {
            var namespaces = new NamespaceTable([Ua.Namespaces.OpcUa, .. nodeSet.NamespaceUris ?? []]);
            NodeId rootId = ExpandedNodeId.ToNodeId(root, namespaces);
            UANode? node = nodeSet.Items?.SingleOrDefault(candidate =>
                NodeId.Parse(candidate.NodeId ?? throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "A converted source Node has no identity.")) == rootId);
            if (rootId.IsNull || node is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "A prepared correlation has no source root in its converted image.");
            }
            ushort index = namespaces.GetIndexOrAppend(Namespaces.WotCon);
            var resourceId = new NodeId(
                $"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}", index);
            string typeId = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, namespaces).ToString();
            string targetId = resourceId.ToString();
            if (node.References?.Any(reference => !reference.IsForward &&
                reference.ReferenceType == typeId && reference.Value == targetId) != true)
            {
                node.References =
                [
                    .. node.References ?? [],
                    new Reference { ReferenceType = typeId, IsForward = false, Value = targetId }
                ];
            }
            nodeSet.NamespaceUris = namespaces.ToArray().Skip(1).ToArray();
        }
    }
}
