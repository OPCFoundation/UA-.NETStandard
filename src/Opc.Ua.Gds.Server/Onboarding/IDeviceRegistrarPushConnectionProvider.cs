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
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Opens host-authorized push connections without granting global trust to discovery certificates.
    /// </summary>
    public interface IDeviceRegistrarPushConnectionProvider
    {
        /// <summary>
        /// Discovers and selects an encrypted endpoint, authenticates the registrar, and returns an owned lease.
        /// Every call must create a fresh session exclusively owned by that lease, with no other users.
        /// Borrowed, shared, pooled and previously leased sessions are not supported, including on retries.
        /// An empty expected certificate requests bootstrap admission, not anonymous trust or auto-acceptance.
        /// A nonempty certificate requires a fresh authenticated channel proving that exact installed credential.
        /// The provider owns connection cleanup on failure before returning a lease. It must attempt session close
        /// and disposal, preserve the original and cleanup errors, and never reuse a session even if cleanup fails.
        /// It must not silently recover or replace the returned generation while an attempt is using it.
        /// </summary>
        ValueTask<IDeviceRegistrarPushConnection> ConnectAsync(
            ApplicationDescription application,
            ByteString expectedCertificate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An exclusively owned push session and model identifiers resolved in that session's namespace table.
    /// Disposal must attempt to close and dispose the session, preserve both errors if these operations fail,
    /// and permanently retire the session regardless of cleanup success. The session must never be reused.
    /// </summary>
    public interface IDeviceRegistrarPushConnection : IAsyncDisposable
    {
        /// <summary>
        /// Gets the fresh authenticated session owned exclusively by this lease.
        /// No other caller may use it, and the provider must not return it from another connection request.
        /// </summary>
        ISession Session { get; }

        /// <summary>
        /// Gets the selected offered endpoint; its certificate is never identity evidence.
        /// </summary>
        EndpointDescription OfferedEndpoint { get; }

        /// <summary>
        /// Gets the provisionable device object to which the selected endpoint belongs.
        /// </summary>
        NodeId ProvisionableDeviceId { get; }

        /// <summary>
        /// Gets the device's mandatory IsSingleton property resolved from its provisionable device object.
        /// </summary>
        NodeId IsSingletonId { get; }

        /// <summary>
        /// Gets the device configuration object used by generated push clients.
        /// </summary>
        NodeId ServerConfigurationId { get; }

        /// <summary>
        /// Gets the remote DCA application certificate group.
        /// </summary>
        NodeId CertificateGroupId { get; }

        /// <summary>
        /// Gets the concrete remote DCA certificate type.
        /// </summary>
        NodeId CertificateTypeId { get; }

        /// <summary>
        /// Gets the trust list belonging to the remote DCA application certificate group.
        /// </summary>
        NodeId TrustListId { get; }
    }

    /// <summary>
    /// Correlates push CSR creation and issuance with the host's existing approved GDS request infrastructure.
    /// There is deliberately no accepting default: the pull request gate requires a device client-channel proof,
    /// which a registrar's outbound connection cannot impersonate.
    /// </summary>
    public interface IDeviceRegistrarPushEnrollmentProvider
    {
        /// <summary>
        /// Finds retained evidence for this exact device and shared application identifier.
        /// Implementations retain the validated identity and request correlation across restart.
        /// </summary>
        ValueTask<DeviceRegistrarPushEnrollment?> FindAsync(
            string productInstanceUri,
            NodeId applicationId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns existing evidence or prepares one CSR and request through the existing certificate service.
        /// The provider must durably claim the device/application before invoking createSigningRequest, retain its
        /// CSR and request identifier, and recover approval/completion using ICertificateRequestIssuanceStore.
        /// Uncertain CSR or StartSigningRequest outcomes must fail for explicit recovery, not repeat key creation.
        /// It must not approve software, mint a second authority, forge a device session, or invent an issuance.
        /// A missing approval or receipt throws; it must never return success-shaped empty evidence.
        /// </summary>
        ValueTask<DeviceRegistrarPushEnrollment> GetOrCreateAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            Func<CancellationToken, ValueTask<ByteString>> createSigningRequest,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Routes managed application enrollment through the selected certificate profile without changing DCA defaults.
    /// </summary>
    public interface IDeviceRegistrarPushCertificateEnrollmentProvider : IDeviceRegistrarPushEnrollmentProvider
    {
        /// <summary>
        /// Retains the selected standard certificate type before creating a CSR and rejects a different type on retry.
        /// </summary>
        ValueTask<DeviceRegistrarPushEnrollment> GetOrCreateAsync(
            OnboardingIdentitySelection identity,
            NodeId applicationId,
            NodeId certificateTypeId,
            Func<CancellationToken, ValueTask<ByteString>> createSigningRequest,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Host-retained enrollment evidence; installation is proven independently by the actual channel certificate.
    /// </summary>
    public sealed record DeviceRegistrarPushEnrollment(
        OnboardingIdentitySelection Identity,
        NodeId ApplicationId,
        NodeId RequestId,
        string CertificateGroupId,
        string CertificateTypeId,
        TrustListDataType TrustList,
        ByteString CertificateRequest);
}
