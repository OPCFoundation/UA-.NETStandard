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
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Enforces OPC 10000-21 §4.2.6 using server channel evidence and approved DCA certificate bindings.
    /// </summary>
    public sealed class DeviceRegistrarAuthorization :
        IDeviceRegistrarAuthorization,
        IDeviceRegistrarCertificateAuthorization
    {
        /// <summary>
        /// Creates an authorization provider with explicit lifecycle and per-application policies.
        /// </summary>
        public DeviceRegistrarAuthorization(
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarApplicationAuthorization applications)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            m_applications = applications ?? throw new ArgumentNullException(nameof(applications));
            m_identities = lifecycle as IDeviceRegistrarIdentityLifecycle;
        }

        /// <summary>
        /// Creates authorization with explicit candidate storage shared with identity selection.
        /// </summary>
        public DeviceRegistrarAuthorization(
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarApplicationAuthorization applications,
            IDeviceRegistrarIdentityLifecycle identities)
            : this(lifecycle, applications)
        {
            m_identities = identities ?? throw new ArgumentNullException(nameof(identities));
        }

        /// <summary>
        /// Adds the explicitly configured alternate-authentication handoff to the ordinary lifecycle.
        /// The certificate manager must also trust the external issuer for channel establishment.
        /// </summary>
        public DeviceRegistrarAuthorization(
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarApplicationAuthorization applications,
            IDeviceRegistrarIdentityLifecycle identities,
            IApplicationsDatabase database,
            IDeviceRegistrarExternalAuthenticationProvider? externalAuthentication)
            : this(lifecycle, applications, identities)
        {
            m_database = database ?? throw new ArgumentNullException(nameof(database));
            m_externalAuthentication = externalAuthentication;
        }

        /// <inheritdoc/>
        public ValueTask RequireAuthenticatedChannelAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireAuthenticatedChannel(context);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RequireRoleAsync(
            ISystemContext context,
            ExpandedNodeId roleId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireAuthenticatedChannel(context);
            var session = (ISessionSystemContext)context;
            NodeId role = ExpandedNodeId.ToNodeId(roleId, context.NamespaceUris);
            if (role.IsNull || !session.UserIdentity!.GrantedRoleIds.Contains(role))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask<string> RequireDcaAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureChannelContext channel = await RequireDeviceChannelAsync(context, cancellationToken)
                .ConfigureAwait(false);
            string? device = await m_lifecycle.FindDeviceByCertificateAsync(
                channel.ClientChannelCertificate,
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(device))
            {
                if (m_identities == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                DeviceRegistrarIdentityCandidate? candidate = await m_identities.FindCandidateByCertificateAsync(
                    channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false);
                if (candidate == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                device = candidate.ProductInstanceUri;
            }
            return device;
        }

        /// <inheritdoc/>
        public async ValueTask RequireDcaCertificateIssuanceAsync(
            ISystemContext context,
            NodeId applicationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureChannelContext channel = await RequireDeviceChannelAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (m_identities == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            DeviceRegistrarIdentityCandidate? candidate = await m_identities.FindCandidateByCertificateAsync(
                channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false);
            if (candidate == null || candidate.ApplicationId != applicationId || applicationId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            if (!candidate.CertificateIssuanceAllowed)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> AuthorizeApplicationAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_lifecycle is not IDeviceRegistrarCertificateLifecycle certificates)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            DeviceRegistrarApplicationBinding? binding = await certificates.FindApplicationAsync(
                application.ApplicationId, cancellationToken).ConfigureAwait(false);
            if (binding == null)
            {
                return false;
            }
            if (binding.IsDca)
            {
                await RequireDcaCertificateIssuanceAsync(
                    context, application.ApplicationId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RequireManagedCertificateIssuanceAsync(
                    context, application, binding.ProtocolUri, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DeviceRegistrarApplicationBinding>> GetChannelApplicationsAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            SecureChannelContext channel = await RequireDeviceChannelAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (m_identities == null || m_lifecycle is not IDeviceRegistrarCertificateLifecycle certificates)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            DeviceRegistrarIdentityCandidate? candidate = await m_identities.FindCandidateByCertificateAsync(
                channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false);
            if (candidate == null)
            {
                return [];
            }
            if (!candidate.CertificateIssuanceAllowed)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            DeviceRegistrarDevice? device = await certificates.FindDeviceAsync(
                candidate.ProductInstanceUri, cancellationToken).ConfigureAwait(false);
            if (device == null || device.DcaApplicationId != candidate.ApplicationId)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            var bindings = new List<DeviceRegistrarApplicationBinding>
            {
                new(device.ProductInstanceUri, device.DcaApplicationId, true, string.Empty)
            };
            if (device.IsSingletonRestricted)
            {
                return bindings.ToArray().ToArrayOf();
            }
            for (int i = 0; i < device.Applications.Count; i++)
            {
                DeviceRegistrarApplication application = device.Applications[i];
                await RequireManagedScopeAsync(device.ProductInstanceUri, cancellationToken).ConfigureAwait(false);
                bindings.Add(new DeviceRegistrarApplicationBinding(
                    device.ProductInstanceUri, application.ApplicationId, false, application.ProtocolUri));
            }
            return bindings.ToArray().ToArrayOf();
        }

        /// <inheritdoc/>
        public async ValueTask RecordCertificateIssuedAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            if (certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            if (!await AuthorizeApplicationAsync(context, application, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            if (m_lifecycle is not IDeviceRegistrarCertificateLifecycle lifecycle)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            DeviceRegistrarApplicationBinding? binding = await lifecycle.FindApplicationAsync(
                application.ApplicationId, cancellationToken).ConfigureAwait(false);
            if (binding == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            if (binding.IsDca)
            {
                await lifecycle.RecordIssuedDcaCertificateAsync(
                    binding.ProductInstanceUri, application.ApplicationId, certificate, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask RequireManagedCertificateIssuanceAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            SecureChannelContext channel = await RequireDeviceChannelAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (m_identities == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            DeviceRegistrarIdentityCandidate? candidate = await m_identities.FindCandidateByCertificateAsync(
                channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false);
            if (candidate == null || application.ApplicationId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            if (!candidate.CertificateIssuanceAllowed)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            if (m_lifecycle is not IDeviceRegistrarCertificateLifecycle certificateLifecycle)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            DeviceRegistrarApplicationBinding? binding = await certificateLifecycle.FindApplicationAsync(
                application.ApplicationId, cancellationToken).ConfigureAwait(false);
            if (binding == null || binding.IsDca ||
                !string.Equals(binding.ProductInstanceUri, candidate.ProductInstanceUri, StringComparison.Ordinal) ||
                !string.Equals(binding.ProtocolUri, protocolUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            await RequireApplicationAsync(
                candidate.ProductInstanceUri, application, protocolUri, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RequireApplicationAsync(
            string productInstanceUri,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await RequireManagedScopeAsync(productInstanceUri, cancellationToken).ConfigureAwait(false);
            if (!await m_applications.CanManageAsync(
                productInstanceUri,
                application,
                protocolUri,
                cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            await RequireManagedScopeAsync(productInstanceUri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies persisted scope independently of per-application approval, including after credential promotion.
        /// </summary>
        private ValueTask RequireManagedScopeAsync(string productInstanceUri, CancellationToken cancellationToken)
        {
            if (m_lifecycle is not IDeviceRegistrarApplicationScopeLifecycle scope)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Managed applications require persisted device application scope.");
            }
            return scope.RequireManagedApplicationAllowedAsync(productInstanceUri, cancellationToken);
        }

        /// <summary>
        /// Recognizes an issued credential only when the server's authenticated channel proves its installation.
        /// Failure to persist that transition rejects the operation instead of returning an uncommitted privilege.
        /// </summary>
        private async ValueTask<SecureChannelContext> RequireDeviceChannelAsync(
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureChannelContext channel = RequireAuthenticatedChannel(context);
            if (m_lifecycle is IDeviceRegistrarCertificateLifecycle certificates)
            {
                await certificates.ConfirmDcaCertificateAsync(channel.ClientChannelCertificate, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (m_externalAuthentication != null && m_identities != null &&
                await m_identities.FindCandidateByCertificateAsync(
                    channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false) == null &&
                string.IsNullOrEmpty(await m_lifecycle.FindDeviceByCertificateAsync(
                    channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false)))
            {
                DeviceRegistrarExternalAuthenticationResult? external =
                    await m_externalAuthentication.EvaluateAsync(
                        channel.ClientChannelCertificate, cancellationToken).ConfigureAwait(false);
                if (external != null)
                {
                    if (m_database == null || m_lifecycle is not IDeviceRegistrarCertificateLifecycle lifecycle)
                    {
                        throw new ServiceResultException(StatusCodes.BadConfigurationError);
                    }
                    ApplicationRecordDataType application = external.Decision.Application;
                    ApplicationRecordDataType[]? existing =
                        m_database.FindApplications(application.ApplicationUri!);
                    if (existing is { Length: > 0 })
                    {
                        if (existing.Length != 1)
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState);
                        }
                        DeviceRegistrarApplicationBinding? owner = await lifecycle.FindApplicationAsync(
                            existing[0].ApplicationId, cancellationToken).ConfigureAwait(false);
                        if (owner == null || !owner.IsDca ||
                            owner.ProductInstanceUri != external.ProductInstanceUri)
                        {
                            throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                        }
                    }
                    await DeviceRegistrarRuntime.RegisterSharedApplicationAsync(
                        m_database, application, async (id, ct) =>
                        {
                            DeviceRegistrarApplicationBinding? binding =
                                await lifecycle.FindApplicationAsync(id, ct).ConfigureAwait(false);
                            if (binding != null &&
                                (!binding.IsDca || binding.ProductInstanceUri != external.ProductInstanceUri))
                            {
                                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                            }
                            await m_identities.RecordCandidateAsync(
                                external.ProductInstanceUri, id, channel.ClientChannelCertificate,
                                external.Decision.RequiresSoftwareUpdate, ct).ConfigureAwait(false);
                        }, cancellationToken).ConfigureAwait(false);
                }
            }
            return channel;
        }

        private static SecureChannelContext RequireAuthenticatedChannel(ISystemContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (context is not ISessionSystemContext session ||
                session.SessionId is not { IsNull: false } ||
                session.UserIdentity == null)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }

            SecureChannelContext? channel = context switch
            {
                SessionSystemContext { OperationContext: OperationContext operation } => operation.ChannelContext,
                SystemContext { OperationContext: OperationContext operation } => operation.ChannelContext,
                _ => null
            };
            if (channel?.EndpointDescription is not { } endpoint ||
                (endpoint.SecurityMode != MessageSecurityMode.Sign &&
                    endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt) ||
                string.IsNullOrEmpty(endpoint.SecurityPolicyUri) ||
                endpoint.SecurityPolicyUri == SecurityPolicies.None ||
                channel.ClientChannelCertificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
            return channel;
        }

        private readonly IDeviceRegistrarLifecycle m_lifecycle;
        private readonly IDeviceRegistrarApplicationAuthorization m_applications;
        private readonly IDeviceRegistrarIdentityLifecycle? m_identities;

        /// <summary>The shared application records used by the external certificate handoff.</summary>
        private readonly IApplicationsDatabase? m_database;

        /// <summary>The explicitly selected external issuer and device approval boundary.</summary>
        private readonly IDeviceRegistrarExternalAuthenticationProvider? m_externalAuthentication;
    }
}
