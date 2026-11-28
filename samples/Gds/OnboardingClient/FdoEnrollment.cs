// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Fdo.Enrollment;
using Fdo.Rendezvous;
using Fdo.Transport.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Fdo;
using Opc.Ua.Security.Certificates;

namespace OnboardingClientSample
{
    /// <summary>
    /// Enrolls a provisioned FDO device before the sample connects to its OPC UA registrar.
    /// </summary>
    internal static class FdoEnrollment
    {
        /// <summary>
        /// Runs Device TO1 and TO2 against the configured deployment and installs the issued DCA credential.
        /// </summary>
        internal static async Task RunAsync(
            IConfiguration settings, IServiceProvider services, CancellationToken ct)
        {
            Uri rendezvous = GetEndpoint(settings, "fdoRendezvous");
            Uri owner = GetEndpoint(settings, "fdoOwner");
            byte[] deviceGuid = Convert.FromHexString(Require(settings, "fdoDeviceGuid"));
            if (deviceGuid.Length != 16)
            {
                throw new ArgumentException("--fdoDeviceGuid must contain the 16 wire-order bytes in hex.");
            }
            byte[] manufacturerHash = Convert.FromHexString(Require(settings, "fdoManufacturerKeyHash"));
            string trustedPath = Require(settings, "fdoEnrollmentTrustedPath");
            string issuerPath = Require(settings, "fdoEnrollmentIssuerPath");
            using ICertificateStore attestationStore = new CertificateStoreIdentifier(
                Require(settings, "fdoAttestationStore"), CertificateStoreType.Directory, noPrivateKeys: false)
                .OpenStore(services.GetRequiredService<ITelemetryContext>()) ??
                throw new InvalidOperationException("The FDO attestation store could not be opened.");
            using Certificate attestation = await attestationStore.LoadPrivateKeyAsync(
                Require(settings, "fdoAttestationThumbprint"), null, null,
                ObjectTypeIds.EccNistP256ApplicationCertificateType, null, ct).ConfigureAwait(false) ??
                throw new InvalidOperationException("The provisioned FDO attestation certificate was not found.");
            using ECDsa attestationKey = attestation.GetECDsaPrivateKey() ??
                throw new InvalidOperationException("The FDO attestation certificate has no ECDSA private key.");

            ApplicationConfiguration application = await services
                .GetRequiredService<IOpcUaApplicationConfigurationProvider>().GetAsync(ct).ConfigureAwait(false);
            ICertificateManager certificates = application.CertificateManager ??
                throw new InvalidOperationException("The DCA certificate manager is unavailable.");
            using CertificateEntry dcaKey = certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new InvalidOperationException("Provision a retained RSA DCA key pair in the application store.");
            ByteString csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(dcaKey.Certificate));
            certificates.RegisterTrustList(s_enrollmentTrust, trustedPath, issuerPath);
            using TrustListData authority = await certificates
                .ReadTrustListAsync(s_enrollmentTrust, TrustListMasks.All, ct).ConfigureAwait(false);
            OnboardingDeviceCredentials credentials = services.GetRequiredService<OnboardingDeviceCredentials>();
            FdoDeviceEnrollmentHandler handler = FdoDeviceEnrollmentSetup.CreateHandler(
                credentials, dcaKey.Certificate, authority, ByteString.From(deviceGuid), csr);

            byte[] secret = Convert.FromBase64String(Environment.GetEnvironmentVariable("ONBOARDING_FDO_VOUCHER_SECRET")
                ?? throw new InvalidOperationException("Set ONBOARDING_FDO_VOUCHER_SECRET in the process environment."));
            try
            {
                if (secret.Length != 32)
                {
                    throw new ArgumentException("The provisioned FDO HMAC-SHA-256 secret must be 32 bytes.");
                }
                var lookup = new FdoDeviceRendezvousSession(new FdoDeviceRendezvousOptions
                {
                    DeviceGuid = deviceGuid,
                    AttestationKey = attestationKey,
                    MaxMessageSize = MaxMessageSize,
                    UseProvisionalPairedProfile = true
                });
                FdoDeviceRendezvousResult redirect = await FdoHttp.RunDeviceRendezvousAsync(
                    lookup, rendezvous, MaxMessageSize, ct).ConfigureAwait(false);
                using var device = new FdoDeviceSession(new FdoDeviceSessionOptions
                {
                    DeviceGuid = deviceGuid,
                    EncodedManufacturerKeyHash = manufacturerHash,
                    VoucherHmacAlgorithm = FdoDeviceHmacAlgorithm.HmacSha256,
                    VoucherHmacSecret = secret,
                    AttestationKey = attestationKey,
                    CertificateSigningRequest = csr.ToArray(),
                    KeyExchangeSuite = FdoPairedKeyExchangeSuite.Ecdh256,
                    CipherSuite = FdoPairedCipherSuite.A128Gcm,
                    RendezvousResult = redirect,
                    AllowRendezvousBypass = false,
                    AllowCredentialReuse = true,
                    UseProvisionalPairedProfile = true,
                    MaxMessageSize = MaxMessageSize,
                    MaxOwnerServiceInfoSize = 4096,
                    Metadata = new FdoDeviceMetadata("sample", "sample", "1", "opcua-dca", ":", "sample"),
                    EnrollmentHandler = handler
                });
                FdoDeviceEnrollment enrolled = await FdoHttp.RunDeviceAsync(device, owner, MaxMessageSize, ct)
                    .ConfigureAwait(false);
                using CertificateEntry? active = certificates.AcquireApplicationCertificateByType(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType);
                if (!device.DoneAckAuthenticated || active?.Certificate is not { HasPrivateKey: true } installed ||
                    !installed.RawData.AsSpan().SequenceEqual(enrolled.Certificate.Span))
                {
                    throw new InvalidOperationException("The FDO enrollment did not activate its exact DCA key pair.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        private static Uri GetEndpoint(IConfiguration settings, string key)
        {
            if (!Uri.TryCreate(Require(settings, key), UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            {
                throw new ArgumentException($"--{key} requires HTTPS or loopback HTTP without credentials or a query.");
            }
            return uri;
        }

        private static string Require(IConfiguration settings, string key)
        {
            string? value = settings[key];
            ArgumentException.ThrowIfNullOrWhiteSpace(value, "--" + key);
            return value;
        }

        private const ushort MaxMessageSize = 8192;
        private static readonly TrustListIdentifier s_enrollmentTrust = new("FdoEnrollmentAuthorities");
    }
}
