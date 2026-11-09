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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Implements registrar operations using shared certificate-manager storage and software-update orchestration.
    /// All providers are required and can be supplied directly or by the host's dependency-injection container.
    /// </summary>
    public sealed class DeviceRegistrarRuntime : IDeviceRegistrarRuntime
    {
        /// <summary>
        /// Creates a runtime over the shared GDS database and host-owned providers.
        /// </summary>
        public DeviceRegistrarRuntime(
            IApplicationsDatabase applications,
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarAuthorization authorization,
            IDeviceRegistrarPushScheduler pushScheduler,
            IDeviceRegistrarManagerProvider managers,
            IDeviceRegistrarAuditReporter audit)
        {
            m_applications = applications ?? throw new ArgumentNullException(nameof(applications));
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            m_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            m_pushScheduler = pushScheduler ?? throw new ArgumentNullException(nameof(pushScheduler));
            m_managers = managers ?? throw new ArgumentNullException(nameof(managers));
            m_audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        /// <summary>
        /// Creates a runtime with explicitly configured identity selection, candidate persistence, and software policy.
        /// </summary>
        public DeviceRegistrarRuntime(
            IApplicationsDatabase applications,
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarAuthorization authorization,
            IDeviceRegistrarPushScheduler pushScheduler,
            IDeviceRegistrarManagerProvider managers,
            IDeviceRegistrarAuditReporter audit,
            IOnboardingIdentitySelector identitySelector,
            IDeviceRegistrarIdentityLifecycle identityLifecycle,
            IDeviceRegistrarOnboardingPolicy onboardingPolicy)
            : this(applications, lifecycle, authorization, pushScheduler, managers, audit)
        {
            m_identitySelector = identitySelector ?? throw new ArgumentNullException(nameof(identitySelector));
            m_identityLifecycle = identityLifecycle ?? throw new ArgumentNullException(nameof(identityLifecycle));
            m_onboardingPolicy = onboardingPolicy ?? throw new ArgumentNullException(nameof(onboardingPolicy));
        }

        /// <summary>
        /// Gets the host's SoftwareUpdateAdmin role, identified by namespace URI rather than a startup index.
        /// Part 21 names this role, but the published onboarding model does not assign it a standard NodeId.
        /// Software status changes are rejected until the host selects and grants its actual role.
        /// </summary>
        public ExpandedNodeId SoftwareUpdateAdminRoleId { get; init; }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarIdentityResult> ProvideIdentitiesAsync(
            ISystemContext context,
            ArrayOf<ByteString> identities,
            ArrayOf<ByteString> issuers,
            ArrayOf<string> tickets,
            CancellationToken cancellationToken = default)
        {
            await m_authorization.RequireAuthenticatedChannelAsync(context, cancellationToken).ConfigureAwait(false);
            if (m_identitySelector == null || m_identityLifecycle == null || m_onboardingPolicy == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            IDeviceRegistrarIdentityLifecycle identityLifecycle = m_identityLifecycle;
            OnboardingIdentitySelection selection = await m_identitySelector.SelectAsync(
                identities, issuers, tickets, cancellationToken).ConfigureAwait(false);
            ByteString selected = selection.SelectedIdentity;
            if (!identities.Contains(selected) || selection.IsExternalAuthentication)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            DeviceRegistrarOnboardingDecision decision = await m_onboardingPolicy.EvaluateAsync(
                selection, cancellationToken).ConfigureAwait(false);
            if (decision.Application == null ||
                decision.RequiresSoftwareUpdate != (decision.SoftwareUpdateManager != null))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            if (decision.SoftwareUpdateManager is { } manager &&
                (manager.EndpointUrls.IsEmpty || string.IsNullOrWhiteSpace(manager.PurposeUri)))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            NodeId applicationId = await RegisterSharedApplicationAsync(
                m_applications,
                decision.Application,
                (id, ct) => identityLifecycle.RecordCandidateAsync(
                    selection.ProductInstanceUri, id, selected, decision.RequiresSoftwareUpdate, ct),
                cancellationToken).ConfigureAwait(false);
            await m_audit.ReportIdentityAcceptedAsync(context, selection, CancellationToken.None).ConfigureAwait(false);
            return new DeviceRegistrarIdentityResult(
                selected, selection.MatchingTicket, applicationId, decision.SoftwareUpdateManager);
        }

        /// <inheritdoc/>
        public async ValueTask UpdateSoftwareStatusAsync(
            ISystemContext context,
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SoftwareUpdateAdminRoleId.IsNull || SoftwareUpdateAdminRoleId.ServerIndex != 0 ||
                string.IsNullOrEmpty(SoftwareUpdateAdminRoleId.NamespaceUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The host must configure its SoftwareUpdateAdmin role.");
            }
            await m_authorization.RequireRoleAsync(
                context,
                SoftwareUpdateAdminRoleId,
                cancellationToken).ConfigureAwait(false);
            RequireUri(productInstanceUri);
            if (string.IsNullOrWhiteSpace(softwareRevision))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!await m_lifecycle.UpdateSoftwareStatusAsync(
                productInstanceUri,
                status,
                softwareRevision,
                cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }
            // Once committed, request cancellation must not suppress the required audit event.
            await m_audit.ReportSoftwareStatusAsync(
                context, productInstanceUri, status, softwareRevision, CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RegisterDeviceEndpointAsync(
            ISystemContext context,
            ApplicationDescription application,
            CancellationToken cancellationToken = default)
        {
            await m_authorization.RequireRoleAsync(
                context,
                Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin,
                cancellationToken).ConfigureAwait(false);
            if (application == null ||
                (application.ApplicationType != ApplicationType.Server &&
                    application.ApplicationType != ApplicationType.ClientAndServer) ||
                application.DiscoveryUrls.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            RequireUri(application.ApplicationUri);
            foreach (string endpoint in application.DiscoveryUrls)
            {
                RequireUri(endpoint);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await m_pushScheduler.EnqueueAsync(application, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            string device = await m_authorization.RequireDcaAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await m_managers.GetManagersAsync(device, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> RegisterManagedApplicationAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            string device = await m_authorization.RequireDcaAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            return await RegisterDeviceApplicationAsync(
                m_applications,
                m_lifecycle,
                m_authorization,
                device,
                application,
                protocolUri,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Authorizes and associates one managed application of an already identified device.
        /// The DCA call path supplies the device from its session; registrar push supplies the device
        /// whose installed DCA credential it has just proven. Authorization sees the final application metadata
        /// before credential verification or any existing registry entry is read.
        /// Singleton-restricted devices are rejected.
        /// </summary>
        internal static async ValueTask<NodeId> RegisterDeviceApplicationAsync(
            IApplicationsDatabase applications,
            IDeviceRegistrarLifecycle lifecycle,
            IDeviceRegistrarAuthorization authorization,
            string device,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken,
            Func<CancellationToken, ValueTask>? prepareAuthorizationRecord = null,
            Func<CancellationToken, ValueTask>? beforeRegistration = null)
        {
            if (application == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            if (lifecycle is not IDeviceRegistrarApplicationScopeLifecycle scope)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Managed applications require persisted device application scope.");
            }
            await scope.RequireManagedApplicationAllowedAsync(device, cancellationToken).ConfigureAwait(false);
            protocolUri ??= string.Empty;
            RequireUri(application.ApplicationUri);
            if (!string.IsNullOrEmpty(protocolUri))
            {
                RequireUri(protocolUri);
            }

            if (prepareAuthorizationRecord != null)
            {
                await prepareAuthorizationRecord(cancellationToken).ConfigureAwait(false);
            }

            // Authorize every call before even looking up an existing registry entry.
            await authorization.RequireApplicationAsync(
                device,
                application,
                protocolUri,
                cancellationToken).ConfigureAwait(false);
            if (beforeRegistration != null)
            {
                await beforeRegistration(cancellationToken).ConfigureAwait(false);
            }
            await scope.RequireManagedApplicationAllowedAsync(device, cancellationToken).ConfigureAwait(false);
            return await RegisterSharedApplicationAsync(
                applications,
                application,
                (id, ct) => lifecycle.AssociateApplicationAsync(device, id, protocolUri, ct),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects conflicting ownership before changing existing shared metadata.
        /// New entries retain their identifier if recording the lifecycle relation fails.
        /// </summary>
        internal static async ValueTask<NodeId> RegisterSharedApplicationAsync(
            IApplicationsDatabase applications,
            ApplicationRecordDataType application,
            Func<NodeId, CancellationToken, ValueTask> associate,
            CancellationToken cancellationToken)
        {
            RequireUri(application.ApplicationUri);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ApplicationsDatabaseBase.ValidateApplication(application);
            }
            catch (ArgumentException error)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, error);
            }

            ApplicationRecordDataType[]? existing = applications.FindApplications(application.ApplicationUri);
            NodeId applicationId;
            if (existing is { Length: > 0 })
            {
                if (existing.Length != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                applicationId = existing[0].ApplicationId;
                if (applicationId.IsNull ||
                    (!application.ApplicationId.IsNull && application.ApplicationId != applicationId))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                var update = new ApplicationRecordDataType
                {
                    ApplicationId = applicationId,
                    ApplicationUri = application.ApplicationUri,
                    ApplicationType = application.ApplicationType,
                    ApplicationNames = application.ApplicationNames,
                    ProductUri = application.ProductUri,
                    DiscoveryUrls = application.DiscoveryUrls,
                    ServerCapabilities = application.ServerCapabilities
                };
                await associate(applicationId, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                NodeId updatedId;
                try
                {
                    updatedId = applications.UpdateApplication(update);
                }
                catch (ArgumentException error)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, error);
                }
                if (updatedId != applicationId)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
            else
            {
                if (!application.ApplicationId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                try
                {
                    applicationId = applications.RegisterApplication(application);
                }
                catch (ArgumentException error)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, error);
                }
                if (applicationId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                await associate(applicationId, cancellationToken).ConfigureAwait(false);
            }

            return applicationId;
        }

        /// <summary>
        /// Requires an absolute application or product URI without changing its case-sensitive representation.
        /// </summary>
        private static void RequireUri([System.Diagnostics.CodeAnalysis.NotNull] string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
        }

        private readonly IApplicationsDatabase m_applications;
        private readonly IDeviceRegistrarLifecycle m_lifecycle;
        private readonly IDeviceRegistrarAuthorization m_authorization;
        private readonly IDeviceRegistrarPushScheduler m_pushScheduler;
        private readonly IDeviceRegistrarManagerProvider m_managers;
        private readonly IDeviceRegistrarAuditReporter m_audit;
        private readonly IOnboardingIdentitySelector? m_identitySelector;
        private readonly IDeviceRegistrarIdentityLifecycle? m_identityLifecycle;
        private readonly IDeviceRegistrarOnboardingPolicy? m_onboardingPolicy;
    }
}
