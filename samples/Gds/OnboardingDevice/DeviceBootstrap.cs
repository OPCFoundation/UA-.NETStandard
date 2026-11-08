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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;

namespace OnboardingDevice
{
    /// <summary>
    /// Verifies real factory key possession and signed ticket binding before the host may open listeners.
    /// It never creates a certificate, signing authority, ticket payload or replacement identity metadata.
    /// </summary>
    internal static class DeviceBootstrap
    {
        /// <summary>
        /// Reads bounded exact UTF-8 signed envelopes and assigns content-addressed runtime ticket identifiers.
        /// </summary>
        public static async ValueTask<ArrayOf<TicketInput>> ReadTicketsAsync(
            DeviceSampleOptions options,
            CancellationToken ct)
        {
            var result = new List<TicketInput>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < options.TicketFiles.Count; i++)
            {
                byte[] bytes = await ReadBoundedAsync(options.TicketFiles[i], ct).ConfigureAwait(false);
                string encoded = s_utf8.GetString(bytes);
                string id = Convert.ToHexString(SHA256.HashData(bytes));
                if (!identifiers.Add(id))
                {
                    throw new ArgumentException("Signed ticket inputs must not be duplicated.");
                }
                result.Add(new TicketInput(id, encoded, bytes));
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Initializes the shared manager without certificate auto-creation, verifies only this invocation's
        /// signed inputs, and preserves the original factory identity independently before server startup.
        /// Existing installed/resetting journals are left to production pre-start recovery, not recaptured.
        /// </summary>
        public static async ValueTask PrepareAsync(
            IServiceProvider services,
            DeviceSampleOptions options,
            ArrayOf<TicketInput> tickets,
            CancellationToken ct)
        {
            IOpcUaApplicationConfigurationProvider provider =
                services.GetRequiredService<IOpcUaApplicationConfigurationProvider>();
            provider.Application.DisableCertificateAutoCreation = true;
            ApplicationConfiguration configuration = await provider.GetAsync(ct).ConfigureAwait(false);
            ICertificateManager certificates = services.GetRequiredService<ICertificateManager>();
            await certificates.UpdateAsync(
                configuration.SecurityConfiguration, options.ApplicationUri, ct).ConfigureAwait(false);
            using CertificateEntry? active = certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            if (active == null || !active.Certificate.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "Preinstall the actual application certificate and private key in pkiRoot/own.");
            }
            byte[] expectedDer = await ReadBoundedAsync(options.FactoryCertificatePath, ct).ConfigureAwait(false);
            using Certificate expected = Certificate.FromRawData(expectedDer);
            if (!expected.RawData.AsSpan().SequenceEqual(expectedDer) ||
                !CertificateIdentifier.ValidateCertificateType(
                    expected, ObjectTypeIds.RsaSha256ApplicationCertificateType) ||
                !X509Utils.GetApplicationUrisFromCertificate(expected).Contains(options.ApplicationUri) ||
                !X509Utils.GetApplicationUrisFromCertificate(expected).Contains(options.ProductInstanceUri))
            {
                throw new InvalidDataException(
                    "factoryCertificate must be exact RSA application DER with both configured device URIs.");
            }

            var acceptedInputs = new MemoryTicketStore();
            IOnboardingTicketValidator validator = services.GetRequiredService<IOnboardingTicketValidator>();
            for (int i = 0; i < tickets.Count; i++)
            {
                TicketInput input = tickets[i];
                ValidatedOnboardingTicket accepted = await validator.ValidateAsync(
                    input.Encoded, OnboardingTicketSource.OutOfBand, ct).ConfigureAwait(false);
                await acceptedInputs.AddAsync(input.Id, input.Bytes, accepted.Metadata, ct).ConfigureAwait(false);
            }
            var selector = new OnboardingIdentitySelector(
                acceptedInputs, validator, services.GetRequiredService<IOnboardingIdentityTrustProvider>());
            OnboardingIdentitySelection selection = await selector.SelectAsync(
                [ByteString.From(expectedDer)], [], [], ct).ConfigureAwait(false);
            if (selection.ProductInstanceUri != options.ProductInstanceUri ||
                !selection.SelectedIdentity.Span.SequenceEqual(expectedDer))
            {
                throw new InvalidDataException("The offered signed tickets do not identify this factory device.");
            }

            ITelemetryContext telemetry = services.GetRequiredService<ITelemetryContext>();
            ProvisionableDeviceSecurityStage stage = await services
                .GetRequiredService<IProvisionableDeviceSecurity>().GetStageAsync(ct).ConfigureAwait(false);
            if (stage == ProvisionableDeviceSecurityStage.Unconfigured &&
                !active.Certificate.RawData.AsSpan().SequenceEqual(expectedDer))
            {
                throw new InvalidOperationException(
                    "An unconfigured journal cannot adopt an operational certificate as the factory identity.");
            }
            if (stage != ProvisionableDeviceSecurityStage.Unconfigured)
            {
                await services.GetRequiredService<DeviceSecurityCheckpointStore>()
                    .RequireDefaultsAsync(ct).ConfigureAwait(false);
            }
            using (ICertificateStore identityStore = new CertificateStoreIdentifier(
                options.IdentityStorePath, CertificateStoreType.Directory, noPrivateKeys: false).OpenStore(telemetry))
            {
                using CertificateCollection identities = await identityStore.EnumerateAsync(ct).ConfigureAwait(false);
                if (identities.Count == 0)
                {
                    if (stage != ProvisionableDeviceSecurityStage.Unconfigured ||
                        !active.Certificate.RawData.AsSpan().SequenceEqual(expectedDer))
                    {
                        throw new InvalidOperationException(
                            "A missing retained identity requires the actual unconfigured factory application key.");
                    }
                }
                else if (identities.Count != 1 || !identities[0].RawData.AsSpan().SequenceEqual(expectedDer))
                {
                    throw new InvalidDataException(
                        "The independent retained identity differs from factoryCertificate.");
                }
            }

            if (stage is ProvisionableDeviceSecurityStage.Unconfigured or ProvisionableDeviceSecurityStage.Configuring)
            {
                var defaults = (IProvisionableDeviceSecurityDefaults)services
                    .GetRequiredService<IProvisionableDeviceSecurityInstaller>();
                await defaults.PreserveDefaultsAsync(ct).ConfigureAwait(false);
            }
            using ICertificateStore retainedStore = new CertificateStoreIdentifier(
                options.IdentityStorePath, CertificateStoreType.Directory, noPrivateKeys: false).OpenStore(telemetry);
            using Certificate? retained = await retainedStore.LoadPrivateKeyAsync(
                expected.Thumbprint, subjectName: null, applicationUri: options.ApplicationUri,
                certificateType: ObjectTypeIds.RsaSha256ApplicationCertificateType,
                password: null, ct: ct).ConfigureAwait(false);
            if (retained == null || !retained.HasPrivateKey ||
                !retained.RawData.AsSpan().SequenceEqual(expectedDer) || !X509Utils.VerifyKeyPair(expected, retained))
            {
                throw new InvalidOperationException("The retained factory private key is missing or does not match.");
            }

            ITicketStore persistentTickets = services.GetRequiredService<ITicketStore>();
            await foreach (TicketRecord record in acceptedInputs.ListAsync(ct).ConfigureAwait(false))
            {
                await persistentTickets.AddAsync(
                    record.TicketId, record.EncodedTicket, record.Metadata, ct).ConfigureAwait(false);
            }
            await services.GetRequiredService<IProvisionableDeviceRegistrarStore>()
                .ReadAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads through an exclusive bounded stream so oversized or concurrently rewritten input cannot be accepted.
        /// </summary>
        private static async ValueTask<byte[]> ReadBoundedAsync(string path, CancellationToken ct)
        {
            using var input = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (input.Length is <= 0 or > kMaximumInputBytes)
            {
                throw new InvalidDataException("A signed ticket or factory DER input has an invalid size.");
            }
            byte[] bytes = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            byte[] trailing = new byte[1];
            if (await input.ReadAsync(trailing.AsMemory(), ct).ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException("An onboarding input changed while being read.");
            }
            return bytes;
        }

        /// <summary>
        /// Retains an unchanged signed input and its content-addressed identifier.
        /// </summary>
        internal sealed record TicketInput(string Id, string Encoded, byte[] Bytes);

        /// <summary>The maximum public signed input or DER certificate size.</summary>
        private const int kMaximumInputBytes = 1024 * 1024;

        /// <summary>Rejects replacement characters rather than repairing signed input.</summary>
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
