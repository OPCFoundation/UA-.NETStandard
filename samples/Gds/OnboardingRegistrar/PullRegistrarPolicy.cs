// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Accepts one operator-approved device for a client-only DCA without software or managed-application services.
    /// This sample mode rejects push scheduling instead of accepting work it cannot perform.
    /// </summary>
    internal sealed class PullRegistrarPolicy :
        IDeviceRegistrarOnboardingPolicy,
        IDeviceRegistrarExternalAuthenticationPolicy,
        IDeviceRegistrarApplicationAuthorization,
        IDeviceRegistrarPushScheduler,
        IDeviceRegistrarManagerProvider
    {
        /// <summary>
        /// Records the explicit product instance and DCA application URI approved by the operator.
        /// </summary>
        public PullRegistrarPolicy(string productInstanceUri, string applicationUri)
        {
            if (!Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute product instance URI is required.", nameof(productInstanceUri));
            }
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute DCA application URI is required.", nameof(applicationUri));
            }
            m_productInstanceUri = productInstanceUri;
            m_applicationUri = applicationUri;
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarOnboardingDecision> EvaluateAsync(
            OnboardingIdentitySelection selection,
            CancellationToken cancellationToken = default)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            RequireDevice(selection.ProductInstanceUri, cancellationToken);
            return new ValueTask<DeviceRegistrarOnboardingDecision>(CreateDecision());
        }

        /// <inheritdoc/>
        public ValueTask<DeviceRegistrarExternalAuthenticationResult> EvaluateAsync(
            ByteString certificate,
            ArrayOf<string> certificateUris,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (certificate.IsEmpty || !certificateUris.Contains(m_productInstanceUri) ||
                !certificateUris.Contains(m_applicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return new ValueTask<DeviceRegistrarExternalAuthenticationResult>(
                new DeviceRegistrarExternalAuthenticationResult(m_productInstanceUri, CreateDecision()));
        }

        /// <inheritdoc/>
        public ValueTask<bool> CanManageAsync(
            string productInstanceUri,
            ApplicationRecordDataType application,
            string protocolUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<bool>(false);
        }

        /// <inheritdoc/>
        public ValueTask EnqueueAsync(
            ApplicationDescription application,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "This registrar sample mode supports pull onboarding only.");
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            RequireDevice(productInstanceUri, cancellationToken);
            return new ValueTask<ArrayOf<ManagerDescription>>(ArrayOf<ManagerDescription>.Empty);
        }

        /// <summary>
        /// Creates the same operator-selected DCA metadata for ticket and external authentication.
        /// </summary>
        private DeviceRegistrarOnboardingDecision CreateDecision()
        {
            return new DeviceRegistrarOnboardingDecision(
                new ApplicationRecordDataType
                {
                    ApplicationUri = m_applicationUri,
                    ApplicationType = ApplicationType.Client,
                    ApplicationNames = [new LocalizedText("OnboardingClient")],
                    ProductUri = "uri:opcfoundation.org:UA-.NETStandard:OnboardingClient",
                    DiscoveryUrls = [],
                    ServerCapabilities = []
                },
                false,
                null);
        }

        /// <summary>
        /// A validated ticket is necessary but does not replace the operator's device admission decision.
        /// </summary>
        private void RequireDevice(string productInstanceUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(productInstanceUri, m_productInstanceUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>The product instance explicitly approved for this sample process.</summary>
        private readonly string m_productInstanceUri;

        /// <summary>The DCA application URI explicitly approved for that device.</summary>
        private readonly string m_applicationUri;
    }
}
