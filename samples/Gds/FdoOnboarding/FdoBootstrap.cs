// Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Fdo.Enrollment;
using Fdo.Initialization;
using Fdo.Ownership;
using Fdo.Rendezvous;
using Fdo.Serialization;
using Fdo.Transport.Http;

namespace FdoOnboarding
{
    /// <summary>
    /// Single-device manufacturing and Rendezvous state for the loopback demonstration.
    /// </summary>
    internal sealed class FdoBootstrap :
        IFdoDeviceInitializeHandler, IFdoManufacturerInitializeHandler, IFdoDeviceInitializeHmacSecret,
        IFdoRendezvousRegistrationStore, IDisposable
    {
        /// <summary>
        /// Creates per-run manufacturing, Owner and attestation keys. None are shared with the UA issuer.
        /// </summary>
        public FdoBootstrap(Uri address)
        {
            m_address = address;
            using ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ca = new CertificateRequest("CN=Demo Attestation CA", rootKey, HashAlgorithmName.SHA256);
            ca.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            ca.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using X509Certificate2 signingRoot = ca.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
            m_rootDer = signingRoot.RawData;

            var device = new CertificateRequest("CN=Demo FDO Device", DeviceKey, HashAlgorithmName.SHA256);
            device.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            device.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using X509Certificate2 leaf = device.Create(
                signingRoot, now.AddMinutes(-4), now.AddHours(1), RandomNumberGenerator.GetBytes(16));
            Attestation = leaf.RawData;
            ManufacturerTrust = new FdoPublicKeyCodec().Encode(m_manufacturerKey);
        }

        /// <summary>Gets the Device attestation private key.</summary>
        public ECDsa DeviceKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        /// <summary>Gets the approved Owner's signing key.</summary>
        public ECDsa OwnerKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        /// <summary>Gets the locally trusted manufacturer key encoding.</summary>
        public ReadOnlyMemory<byte> ManufacturerTrust { get; }
        /// <summary>Gets the approved Device's attestation leaf.</summary>
        public ReadOnlyMemory<byte> Attestation { get; }
        /// <summary>Gets the local attestation root.</summary>
        public ReadOnlyMemory<byte> AttestationRoot => m_rootDer;
        /// <inheritdoc/>
        public FdoDeviceHmacAlgorithm Algorithm => FdoDeviceHmacAlgorithm.HmacSha256;
        /// <inheritdoc/>
        public int KeySizeInBytes => m_voucherSecret.Length;

        /// <summary>
        /// Initializes the Device, transfers the voucher to the approved Owner, and retrieves the Owner redirect.
        /// </summary>
        public async Task<BootstrappedDevice> RunAsync(LoopbackFdoHost http, CancellationToken ct)
        {
            using var di = new FdoManufacturerInitializeSession(new FdoManufacturerInitializeOptions
            {
                Handler = this,
                MaxMessageSize = MaxMessageSize,
                UseVoucherHeaderHmac = true
            });
            var device = new FdoDeviceInitializeSession(new FdoDeviceInitializeOptions
            {
                AttestationKey = DeviceKey,
                Handler = this,
                MaxMessageSize = MaxMessageSize,
                UseVoucherHeaderHmac = true
            });
            using var diBinding = FdoHttpServerSession.ForManufacturer(di, MaxMessageSize);
            FdoDeviceInitializeCredential credential = await http.RunAsync(diBinding,
                token => FdoHttp.RunDeviceInitializeAsync(device, m_address, MaxMessageSize, token), ct)
                .ConfigureAwait(false);
            Require(di.DoneHandoffConfirmed && ReferenceEquals(m_installed, credential),
                "The Device did not complete initialization.");

            using ECDsa ownerPublic = ECDsa.Create(OwnerKey.ExportParameters(false));
            FdoTransferredOwnershipVoucher voucher = FdoOwnershipVoucherTransfer.Extend(
                (m_voucher ?? throw new InvalidOperationException("The manufacturer did not retain the voucher."))
                    .EncodedVoucher, m_manufacturerKey, ownerPublic);
            IReadOnlyList<FdoRendezvousServerCandidate> ownerTargets = FdoRendezvousInfo.ParseVoucherHeader(
                voucher.EncodedVoucherHeaderTag, FdoRendezvousProtocol.OwnerRegistration);
            Require(ownerTargets.Count == 1 && ownerTargets[0].BaseAddress.AbsoluteUri == "https://rvserver.local/",
                "Unexpected Owner Rendezvous configuration.");
            Uri deviceTarget = FdoRendezvousInfo.Parse(
                    credential.EncodedRendezvousInfo, FdoRendezvousProtocol.DeviceLookup)
                .Single(candidate => candidate.BaseAddress == m_address).BaseAddress;

            var registeringOwner = new FdoOwnerRendezvousSession(new FdoOwnerRendezvousOptions
            {
                EncodedVoucherHeaderTag = voucher.EncodedVoucherHeaderTag,
                EncodedVoucherHeaderHmac = voucher.EncodedVoucherHeaderHmac,
                EncodedVoucherEntries = voucher.EncodedVoucherEntries,
                EncodedDeviceCertificateChain = voucher.EncodedDeviceCertificateChain,
                TrustedManufacturerKey = ManufacturerTrust,
                DeviceTrustAnchor = m_rootDer,
                DeviceRevocationMode = X509RevocationMode.NoCheck,
                OwnerSigningKey = OwnerKey,
                OwnerAddress = new FdoRendezvousAddress(IPAddress.Loopback.GetAddressBytes(), null,
                    (ushort)m_address.Port, FdoRendezvousTransportProtocol.Http),
                WaitSeconds = 120,
                MaxMessageSize = MaxMessageSize,
                UseProvisionalPairedProfile = true
            });
            using var registrationRole = CreateRendezvous(FdoRendezvousProtocol.OwnerRegistration);
            using var registration = FdoHttpServerSession.ForRendezvous(registrationRole, MaxMessageSize);
            // This local demo maps its configured rvserver.local to the ephemeral loopback listener.
            uint accepted = await http.RunAsync(registration,
                token => FdoHttp.RunOwnerRendezvousAsync(registeringOwner, m_address, MaxMessageSize, token), ct)
                .ConfigureAwait(false);
            Require(accepted > 0 && m_registration != null, "TO0 did not store an Owner registration.");

            var lookingUpDevice = new FdoDeviceRendezvousSession(new FdoDeviceRendezvousOptions
            {
                DeviceGuid = credential.DeviceGuid,
                AttestationKey = DeviceKey,
                MaxMessageSize = MaxMessageSize,
                UseProvisionalPairedProfile = true
            });
            using var lookupRole = CreateRendezvous(FdoRendezvousProtocol.DeviceLookup);
            using var lookup = FdoHttpServerSession.ForRendezvous(lookupRole, MaxMessageSize);
            FdoDeviceRendezvousResult redirect = await http.RunAsync(lookup,
                token => FdoHttp.RunDeviceRendezvousAsync(lookingUpDevice, deviceTarget, MaxMessageSize, token), ct)
                .ConfigureAwait(false);
            FdoRendezvousAddress candidate = redirect.CandidateAddress;
            Require(candidate.Protocol == FdoRendezvousTransportProtocol.Http && candidate.Port == m_address.Port &&
                candidate.IpAddress is { } ip && new IPAddress(ip.Span).Equals(IPAddress.Loopback) &&
                redirect.SignedRedirect.Span.SequenceEqual(m_registration!.SignedRedirect.Span),
                "TO1 did not return the registered loopback Owner.");
            return new BootstrappedDevice(credential, voucher, redirect);
        }

        /// <inheritdoc/>
        public ValueTask<IFdoDeviceInitializeHmacSecret> AuthorizeAndReserveAsync(
            Guid runId, IFdoDeviceInitializeTransport transport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(!m_reserved && transport is IFdoHttpPeer peer && peer.BaseAddress == m_address,
                "The manufacturing endpoint is not approved or the Device is already reserved.");
            m_reserved = true;
            return ValueTask.FromResult<IFdoDeviceInitializeHmacSecret>(this);
        }

        /// <inheritdoc/>
        public ValueTask StageAsync(FdoDeviceInitializeCredential credential, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(m_reserved && m_staged == null && credential.DeviceInfo == DeviceInfo,
                "Unexpected manufacturing credential.");
            m_staged = credential;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask InstallAsync(FdoDeviceInitializeCredential credential, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(ReferenceEquals(m_staged, credential) && m_installed == null,
                "The Device did not stage this manufacturing credential.");
            m_installed = credential;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask<FdoManufacturerInitializeProposal> AuthorizeAndPrepareAsync(
            FdoManufacturerInitializeRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(!m_proposed, "The manufacturer has already authorized its single Device.");
            m_proposed = true;
            return ValueTask.FromResult(FdoManufacturerInitializeProposal.Create(
                DeviceInfo, m_manufacturerKey, [Attestation, m_rootDer], EncodeRendezvousConfiguration()));
        }

        /// <inheritdoc/>
        public ValueTask PersistVoucherAsync(
            FdoManufacturerInitializeVoucher voucher, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(m_proposed && m_voucher == null, "Unexpected manufacturing voucher.");
            m_voucher = voucher;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask<ReadOnlyMemory<byte>> ComputeAsync(
            ReadOnlyMemory<byte> encodedHeaderContents, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(
                HMACSHA256.HashData(m_voucherSecret, encodedHeaderContents.Span));
        }

        /// <inheritdoc/>
        public ValueTask StoreAsync(FdoRendezvousRegistration registration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(m_registration == null && m_installed != null &&
                registration.DeviceGuid.Span.SequenceEqual(m_installed.DeviceGuid.Span),
                "The Rendezvous registration is not for the approved Device.");
            m_registration = registration;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask<FdoRendezvousRegistration?> FindAsync(
            ReadOnlyMemory<byte> deviceGuid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(m_registration is { } registration &&
                registration.DeviceGuid.Span.SequenceEqual(deviceGuid.Span) ? registration : null);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(m_voucherSecret);
            m_manufacturerKey.Dispose();
            DeviceKey.Dispose();
            OwnerKey.Dispose();
        }

        /// <summary>
        /// Constructs the paired Rendezvous role with only the locally provisioned manufacturing trust.
        /// </summary>
        private FdoRendezvousServerSession CreateRendezvous(FdoRendezvousProtocol protocol)
        {
            return new FdoRendezvousServerSession(new FdoRendezvousServerOptions
            {
                TrustedManufacturerKey = ManufacturerTrust,
                DeviceTrustAnchor = m_rootDer,
                DeviceRevocationMode = X509RevocationMode.NoCheck,
                RegistrationStore = this,
                MaxWaitSeconds = 120,
                MaxMessageSize = MaxMessageSize,
                UseProvisionalPairedProfile = true
            }, protocol);
        }

        /// <summary>
        /// Supplies this demo's voucher configuration; voucher framing and protocol validation belong to FDO.
        /// </summary>
        private byte[] EncodeRendezvousConfiguration()
        {
            var writer = new CborWriter(CborConformanceMode.Canonical);
            writer.WriteStartArray(2);
            writer.WriteStartArray(1);
            WriteDirective(5, value => value.WriteTextString("rvserver.local"));
            writer.WriteEndArray();
            writer.WriteStartArray(4);
            WriteDirective(0, value => value.WriteNull());
            WriteDirective(2, value => value.WriteByteString(IPAddress.Loopback.GetAddressBytes()));
            WriteDirective(3, value => value.WriteUInt32((uint)m_address.Port));
            WriteDirective(12, value => value.WriteUInt32(RendezvousDirectiveHttp));
            writer.WriteEndArray();
            writer.WriteEndArray();
            return writer.Encode();

            void WriteDirective(int code, Action<CborWriter> valueWriter)
            {
                var value = new CborWriter(CborConformanceMode.Canonical);
                valueWriter(value);
                writer.WriteStartArray(2);
                writer.WriteInt32(code);
                writer.WriteByteString(value.Encode());
                writer.WriteEndArray();
            }
        }

        /// <summary>
        /// Rejects inconsistent bootstrap state instead of proceeding with a different endpoint or credential.
        /// </summary>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>The local maximum for FDO messages, in bytes.</summary>
        public const ushort MaxMessageSize = 8192;
        /// <summary>The Device information expected by the local manufacturing policy.</summary>
        private const string DeviceInfo = "opc-ua-onboarding-demo";
        /// <summary>HTTP in the RendezvousInfo protocol directive, not in an Owner address.</summary>
        private const uint RendezvousDirectiveHttp = 1;
        /// <summary>The sole permitted loopback endpoint for this run.</summary>
        private readonly Uri m_address;
        /// <summary>The retained public attestation root certificate.</summary>
        private readonly byte[] m_rootDer;
        /// <summary>The manufacturing voucher signing key.</summary>
        private readonly ECDsa m_manufacturerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        /// <summary>The Device-only voucher MAC secret.</summary>
        private readonly byte[] m_voucherSecret = RandomNumberGenerator.GetBytes(32);
        /// <summary>The Device initialization reservation.</summary>
        private bool m_reserved;
        /// <summary>The manufacturer initialization reservation.</summary>
        private bool m_proposed;
        /// <summary>The staged Device initialization credential.</summary>
        private FdoDeviceInitializeCredential? m_staged;
        /// <summary>The installed Device initialization credential.</summary>
        private FdoDeviceInitializeCredential? m_installed;
        /// <summary>The voucher retained by the manufacturer.</summary>
        private FdoManufacturerInitializeVoucher? m_voucher;
        /// <summary>The single TO0 registration served to the Device.</summary>
        private FdoRendezvousRegistration? m_registration;
    }

    /// <summary>
    /// Carries independently verified Device, Owner and Rendezvous inputs into TO2.
    /// The Device verifies the signed redirect against the voucher Owner during TO2.
    /// </summary>
    internal sealed record BootstrappedDevice(
        FdoDeviceInitializeCredential Credential,
        FdoTransferredOwnershipVoucher Voucher,
        FdoDeviceRendezvousResult Redirect);
}
