/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Server
{
    public sealed partial class EndpointRegistryNodeManager
    {
        /// <summary>
        /// Projects server-owned Group reference properties on an actually hosted Group.
        /// Metadata is an exact source record, separate from the local Group's identity/epoch.
        /// This helper makes no trust decision, metadata commit or conformance claim.
        /// </summary>
        public async ValueTask SetGroupFederationAsync(
            BaseObjectState node,
            RegistryOriginDataType origin,
            ExpandedNodeId target,
            string locator,
            RegistryRecordDataType metadata,
            uint sourceEpoch,
            bool verified,
            string applicationUri,
            CancellationToken cancellationToken = default)
        {
            if (node is not GroupState group)
            {
                throw new ArgumentException("Only a projected Group can own a Group reference.", nameof(node));
            }
            if (group.OriginRegistry is null)
            {
                group.AddOriginRegistry(SystemContext);
                await RegisterGroupPropertyAsync(group.OriginRegistry!, cancellationToken).ConfigureAwait(false);
            }
            if (group.ExternalReference is null)
            {
                group.AddExternalReference(SystemContext);
                await RegisterGroupPropertyAsync(group.ExternalReference!, cancellationToken).ConfigureAwait(false);
            }
            if (group.GroupUrl is null)
            {
                group.AddGroupUrl(SystemContext);
                await RegisterGroupPropertyAsync(group.GroupUrl!, cancellationToken).ConfigureAwait(false);
            }
            var name = new QualifiedName("SourceSnapshot", NamespaceIndexes[0]);
            if (group.FindChild(SystemContext, name) is not PropertyState<RegistryReadResultDataType> snapshot)
            {
                snapshot = new PropertyState<RegistryReadResultDataType>.Implementation<StructureBuilder<RegistryReadResultDataType>>(group)
                {
                    BrowseName = name,
                    DisplayName = new LocalizedText(name.Name),
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                    TypeDefinitionId = Ua.VariableTypeIds.PropertyType,
                    DataType = ExpandedNodeId.ToNodeId(XRegistry.DataTypeIds.RegistryReadResultDataType, Server.NamespaceUris),
                    ValueRank = ValueRanks.Scalar
                };
                group.AddChild(snapshot);
                await RegisterGroupPropertyAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            group.OriginRegistry!.Value = (RegistryOriginDataType)origin.Clone();
            group.ExternalReference!.Value = verified && !target.IsNull
                ? target.WithServerIndex(Server.ServerUris.GetIndexOrAppend(applicationUri))
                : ExpandedNodeId.Null;
            group.GroupUrl!.Value = verified ? locator : string.Empty;
            group.ExternalReference.StatusCode = verified ? StatusCodes.Good : StatusCodes.BadNotConnected;
            group.GroupUrl.StatusCode = verified ? StatusCodes.Good : StatusCodes.BadNotConnected;
            snapshot.Value = new RegistryReadResultDataType
            {
                StatusCode = verified ? StatusCodes.Good : StatusCodes.BadNotConnected,
                Epoch = sourceEpoch,
                Document = new ExtensionObject((RegistryRecordDataType)metadata.Clone()),
                ContinuationPoint = ByteString.Empty,
                Issues = []
            };
            group.ClearChangeMasks(SystemContext, includeChildren: true);
        }

        /// <summary>Clears transport exposure before its previous projection is retired.</summary>
        public void InvalidateGroupFederation(BaseObjectState node)
        {
            if (node is GroupState group)
            {
                if (group.ExternalReference is { } target)
                {
                    target.Value = ExpandedNodeId.Null;
                    target.StatusCode = StatusCodes.BadNotConnected;
                }
                if (group.GroupUrl is { } locator)
                {
                    locator.Value = string.Empty;
                    locator.StatusCode = StatusCodes.BadNotConnected;
                }
                if (group.FindChild(SystemContext, new QualifiedName("SourceSnapshot", NamespaceIndexes[0]))
                    is PropertyState<RegistryReadResultDataType> snapshot && snapshot.Value is { } value)
                {
                    var copy = (RegistryReadResultDataType)value.Clone();
                    copy.StatusCode = StatusCodes.BadNotConnected;
                    snapshot.Value = copy;
                }
                group.ClearChangeMasks(SystemContext, includeChildren: true);
            }
        }

        private async ValueTask RegisterGroupPropertyAsync(BaseVariableState property, CancellationToken cancellationToken)
        {
            property.NodeId = new NodeId(Guid.NewGuid(), NamespaceIndexes[0]);
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.WriteMask = AttributeWriteMask.None;
            property.UserWriteMask = AttributeWriteMask.None;
            await AddPredefinedNodeAsync(SystemContext, property, cancellationToken).ConfigureAwait(false);
        }
    }
}
