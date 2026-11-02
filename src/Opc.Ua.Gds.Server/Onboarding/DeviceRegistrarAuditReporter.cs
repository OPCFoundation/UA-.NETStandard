/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Reports accepted identities and software updates after restricting onboarding event types.
    /// </summary>
    public sealed class DeviceRegistrarAuditReporter : IDeviceRegistrarAuditReporter
    {
        /// <summary>
        /// Restricts the installed registration event hierarchy to SecurityAdmin receivers.
        /// Supply all installed model nodes, including custom subtypes, before publishing the address space.
        /// Hosts adding subtypes later must apply the same permissions before exposing them.
        /// Delivery uses the server's asynchronous event path and its per-session ReceiveEvents authorization.
        /// </summary>
        public DeviceRegistrarAuditReporter(
            IServerInternal server,
            ISystemContext context,
            ArrayOf<NodeState> addressSpace)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            NodeId baseTypeId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceRegistrationAuditEventType, context.NamespaceUris);
            NodeId softwareTypeId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceSoftwareUpdatedAuditEventType, context.NamespaceUris);
            NodeId identityTypeId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceIdentityAcceptedAuditEventType, context.NamespaceUris);
            var hierarchy = new HashSet<NodeId> { baseTypeId };
            var types = new List<BaseObjectTypeState>();
            bool hasRegistrationType = false;
            bool added;
            do
            {
                added = false;
                foreach (NodeState node in addressSpace)
                {
                    if (node is BaseObjectTypeState type &&
                        (type.NodeId == baseTypeId || hierarchy.Contains(type.SuperTypeId)) &&
                        !types.Contains(type))
                    {
                        types.Add(type);
                        hierarchy.Add(type.NodeId);
                        hasRegistrationType |= type.NodeId == baseTypeId;
                        added = true;
                    }
                }
            }
            while (added);

            if (!hasRegistrationType ||
                !hierarchy.Contains(softwareTypeId) ||
                !hierarchy.Contains(identityTypeId))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            foreach (BaseObjectTypeState type in types)
            {
                if (type.OnReadRolePermissions != null || type.OnReadUserRolePermissions != null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
            foreach (BaseObjectTypeState type in types)
            {
                type.RolePermissions = CreatePermissions();
                type.UserRolePermissions = CreatePermissions();
                type.ClearChangeMasks(context, false);
            }
            m_eventTypes = types.ToArray().ToArrayOf();
        }

        /// <inheritdoc/>
        public ValueTask ReportIdentityAcceptedAsync(
            ISystemContext context,
            OnboardingIdentitySelection selection,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            VerifyDelivery(cancellationToken);
            if (string.IsNullOrEmpty(selection.EncodedTicket))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            var auditEvent = new DeviceIdentityAcceptedAuditEventState(null);
            auditEvent.Initialize(
                context, null, EventSeverity.Min,
                LocalizedText.From("Device identity accepted."), true, DateTime.UtcNow);
            ushort namespaceIndex = auditEvent.TypeDefinitionId.NamespaceIndex;
            if (!auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.ProductInstanceUri, namespaceIndex),
                    selection.ProductInstanceUri, false) ||
                !auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.Certificate, namespaceIndex),
                    selection.SelectedIdentity, false) ||
                !auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.Ticket, namespaceIndex),
                    selection.EncodedTicket, false) ||
                !auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.Composite, namespaceIndex),
                    selection.EncodedComposite, false) ||
                !auditEvent.SetChildValue(context, Opc.Ua.BrowseNames.SourceNode, Opc.Ua.ObjectIds.Server, false) ||
                !auditEvent.SetChildValue(
                    context, Opc.Ua.BrowseNames.SourceName, "DeviceRegistrar/ProvideIdentities", false))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            return m_server.ReportEventAsync(context, auditEvent, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ReportSoftwareStatusAsync(
            ISystemContext context,
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            VerifyDelivery(cancellationToken);
            var auditEvent = new DeviceSoftwareUpdatedAuditEventState(null);
            auditEvent.Initialize(
                context, null, EventSeverity.Min,
                LocalizedText.From("Device software status updated."), true, DateTime.UtcNow);
            ushort namespaceIndex = auditEvent.TypeDefinitionId.NamespaceIndex;
            if (!auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.ProductInstanceUri, namespaceIndex),
                    productInstanceUri, false) ||
                !auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.Status, namespaceIndex),
                    status, false) ||
                !auditEvent.SetChildValue(context,
                    new QualifiedName(Opc.Ua.Onboarding.BrowseNames.SoftwareRevision, namespaceIndex),
                    softwareRevision, false) ||
                !auditEvent.SetChildValue(context, Opc.Ua.BrowseNames.SourceNode, Opc.Ua.ObjectIds.Server, false) ||
                !auditEvent.SetChildValue(
                    context, Opc.Ua.BrowseNames.SourceName, "DeviceRegistrar/UpdateSoftwareStatus", false))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            return m_server.ReportEventAsync(context, auditEvent, cancellationToken);
        }

        /// <summary>
        /// Rejects cancellation, disabled auditing and permission changes before reporting sensitive data.
        /// </summary>
        private void VerifyDelivery(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_server.Auditing)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            foreach (BaseObjectTypeState type in m_eventTypes)
            {
                if (type.OnReadRolePermissions != null ||
                    type.OnReadUserRolePermissions != null ||
                    !IsRestricted(type.RolePermissions) ||
                    !IsRestricted(type.UserRolePermissions))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
        }

        /// <summary>
        /// Grants access to the complete registration event only to the SecurityAdmin role.
        /// </summary>
        private static ArrayOf<RolePermissionType> CreatePermissions()
        {
            return
            [
                new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.ReceiveEvents)
                }
            ];
        }

        /// <summary>
        /// Detects a changed access policy before sensitive event payloads enter the dispatcher.
        /// </summary>
        private static bool IsRestricted(ArrayOf<RolePermissionType> permissions)
        {
            return permissions.Count == 1 &&
                permissions[0].RoleId == Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin &&
                (permissions[0].Permissions & (uint)PermissionType.ReceiveEvents) != 0;
        }

        /// <summary>The host's asynchronous, session-authorized event reporting surface.</summary>
        private readonly IServerInternal m_server;

        /// <summary>The registration event hierarchy restricted before the address space is published.</summary>
        private readonly ArrayOf<BaseObjectTypeState> m_eventTypes;
    }
}
