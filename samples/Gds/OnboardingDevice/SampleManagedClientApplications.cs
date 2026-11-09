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
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace OnboardingDevice
{
    /// <summary>
    /// Publishes independently configured client applications beneath the sample's non-singleton device.
    /// </summary>
    internal sealed class SampleManagedClientApplicationsProvider :
        IProvisionableDeviceApplicationsProvider,
        IDisposable
    {
        /// <summary>
        /// Creates one independent client configuration and push authorization for each selected URI.
        /// </summary>
        public SampleManagedClientApplicationsProvider(
            DeviceSampleOptions options,
            ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(telemetry);

            var targets = new List<SampleManagedClientApplication>();
            try
            {
                var registrations = new List<ManagedApplicationPushRegistration>();
                foreach (string applicationUri in options.ManagedApplicationUris)
                {
                    var target = new SampleManagedClientApplication(
                        applicationUri,
                        options.ProductUri,
                        options.PkiRoot,
                        telemetry);
                    targets.Add(target);
                    registrations.Add(new ManagedApplicationPushRegistration(
                        target,
                        SampleManagedApplicationAuthorization.Instance));
                }
                m_targets = targets.ToArray();
                m_registrations = registrations.ToArrayOf();
            }
            catch (Exception error)
            {
                List<Exception>? disposalErrors = null;
                foreach (SampleManagedClientApplication target in targets)
                {
                    try
                    {
                        target.Dispose();
                    }
                    catch (Exception disposalError)
                    {
                        (disposalErrors ??= []).Add(disposalError);
                    }
                }
                if (disposalErrors is { Count: > 0 })
                {
                    throw new AggregateException([error, .. disposalErrors]);
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagedApplicationPushRegistration>> GetApplicationsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(m_registrations);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            List<Exception>? disposalErrors = null;
            foreach (SampleManagedClientApplication target in m_targets)
            {
                try
                {
                    target.Dispose();
                }
                catch (Exception error)
                {
                    (disposalErrors ??= []).Add(error);
                }
            }
            if (disposalErrors is { Count: 1 })
            {
                ExceptionDispatchInfo.Capture(disposalErrors[0]).Throw();
            }
            if (disposalErrors is { Count: > 1 })
            {
                throw new AggregateException(disposalErrors);
            }
        }

        /// <summary>The target application runtimes owned for the provider lifetime.</summary>
        private readonly SampleManagedClientApplication[] m_targets;

        /// <summary>The immutable registrations returned during address-space creation.</summary>
        private readonly ArrayOf<ManagedApplicationPushRegistration> m_registrations;
    }

    /// <summary>
    /// Holds one client application's independent configuration, certificate manager and directory stores.
    /// </summary>
    internal sealed class SampleManagedClientApplication : IApplicationConfigurationPushTarget, IDisposable
    {
        /// <summary>
        /// Creates a client configuration with private certificate and independent trust stores under the PKI root.
        /// </summary>
        public SampleManagedClientApplication(
            string applicationUri,
            string productUri,
            string pkiRoot,
            ITelemetryContext telemetry)
        {
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationUri)))
                .ToLowerInvariant();
            string root = Path.Combine(pkiRoot, "managed-applications", identity);
            string ownPath = Path.Combine(root, "own");
            string trustedPath = Path.Combine(root, "trusted");
            string issuersPath = Path.Combine(root, "issuers");
            var security = new SecurityConfiguration
            {
                ApplicationCertificates =
                [
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = ownPath,
                        SubjectName = $"CN=Onboarding Managed Client {identity[..12]}",
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ],
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = trustedPath
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = issuersPath
                },
                RejectedCertificateStore = new CertificateStoreIdentifier(
                    Path.Combine(root, "rejected"), CertificateStoreType.Directory)
            };
            Configuration = new ApplicationConfiguration
            {
                ApplicationName = $"OnboardingManagedClient-{identity[..12]}",
                ApplicationUri = applicationUri,
                ProductUri = productUri,
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = security,
                ServerConfiguration = new ServerConfiguration()
            };
            m_certificateManager = CertificateManagerFactory.Create(security, telemetry);
            Configuration.CertificateManager = m_certificateManager;
        }

        /// <inheritdoc/>
        public ApplicationConfiguration Configuration { get; }

        /// <inheritdoc/>
        public ArrayOf<EndpointDescription> Endpoints => [];

        /// <inheritdoc/>
        public ISecurityPolicyRegistry? SecurityPolicies => null;

        /// <inheritdoc/>
        public ValueTask ApplyCertificateChangesAsync(
            ArrayOf<NodeId> certificateGroups,
            ArrayOf<ApplicationCertificateRotation> previousCertificates,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ApplyTrustListChangesAsync(
            ArrayOf<TrustListChangeEffect> effects,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_certificateManager.Dispose();
        }

        /// <summary>The certificate manager configured for this application's private and trust stores.</summary>
        private readonly CertificateManager m_certificateManager;
    }

    /// <summary>
    /// Selects no additional application-specific policy; the device's standard registrar and software gates remain.
    /// </summary>
    internal sealed class SampleManagedApplicationAuthorization : IPushConfigurationAuthorization
    {
        /// <summary>
        /// Gets the singleton policy used by the sample's client applications.
        /// </summary>
        public static SampleManagedApplicationAuthorization Instance { get; } = new();

        /// <inheritdoc/>
        public ValueTask AuthorizeAsync(ISystemContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <summary>Creates the sample's no-additional-policy implementation.</summary>
        private SampleManagedApplicationAuthorization()
        {
        }
    }
}
