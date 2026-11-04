/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    public sealed partial class OpcUaFederationProvider
    {
        /// <summary>
        /// Explicitly observes an Endpoint or reusable Message Group through an authorized Session.
        /// Collection/root ownership is established by Browse, never by a private NodeId formula.
        /// </summary>
        public async ValueTask<FederationGroupSnapshot> PreloadGroupAsync(
            string xid,
            FederationGroupSnapshot? previous = null,
            CancellationToken cancellationToken = default)
        {
            CheckSession();
            string[] parts = xid.Split('/');
            if (parts.Length != 3 || parts[0].Length != 0 ||
                parts[1] is not ("endpoints" or "messagegroups") || parts[2].Length == 0)
            {
                throw new ArgumentException("A concrete collection-qualified Group Xid is required.", nameof(xid));
            }
            NodeId root = Transport(m_binding.RegistryRoot);
            ushort domain = Namespace(Namespaces.EndpointRegistry);
            NodeId collection = await ChildAsync(root, new QualifiedName(parts[1] == "endpoints"
                ? BrowseNames.Endpoints : BrowseNames.MessageGroups, domain), cancellationToken).ConfigureAwait(false);
            NodeId group = await ChildAsync(collection, new QualifiedName(parts[2], domain), cancellationToken)
                .ConfigureAwait(false);
            FederationNodeObservation registryEvidence = await ObserveNodeAsync(root, "RegistryRoot", cancellationToken)
                .ConfigureAwait(false);
            FederationNodeObservation groupEvidence = await ObserveNodeAsync(group, "Group", cancellationToken)
                .ConfigureAwait(false);
            ushort xreg = Namespace(XRegistry.Namespaces.xRegistry);
            string observedXid = Text(await ReadPropertyAsync(group, new QualifiedName(XRegistry.BrowseNames.Xid, xreg),
                cancellationToken).ConfigureAwait(false));
            string groupId = Text(await ReadPropertyAsync(group, new QualifiedName(XRegistry.BrowseNames.GroupId, xreg),
                cancellationToken).ConfigureAwait(false));
            string collectionName = Text(await ReadPropertyAsync(group,
                new QualifiedName(XRegistry.BrowseNames.CollectionName, xreg), cancellationToken).ConfigureAwait(false));
            uint epoch = Number(await ReadPropertyAsync(group, new QualifiedName(XRegistry.BrowseNames.Epoch, xreg),
                cancellationToken).ConfigureAwait(false));
            if (observedXid != xid || groupId != parts[2] || collectionName != parts[1])
            {
                throw new ArgumentException("The observed Group identity or collection disagrees with its ownership path.");
            }
            NodeId snapshot = await ChildAsync(group, new QualifiedName(BrowseNames.Snapshot, domain), cancellationToken)
                .ConfigureAwait(false);
            DataValue value = await m_session.ReadValueAsync(snapshot, cancellationToken).ConfigureAwait(false);
            Check(value.StatusCode);
            if (!value.WrappedValue.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out RegistryReadResultDataType? result, m_session.MessageContext) ||
                result is null || StatusCode.IsBad(result.StatusCode) ||
                !result.Document.TryGetValue(out RegistryRecordDataType? record, m_session.MessageContext) ||
                record is null || result.Epoch != epoch)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The native Group snapshot is incomplete or changed.");
            }
            uint after = Number(await ReadPropertyAsync(group, new QualifiedName(XRegistry.BrowseNames.Epoch, xreg),
                cancellationToken).ConfigureAwait(false));
            if (after != epoch)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The Group changed during explicit preload.");
            }
            var source = new RegistryEntityReferenceDataType
            {
                OriginUri = m_binding.Origin.OriginUri,
                ApplicationUri = m_binding.Origin.ApplicationUri,
                RegistryNode = m_binding.Origin.RegistryNode,
                Xid = xid,
                Role = "Group",
                Locator = m_session.ConfiguredEndpoint.Description.EndpointUrl,
                HasNativeTarget = true,
                NativeTarget = FederationPortableIdentity.FromNode(group, m_session.NamespaceUris)
            };
            m_mapper.Restore(record);
            return new FederationGroupSnapshot(source, m_binding, registryEvidence, groupEvidence, record, epoch, previous);
        }
    }
}
