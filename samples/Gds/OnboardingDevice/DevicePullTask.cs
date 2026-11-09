/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace OnboardingDevice
{
    /// <summary>
    /// Initiates one explicitly configured pull operation after recovery and listener activation.
    /// Candidate metadata selects the connection; existing peer trust and the actual channel prove its identity.
    /// </summary>
    internal sealed class DevicePullTask : IServerStartupTask
    {
        /// <summary>
        /// Borrows the same configuration, security state and session ownership used by the hosted device.
        /// </summary>
        public DevicePullTask(
            DeviceSampleOptions options,
            IProvisionableDeviceSecurity security,
            IProvisionableDeviceRegistrarStore registrars,
            IProvisionableDeviceRuntime runtime,
            IOpcUaApplicationConfigurationProvider configuration,
            PullDcaWorkflow workflow,
            DevicePullSession sessions)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_security = security ?? throw new ArgumentNullException(nameof(security));
            m_registrars = registrars ?? throw new ArgumentNullException(nameof(registrars));
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            ProvisionableDeviceSecurityStage stage = await m_security.GetStageAsync(ct).ConfigureAwait(false);
            if (stage == ProvisionableDeviceSecurityStage.Installed)
            {
                Console.WriteLine("ONBOARDING_DEVICE_PULL_ALREADY_INSTALLED");
                return;
            }
            if (stage != ProvisionableDeviceSecurityStage.Unconfigured)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Complete or reset the pending device transition before outbound pull.");
            }
            Uri endpoint = m_options.PullRegistrarEndpoint ??
                throw new InvalidOperationException("The pull registrar endpoint is not configured.");
            string expectedApplicationUri = m_options.PullRegistrarApplicationUri ??
                throw new InvalidOperationException("The pull registrar application URI is not configured.");
            ArrayOf<ApplicationDescription> candidates = await m_registrars.ReadAsync(ct).ConfigureAwait(false);
            PullDcaRegistrarSelection.RequireConfiguredEndpoint(candidates, expectedApplicationUri, endpoint);
            DevicePullSession.Attempt attempt = await m_sessions.BeginAsync(ct).ConfigureAwait(false);
            await using var attemptLifetime = attempt.ConfigureAwait(false);
            ManagedSession session = attempt.Session;
            await RequireRegistrarIdentityAsync(session, expectedApplicationUri, ct).ConfigureAwait(false);

            var registrar = PullDcaRegistrarSelection.SelectConfigured(
                candidates, expectedApplicationUri, endpoint,
                ExpandedNodeId.ToNodeId(Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, session.NamespaceUris));
            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(
                    Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, session.NamespaceUris),
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            ApplicationConfiguration configuration = await m_configuration.GetAsync(ct).ConfigureAwait(false);
            ICertificateManager certificates = configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            using CertificateEntry identity = certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            var issuers = new ByteString[identity.IssuerChain.Count];
            for (int i = 0; i < issuers.Length; i++)
            {
                issuers[i] = ByteString.From(identity.IssuerChain[i].RawData.AsSpan());
            }
            ArrayOf<string> tickets = await m_runtime.RequestTicketsAsync(ct).ConfigureAwait(false);
            var request = new PullDcaRequest(
                new PullDcaIdentityOffer([ByteString.From(identity.Certificate.RawData.AsSpan())], issuers, tickets),
                profile, []);
            PullDcaResult result = await m_workflow.RunAsync(registrar, request, cancellationToken: ct)
                .ConfigureAwait(false);
            Console.WriteLine($"ONBOARDING_DEVICE_PULL_INSTALLED {result.Identity.ApplicationId}");
        }

        /// <summary>
        /// Rejects a different trusted application before disclosing the device's signed tickets.
        /// </summary>
        private static async ValueTask RequireRegistrarIdentityAsync(
            ManagedSession session,
            string expectedApplicationUri,
            CancellationToken ct)
        {
            SessionRequestContext context = await SessionRequestContext.CaptureConfidentialAsync(session, ct)
                .ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            ITransportChannel channel = context.Session.TransportChannel;
            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(
                channel.ServerChannelCertificate.AsMemory());
            if (chain.Count == 0 ||
                !string.Equals(channel.EndpointDescription.Server.ApplicationUri,
                    expectedApplicationUri, StringComparison.Ordinal) ||
                !X509Utils.GetApplicationUrisFromCertificate(chain[0]).Contains(expectedApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            if (context.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
            }
        }

        /// <summary>The explicit operator-selected connection inputs.</summary>
        private readonly DeviceSampleOptions m_options;

        /// <summary>The shared device transition owner.</summary>
        private readonly IProvisionableDeviceSecurity m_security;

        /// <summary>The persisted candidate metadata, which does not grant peer trust.</summary>
        private readonly IProvisionableDeviceRegistrarStore m_registrars;

        /// <summary>The verified signed ticket source.</summary>
        private readonly IProvisionableDeviceRuntime m_runtime;

        /// <summary>The host-owned shared application configuration.</summary>
        private readonly IOpcUaApplicationConfigurationProvider m_configuration;

        /// <summary>The typed workflow over generated method clients.</summary>
        private readonly PullDcaWorkflow m_workflow;

        /// <summary>The active attempt's session owner.</summary>
        private readonly DevicePullSession m_sessions;
    }
}
