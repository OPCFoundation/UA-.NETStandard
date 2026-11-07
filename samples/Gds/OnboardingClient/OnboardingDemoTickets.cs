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
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace OnboardingClientSample
{
    /// <summary>
    /// Prepares signed demo tickets and explicit local-demo certificate trust.
    /// </summary>
    internal static class OnboardingDemoTickets
    {
        /// <summary>
        /// Creates fresh keys in the client certificate store and imports only the
        /// public ticket signer into the explicitly selected registrar trust store.
        /// </summary>
        internal static async Task PrepareAsync(
            string clientPkiRoot,
            string registrarPkiRoot,
            string registrarTrustPath,
            string? untrustedClientPkiRoot,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            string signingStorePath = Path.Combine(clientPkiRoot, "ticket-signing");
            using var manager = new CertificateManager(telemetry);
            await PrepareChannelTrustAsync(
                clientPkiRoot, registrarPkiRoot, untrustedClientPkiRoot, manager, ct).ConfigureAwait(false);
            using Certificate signer = CertificateBuilder.Create("CN=Onboarding Demo Ticket Signer")
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate deviceAuthority = CertificateBuilder.Create("CN=Onboarding Demo Device Authority")
                .SetCAConstraint(0)
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using (ICertificateStore signingStore = manager.OpenCertificateStore(
                signingStorePath, CertificateStoreType.Directory, noPrivateKeys: false))
            {
                await signingStore.AddAsync(signer, ct: ct).ConfigureAwait(false);
                await signingStore.AddAsync(deviceAuthority, ct: ct).ConfigureAwait(false);
            }
            using (ICertificateStore trustedStore = manager.OpenCertificateStore(
                registrarTrustPath, CertificateStoreType.Directory))
            using (Certificate publicSigner = Certificate.FromRawData(signer.RawData))
            {
                await trustedStore.AddAsync(publicSigner, ct: ct).ConfigureAwait(false);
            }
            var identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = signingStorePath,
                Thumbprint = signer.Thumbprint
            };
            var writer = new OnboardingTicketWriter(
                manager.CertificateProvider, ServiceMessageContext.Create(telemetry));
            for (int i = 1; i <= 2; i++)
            {
                string encodedTicket = await writer.WriteAsync(
                    new DeviceIdentityTicketType
                    {
                        ManufacturerName = "OPC Foundation Demo",
                        ModelName = "Onboarding demonstration device",
                        ModelVersion = "1",
                        SerialNumber = "Demo-" + i,
                        ProductInstanceUri = "urn:opcfoundation:onboarding-demo:device:" + i,
                        Authorities =
                        [
                            new CertificateAuthorityType
                            {
                                AuthorityCertificate = ByteString.From(deviceAuthority.RawData.AsSpan())
                            }
                        ]
                    },
                    identifier,
                    ct: ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(clientPkiRoot, $"ticket-{i}.json"), encodedTicket, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Generates both local applications' keys before startup and exchanges only their public certificates.
        /// This is out-of-band demo setup, not a discovery-time trust decision.
        /// </summary>
        private static async Task PrepareChannelTrustAsync(
            string clientPkiRoot,
            string registrarPkiRoot,
            string? untrustedClientPkiRoot,
            CertificateManager manager,
            CancellationToken ct)
        {
            using Certificate client = CreateApplicationCertificate("OnboardingClient");
            using Certificate registrar = CreateApplicationCertificate("OnboardingRegistrar");
            using (ICertificateStore clientStore = manager.OpenCertificateStore(
                clientPkiRoot, CertificateStoreType.Directory, noPrivateKeys: false))
            using (ICertificateStore registrarStore = manager.OpenCertificateStore(
                registrarPkiRoot, CertificateStoreType.Directory, noPrivateKeys: false))
            {
                using CertificateCollection existingClient = await clientStore.EnumerateAsync(ct).ConfigureAwait(false);
                using CertificateCollection existingRegistrar = await registrarStore.EnumerateAsync(ct)
                    .ConfigureAwait(false);
                if (existingClient.Count != 0 || existingRegistrar.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Demo preparation requires fresh application certificate stores; choose new PKI roots.");
                }
                await clientStore.AddAsync(client, ct: ct).ConfigureAwait(false);
                await registrarStore.AddAsync(registrar, ct: ct).ConfigureAwait(false);
            }
            if (untrustedClientPkiRoot != null)
            {
                using ICertificateStore untrustedClientStore = manager.OpenCertificateStore(
                    untrustedClientPkiRoot, CertificateStoreType.Directory, noPrivateKeys: false);
                using CertificateCollection existing = await untrustedClientStore.EnumerateAsync(ct)
                    .ConfigureAwait(false);
                if (existing.Count != 0)
                {
                    throw new InvalidOperationException(
                        "The client rejection check also requires a fresh application certificate store.");
                }
                // Preserve the channel peer the registrar trusts; only the client's registrar trust is absent.
                await untrustedClientStore.AddAsync(client, ct: ct).ConfigureAwait(false);
            }
            using (ICertificateStore clientTrust = manager.OpenCertificateStore(
                Path.Combine(clientPkiRoot, "trusted"), CertificateStoreType.Directory))
            using (ICertificateStore registrarTrust = manager.OpenCertificateStore(
                Path.Combine(registrarPkiRoot, "trusted"), CertificateStoreType.Directory))
            using (Certificate publicClient = Certificate.FromRawData(client.RawData))
            using (Certificate publicRegistrar = Certificate.FromRawData(registrar.RawData))
            {
                await clientTrust.AddAsync(publicRegistrar, ct: ct).ConfigureAwait(false);
                await registrarTrust.AddAsync(publicClient, ct: ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Matches the fluent host's application identity and includes local endpoint names.
        /// </summary>
        private static Certificate CreateApplicationCertificate(string name)
        {
            return DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                $"urn:localhost:OPCFoundation:{name}",
                name,
                Utils.ReplaceDCLocalhost($"CN={name}, O=OPC Foundation, DC=localhost"),
                ["localhost", Utils.GetHostName()])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }
    }
}
