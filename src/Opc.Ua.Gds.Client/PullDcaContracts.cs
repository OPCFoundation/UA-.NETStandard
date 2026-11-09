/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// An explicitly selected registrar and its certificate directory on the same endpoint.
    /// </summary>
    public sealed record PullDcaRegistrar(
        Uri EndpointUrl,
        NodeId RegistrarId,
        NodeId CertificateDirectoryId);

    /// <summary>
    /// Bounded execution settings and configured registrar candidates.
    /// </summary>
    public sealed class PullDcaOptions
    {
        /// <summary>
        /// Gets or sets configured registrars, used when no discovery provider is supplied.
        /// </summary>
        public ArrayOf<PullDcaRegistrar> Registrars { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of FinishRequest calls per certificate request,
        /// shared by pending responses and retries of transport failures. Exhausted transport failures propagate.
        /// </summary>
        public int MaximumFinishAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay between pending certificate request checks and retries of transport failures.
        /// </summary>
        public TimeSpan FinishRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the cooperative deadline for each discovery or workflow operation.
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the maximum size of each downloaded trust list.
        /// </summary>
        public long MaximumTrustListSize { get; set; } = 16 * 1024 * 1024;
    }

    /// <summary>
    /// The certificates, issuer chains and encoded tickets supplied by the device.
    /// </summary>
    public sealed record PullDcaIdentityOffer(
        ArrayOf<ByteString> Identities,
        ArrayOf<ByteString> Issuers,
        ArrayOf<string> Tickets);

    /// <summary>
    /// The registrar's native identity selection.
    /// </summary>
    public sealed record PullDcaIdentitySelection(
        ByteString SelectedIdentity,
        BaseTicketType MatchingTicket,
        NodeId ApplicationId,
        ManagerDescription? SoftwareUpdateManager);

    /// <summary>
    /// A certificate group and certificate type explicitly selected by the host.
    /// </summary>
    public sealed record PullDcaCertificateProfile(NodeId CertificateGroupId, NodeId CertificateTypeId);

    /// <summary>
    /// One application to register and provision after the DCA is credentialled.
    /// </summary>
    public sealed record PullDcaManagedApplication(
        ApplicationRecordDataType Application,
        string ProtocolUri,
        PullDcaCertificateProfile CertificateProfile);

    /// <summary>
    /// Inputs for one pull onboarding operation; no lifecycle state is implicitly persisted.
    /// </summary>
    public sealed record PullDcaRequest(
        PullDcaIdentityOffer IdentityOffer,
        PullDcaCertificateProfile DcaCertificateProfile,
        ArrayOf<PullDcaManagedApplication> ManagedApplications);

    /// <summary>
    /// A locally generated key's issued certificate, issuer chain and downloaded trust list.
    /// Private keys remain owned by the credential provider.
    /// </summary>
    public sealed record PullDcaCredentials(
        ByteString Certificate,
        ArrayOf<ByteString> IssuerCertificates,
        TrustListDataType TrustList);

    /// <summary>
    /// Public signing output received from one certificate request; this does not establish local installation.
    /// </summary>
    public sealed record PullDcaCertificateReceipt(ByteString Certificate, ArrayOf<ByteString> IssuerCertificates);

    /// <summary>
    /// Retains received signing output before trust download or local credential installation can fail.
    /// The host restores the original key and channel identity before resuming the request.
    /// </summary>
    public interface IPullDcaCertificateReceiptStore
    {
        /// <summary>
        /// Returns immutable retained output or exclusively invokes receive and publishes its output before returning.
        /// The registrar, directory, application, request, and certificate profile must match the retained binding.
        /// Implementations serialize all writers sharing their storage, including concurrent workflow instances.
        /// An error or cancellation before publication cannot return success; published output must survive restart
        /// when the provider promises durable storage. A server-accepted response lost before local publication
        /// cannot be recovered by this interface and still requires server-side request recovery.
        /// </summary>
        ValueTask<PullDcaCertificateReceipt> GetOrCreateAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A managed application whose credentials were installed by the host provider.
    /// </summary>
    public sealed record PullDcaProvisionedApplication(NodeId ApplicationId, PullDcaCredentials Credentials);

    /// <summary>
    /// Successful completion of all requested installation and registration operations.
    /// </summary>
    public sealed record PullDcaResult(
        PullDcaIdentitySelection Identity,
        PullDcaCredentials DcaCredentials,
        ArrayOf<ManagerDescription> Managers,
        ArrayOf<PullDcaProvisionedApplication> ManagedApplications);

    /// <summary>
    /// Manager discovery and managed-application provisioning using an externally installed DCA certificate.
    /// The external service owns initial authentication and installation; no ticket selection is synthesized.
    /// </summary>
    public sealed record PullDcaExternalAuthenticationResult(
        ArrayOf<ManagerDescription> Managers,
        ArrayOf<PullDcaProvisionedApplication> ManagedApplications);

    /// <summary>
    /// Observable workflow stages, emitted before the corresponding operation.
    /// </summary>
    public enum PullDcaStage
    {
        /// <summary>
        /// Obtaining the registrar-owned connection.
        /// </summary>
        Connecting,

        /// <summary>
        /// Providing device identities and tickets.
        /// </summary>
        ProvidingIdentities,

        /// <summary>
        /// Installing and proving the selected SecureChannel identity.
        /// </summary>
        ActivatingIdentity,

        /// <summary>
        /// Waiting for the real software manager to complete its gate.
        /// </summary>
        UpdatingSoftware,

        /// <summary>
        /// Starting a certificate signing request.
        /// </summary>
        RequestingCertificate,

        /// <summary>
        /// Checking a specific outstanding certificate request.
        /// </summary>
        FinishingCertificate,

        /// <summary>
        /// Downloading the application's trust list.
        /// </summary>
        ReadingTrustList,

        /// <summary>
        /// Installing the issued credentials through the host.
        /// </summary>
        InstallingCredentials,

        /// <summary>
        /// Reconnecting with the newly installed DCA certificate.
        /// </summary>
        ActivatingDca,

        /// <summary>
        /// Obtaining managers accessible to the credentialled DCA.
        /// </summary>
        GettingManagers,

        /// <summary>
        /// Registering a managed application.
        /// </summary>
        RegisteringApplication,

        /// <summary>
        /// All requested operations succeeded.
        /// </summary>
        Completed
    }

    /// <summary>
    /// Typed progress, including request identifiers for host-owned recovery/checkpointing.
    /// Progress notification is not durable storage.
    /// </summary>
    public sealed record PullDcaProgress(PullDcaStage Stage, NodeId ApplicationId, NodeId RequestId);

    /// <summary>
    /// Discovers real registrar endpoints and resolves their model node identifiers.
    /// Implementations must honour cancellation and must not silently trust discovered servers.
    /// </summary>
    public interface IPullDcaRegistrarDiscovery
    {
        /// <summary>
        /// Returns registrar candidates for explicit host selection, without connecting to all candidates.
        /// </summary>
        ValueTask<ArrayOf<PullDcaRegistrar>> DiscoverAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Selects the offered certificate and its existing private key in the host's application configuration.
    /// </summary>
    public interface IPullDcaIdentityProvider
    {
        /// <summary>
        /// Installs/selects the exact identity returned by the registrar before the managed session reloads it.
        /// Throws when the certificate or corresponding private key is unavailable.
        /// </summary>
        ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Bridges to an actual software-update manager; the DCA does not impersonate SoftwareUpdateAdmin.
    /// </summary>
    public interface IPullDcaSoftwareUpdateProvider
    {
        /// <summary>
        /// Performs the required update and returns only once the authorized software manager has
        /// successfully reported UpdateSoftwareStatus to the registrar. Failures must throw.
        /// </summary>
        ValueTask CompleteAsync(
            PullDcaRegistrar registrar,
            PullDcaIdentitySelection identity,
            ManagerDescription manager,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns real key generation and certificate/trust installation for the DCA and managed applications.
    /// </summary>
    public interface IPullDcaCredentialProvider
    {
        /// <summary>
        /// Generates a CSR for a locally retained private key, using the existing certificate-manager infrastructure.
        /// </summary>
        ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Installs and validates issued credentials and trust through the host's certificate stores.
        /// For the DCA, updates the application configuration used by the borrowed managed session.
        /// Returns only after installation; a receipt without installation is not success.
        /// </summary>
        ValueTask InstallAsync(
            NodeId applicationId,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validates signing-request recovery against host-retained request and private-key state.
    /// No recovery provider is inferred from a credential installer.
    /// </summary>
    public interface IPullDcaSigningRecoveryProvider
    {
        /// <summary>
        /// Proves that a currently assigned certificate belongs to the original signing request.
        /// Implementations verify the retained request's registrar, application, group and type, possession of its
        /// private key, application URI, certificate validity and trust. They must reject stale assignments;
        /// matching a reused public key alone does not prove that the certificate resulted from this request.
        /// Missing or ambiguous retained evidence must throw without installing or accepting the certificate.
        /// The supplied trust is downloaded from the original authenticated registrar, not caller-selected trust.
        /// Returns only after validation, without changing active credentials.
        /// </summary>
        ValueTask ValidateAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional journaling capability of an explicitly registered <see cref="IPullDcaSigningRecoveryProvider"/>.
    /// The workflow calls it after a StartSigningRequest response returns a request identifier and before the
    /// first FinishRequest, so the provider can durably bind that request to its retained private key.
    /// </summary>
    public interface IPullDcaSigningRequestObserver
    {
        /// <summary>
        /// Records the registrar, application, profile and request identifier for the exact signing request.
        /// Throwing stops the workflow before FinishRequest; the request then stays pending at the registrar.
        /// </summary>
        ValueTask OnSigningRequestStartedAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            ByteString signingRequest,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A bounded request remains pending; identifiers allow the host to resume it without issuing another request.
    /// </summary>
    public sealed class PullDcaCertificatePendingException : Exception
    {
        /// <summary>
        /// Creates an exception without server-issued request identifiers.
        /// </summary>
        public PullDcaCertificatePendingException()
            : this(NodeId.Null, NodeId.Null)
        {
        }

        /// <summary>
        /// Creates an exception with a diagnostic message.
        /// </summary>
        public PullDcaCertificatePendingException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an exception with a diagnostic message and underlying cause.
        /// </summary>
        public PullDcaCertificatePendingException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a pending result with its server-issued identifiers.
        /// </summary>
        public PullDcaCertificatePendingException(NodeId applicationId, NodeId requestId)
            : base("The certificate request is still pending approval.")
        {
            ApplicationId = applicationId;
            RequestId = requestId;
        }

        /// <summary>
        /// Gets the registered application identifier.
        /// </summary>
        public NodeId ApplicationId { get; }

        /// <summary>
        /// Gets the existing request identifier; the host must retain it for recovery.
        /// </summary>
        public NodeId RequestId { get; }
    }
}
