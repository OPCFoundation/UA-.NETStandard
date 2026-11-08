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
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Binds registrar authority nodes to the same managed stores used by ticket and identity validation.
    /// The hosting node manager owns this binding and disposes it after stopping request dispatch.
    /// </summary>
    public sealed class DeviceRegistrarAuthorityTrustLists : IDisposable
    {
        /// <summary>
        /// Binds both registered authority lists through the existing TrustList service and audit pipeline.
        /// Both lists require configured issuer stores so their complete contents can be administered.
        /// </summary>
        public DeviceRegistrarAuthorityTrustLists(
            DeviceRegistrarAdminState administration,
            ISystemContext context,
            ICertificateManager certificateManager,
            IAuditEventServer auditServer,
            ITelemetryContext telemetry)
        {
            if (administration == null)
            {
                throw new ArgumentNullException(nameof(administration));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (certificateManager == null)
            {
                throw new ArgumentNullException(nameof(certificateManager));
            }
            if (auditServer == null)
            {
                throw new ArgumentNullException(nameof(auditServer));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (certificateManager is not ICertificateStoreResolver resolver)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Registrar authorities require a manager-scoped store resolver.");
            }
            if (administration.TicketAuthorities == null || administration.DeviceIdentityAuthorities == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            if (administration.TicketAuthorities.Handle != null ||
                administration.DeviceIdentityAuthorities.Handle != null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }

            m_ticketAuthorities = Bind(
                administration.TicketAuthorities, context, certificateManager,
                OnboardingTicketTrustProvider.TicketAuthorities, resolver, auditServer, telemetry);
            try
            {
                m_identityAuthorities = Bind(
                    administration.DeviceIdentityAuthorities, context, certificateManager,
                    OnboardingIdentityTrustProvider.DeviceIdentityAuthorities, resolver, auditServer, telemetry);
            }
            catch
            {
                m_ticketAuthorities.Dispose();
                administration.TicketAuthorities.Handle = null;
                throw;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_ticketAuthorities.Dispose();
            m_identityAuthorities.Dispose();
        }

        /// <summary>
        /// Uses independently owned store handles to capture managed locations without retaining their lifetime.
        /// </summary>
        private static TrustList Bind(
            TrustListState node,
            ISystemContext context,
            ICertificateManager certificateManager,
            TrustListIdentifier identifier,
            ICertificateStoreResolver resolver,
            IAuditEventServer auditServer,
            ITelemetryContext telemetry)
        {
            if (node.Writable == null || node.UserWritable == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            using ICertificateStore trusted = certificateManager.OpenTrustedStore(identifier);
            using ICertificateStore? issuers = certificateManager.OpenIssuerStore(identifier);
            if (issuers == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "Registrar authority administration requires an issuer store.");
            }
            RequireLocation(trusted);
            RequireLocation(issuers);
            ConfigurePermissions(node, context);

            var handler = new TrustList(
                node,
                new CertificateStoreIdentifier(trusted.StorePath, trusted.StoreType),
                new CertificateStoreIdentifier(issuers.StorePath, issuers.StoreType),
                RequireRegistrarAdmin,
                RequireRegistrarAdmin,
                telemetry,
                coordinator: null,
                maxTrustListSize: 0,
                maxTrustListSizeSafetyCeiling: 1024 * 1024,
                storeResolver: resolver);
            handler.SetAuditEventServer(auditServer);
            handler.SetTrustListChangeNotifier(certificateManager, identifier);
            node.Handle = handler;
            node.Writable!.Value = true;
            node.UserWritable!.Value = true;
            return handler;
        }

        /// <summary>
        /// Rejects an incomplete store descriptor rather than substituting an application peer store.
        /// </summary>
        private static void RequireLocation(ICertificateStore store)
        {
            if (string.IsNullOrWhiteSpace(store.StorePath) || string.IsNullOrWhiteSpace(store.StoreType))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
        }

        /// <summary>
        /// Requires an encrypted authenticated channel and the RegistrarAdmin role for reads and writes.
        /// </summary>
        private static void RequireRegistrarAdmin(ISystemContext context, CertificateStoreIdentifier _)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);
            AuthorizationHelper.GetClientCertificateFingerprint(context);
            if (context is not ISessionSystemContext { UserIdentity: { } identity, SessionId.IsNull: false })
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            NodeId role = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, context.NamespaceUris);
            if (role.IsNull || !identity.GrantedRoleIds.Contains(role))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Restricts the complete file-method subtree rather than only its parent object.
        /// </summary>
        private static void ConfigurePermissions(NodeState root, ISystemContext context)
        {
            NodeId role = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, context.NamespaceUris);
            if (role.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            var pending = new Stack<NodeState>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                NodeState node = pending.Pop();
                if (node.OnReadRolePermissions != null || node.OnReadUserRolePermissions != null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                node.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = role,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Call)
                    }
                ];
                node.UserRolePermissions = node.RolePermissions;
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// The owned TicketAuthorities file-service binding.
        /// </summary>
        private readonly TrustList m_ticketAuthorities;

        /// <summary>
        /// The owned DeviceIdentityAuthorities file-service binding.
        /// </summary>
        private readonly TrustList m_identityAuthorities;
    }
}
