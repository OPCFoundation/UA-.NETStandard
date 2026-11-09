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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Executes the registrar operations defined by OPC 10000-21 §9.2.3–9.2.8.
    /// </summary>
    public interface IDeviceRegistrarRuntime
    {
        /// <summary>
        /// Selects a validated identity and records a candidate with a shared certificate-manager identifier.
        /// Selection does not grant privilege to the caller's unrelated channel certificate.
        /// </summary>
        ValueTask<DeviceRegistrarIdentityResult> ProvideIdentitiesAsync(
            ISystemContext context,
            ArrayOf<ByteString> identities,
            ArrayOf<ByteString> issuers,
            ArrayOf<string> tickets,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the required or installed revision of a known device.
        /// </summary>
        ValueTask UpdateSoftwareStatusAsync(
            ISystemContext context,
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Enqueues push onboarding through the configured orchestration provider.
        /// </summary>
        ValueTask RegisterDeviceEndpointAsync(
            ISystemContext context,
            ApplicationDescription application,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns managers available to the authenticated DCA.
        /// </summary>
        ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Authorizes and registers one application in the shared certificate-manager database.
        /// </summary>
        ValueTask<NodeId> RegisterManagedApplicationAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Stores device authorization, software status, and certificate-manager application relations.
    /// Implementations must make updates atomic and preserve software status on identity refresh.
    /// Production deployments supply storage with their required persistence and replication guarantees.
    /// </summary>
    public interface IDeviceRegistrarLifecycle
    {
        /// <summary>
        /// Resolves an approved DCA certificate to its device, or returns null when no privilege was granted.
        /// The certificate is the proof supplied by the authenticated SecureChannel, not a caller argument.
        /// </summary>
        ValueTask<string?> FindDeviceByCertificateAsync(
            ByteString certificate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically changes software status; returns false when the product instance is unknown.
        /// This records the software manager's result and does not install firmware.
        /// </summary>
        ValueTask<bool> UpdateSoftwareStatusAsync(
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Associates an existing certificate-manager application identifier with the device.
        /// Repeated associations must retain software status and the same application identifier.
        /// </summary>
        ValueTask AssociateApplicationAsync(
            string productInstanceUri,
            NodeId applicationId,
            string protocolUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Schedules real push onboarding using the host's supervised orchestration infrastructure.
    /// </summary>
    public interface IDeviceRegistrarPushScheduler
    {
        /// <summary>
        /// Returns only after the request has been accepted for processing.
        /// The provider owns observation, retry, shutdown, and cancellation of queued work.
        /// Existing software-update providers perform any required firmware work.
        /// </summary>
        ValueTask EnqueueAsync(
            ApplicationDescription application,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Resolves managers accessible to a particular authenticated device.
    /// </summary>
    public interface IDeviceRegistrarManagerProvider
    {
        /// <summary>
        /// Returns manager descriptions filtered for this device.
        /// Software-update endpoints must refer to the existing software-update service.
        /// </summary>
        ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Decides whether a DCA may manage the specific application, independently of method access.
    /// </summary>
    public interface IDeviceRegistrarApplicationAuthorization
    {
        /// <summary>
        /// Checks an explicit device-to-application permission, DNS restriction, or approval.
        /// A role label alone is not sufficient authorization.
        /// </summary>
        ValueTask<bool> CanManageAsync(
            string productInstanceUri,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reports onboarding audit events through the server's event-authorization pipeline.
    /// </summary>
    public interface IDeviceRegistrarAuditReporter
    {
        /// <summary>
        /// Reports the accepted identity and exact signed ticket envelopes to SecurityAdmin sessions only.
        /// The event records validated identity selection, not possession of its private key.
        /// </summary>
        ValueTask ReportIdentityAcceptedAsync(
            ISystemContext context,
            OnboardingIdentitySelection selection,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports DeviceSoftwareUpdatedAuditEventType to SecurityAdmin sessions only.
        /// Implementations must apply ReceiveEvents permissions to this type and its subtypes.
        /// Audit failures propagate to the caller; they must not be silently discarded.
        /// </summary>
        ValueTask ReportSoftwareStatusAsync(
            ISystemContext context,
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Authorizes registrar calls using the current session and SecureChannel evidence.
    /// </summary>
    public interface IDeviceRegistrarAuthorization
    {
        /// <summary>
        /// Requires a real session and authenticated channel without requiring existing DCA privilege.
        /// </summary>
        ValueTask RequireAuthenticatedChannelAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requires an authenticated channel and session with the specified onboarding role.
        /// </summary>
        ValueTask RequireRoleAsync(
            ISystemContext context,
            ExpandedNodeId roleId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requires DCA privilege and returns the device proven by the current channel certificate.
        /// </summary>
        ValueTask<string> RequireDcaAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Independently authorizes the application, throwing BadRequestNotAllowed on denial.
        /// </summary>
        ValueTask RequireApplicationAsync(
            string productInstanceUri,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The native ProvideIdentities response.
    /// </summary>
    public sealed record DeviceRegistrarIdentityResult(
        ByteString SelectedIdentity,
        BaseTicketType MatchingTicket,
        NodeId ApplicationId,
        ManagerDescription? SoftwareUpdateManager);

    /// <summary>
    /// Host-authorized DCA registration and software-update decision.
    /// </summary>
    public sealed record DeviceRegistrarOnboardingDecision(
        ApplicationRecordDataType Application,
        bool RequiresSoftwareUpdate,
        ManagerDescription? SoftwareUpdateManager);

    /// <summary>
    /// Decides whether a validated device may be onboarded and supplies its actual DCA application description.
    /// </summary>
    public interface IDeviceRegistrarOnboardingPolicy
    {
        /// <summary>
        /// Authorizes device acceptance and determines software requirements using existing update infrastructure.
        /// Denied devices must raise a service error rather than return an accepting default.
        /// </summary>
        ValueTask<DeviceRegistrarOnboardingDecision> EvaluateAsync(
            OnboardingIdentitySelection selection,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A selected identity awaiting proof by a matching authenticated channel.
    /// </summary>
    public sealed record DeviceRegistrarIdentityCandidate(
        string ProductInstanceUri,
        NodeId ApplicationId,
        ByteString SelectedIdentity,
        bool CertificateIssuanceAllowed);

    /// <summary>
    /// Stores validated identity candidates independently of already-approved DCA certificate bindings.
    /// </summary>
    public interface IDeviceRegistrarIdentityLifecycle
    {
        /// <summary>
        /// Records a candidate without treating the selecting caller as the device.
        /// Retries preserve existing software state, application relations, and the shared application identifier.
        /// </summary>
        ValueTask RecordCandidateAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString selectedIdentity,
            bool requiresSoftwareUpdate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves an exact selected identity or confirmed DCA credential from an authenticated channel.
        /// The issuance decision reflects the current persisted software status.
        /// </summary>
        ValueTask<DeviceRegistrarIdentityCandidate?> FindCandidateByCertificateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Persists the monotonic OPC 10000-21 singleton application-scope restriction in the shared lifecycle.
    /// Providers without this capability cannot onboard singletons or authorize managed-application operations.
    /// </summary>
    public interface IDeviceRegistrarApplicationScopeLifecycle
    {
        /// <summary>
        /// Permanently restricts this device's DCA credentials to their own application.
        /// Repeated calls are idempotent; identity refresh, software changes and credential rotation never clear it.
        /// Existing managed relations remain recorded but cannot be used to configure their applications.
        /// </summary>
        ValueTask RestrictToDcaApplicationAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Throws BadRequestNotAllowed for restricted devices and BadNotFound for unknown devices.
        /// Implementations also enforce this gate atomically inside AssociateApplicationAsync.
        /// </summary>
        ValueTask RequireManagedApplicationAllowedAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A shared GDS identifier's persisted device owner and onboarding role.
    /// </summary>
    public sealed record DeviceRegistrarApplicationBinding(
        string ProductInstanceUri,
        NodeId ApplicationId,
        bool IsDca,
        string ProtocolUri);

    /// <summary>
    /// Resolves certificate-request ownership and records issued DCA credentials in the same lifecycle store.
    /// </summary>
    public interface IDeviceRegistrarCertificateLifecycle
    {
        /// <summary>
        /// Retains an issued credential separately from the active one until installation is proven.
        /// Repeating the same issuance is idempotent; another pending credential must not be overwritten.
        /// </summary>
        ValueTask RecordIssuedDcaCertificateAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Promotes a pending credential only after the caller obtains it from an authenticated channel.
        /// Supplying an offered certificate or an issuance response is not proof of installation.
        /// Returns true only when an installation transition was committed.
        /// </summary>
        ValueTask<bool> ConfirmDcaCertificateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the current device binding and approval state without sharing mutable storage.
        /// </summary>
        ValueTask<DeviceRegistrarDevice?> FindDeviceAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the current device owner of a shared GDS application identifier.
        /// Returns null for applications outside this registrar's lifecycle.
        /// </summary>
        ValueTask<DeviceRegistrarApplicationBinding?> FindApplicationAsync(
            NodeId applicationId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records the issued DCA certificate only after the caller has verified the candidate's authorization.
        /// This records issuance, not proof that the device installed the certificate.
        /// </summary>
        ValueTask RegisterDeviceAsync(
            string productInstanceUri,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An independent gate for the host's GDS certificate-request and issuance paths.
    /// </summary>
    public interface IDeviceRegistrarCertificateAuthorization
    {
        /// <summary>
        /// Returns the device and managed application relations proven by the current channel.
        /// Empty means that this channel is outside onboarding. Known devices must pass current software approval.
        /// The host rechecks application policy before using a managed relation.
        /// </summary>
        ValueTask<ArrayOf<DeviceRegistrarApplicationBinding>> GetChannelApplicationsAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Authorizes an application's persisted onboarding relation for certificate and trust-list access.
        /// Returns false only when the application is outside this registrar's lifecycle; the caller then
        /// applies its unchanged GDS authorization. A known relation with insufficient proof throws.
        /// </summary>
        ValueTask<bool> AuthorizeApplicationAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Rechecks the current lifecycle permission and records an issued DCA certificate before delivery.
        /// Applications outside this registrar's lifecycle retain the GDS's normal delivery behavior.
        /// </summary>
        ValueTask RecordCertificateIssuedAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            ByteString certificate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requires possession of the selected device key and software approval for the exact shared DCA identifier.
        /// Hosts must invoke this before accepting or completing DCA certificate requests.
        /// </summary>
        ValueTask RequireDcaCertificateIssuanceAsync(
            ISystemContext context,
            NodeId applicationId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Rechecks device proof, software approval, and the specific managed-application permission at issuance.
        /// The application record must be read from the shared GDS database, not accepted from request arguments.
        /// </summary>
        ValueTask RequireManagedCertificateIssuanceAsync(
            ISystemContext context,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default);
    }
}
