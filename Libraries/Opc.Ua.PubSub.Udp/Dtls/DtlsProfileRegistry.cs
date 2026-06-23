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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Fail-closed runtime registry for OPC UA PubSub DTLS 1.3 profiles.
    /// </summary>
    public sealed class DtlsProfileRegistry
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsProfileRegistry"/> with runtime primitive probes.
        /// </summary>
        public DtlsProfileRegistry()
            : this(DtlsPrimitiveSupport.Probe())
        {
        }

        /// <summary>
        /// Initializes a new <see cref="DtlsProfileRegistry"/> with explicit primitive support.
        /// </summary>
        public DtlsProfileRegistry(DtlsPrimitiveSupport primitiveSupport)
        {
            PrimitiveSupport = primitiveSupport;
            KnownProfiles = new ReadOnlyCollection<DtlsProfile>(CreateKnownProfiles());
            DtlsProfile[] supported = KnownProfiles.Where(primitiveSupport.Supports).ToArray();
            SupportedProfiles = new ReadOnlyCollection<DtlsProfile>(supported);
            m_supportedByName = supported.ToDictionary(profile => profile.Name, StringComparer.OrdinalIgnoreCase);
            m_knownByName = KnownProfiles.ToDictionary(profile => profile.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Primitive support snapshot used to decide profile availability.
        /// </summary>
        public DtlsPrimitiveSupport PrimitiveSupport { get; }

        /// <summary>
        /// Complete profile matrix, including fail-closed unsupported entries.
        /// </summary>
        public IReadOnlyList<DtlsProfile> KnownProfiles { get; }

        /// <summary>
        /// Profiles registered for this platform.
        /// </summary>
        public IReadOnlyList<DtlsProfile> SupportedProfiles { get; }

        /// <summary>
        /// Emits a startup diagnostic listing supported DTLS profiles.
        /// </summary>
        public void EmitStartupDiagnostic(ITelemetryContext telemetry)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            ILogger logger = telemetry.CreateLogger<DtlsProfileRegistry>();
            string supported = SupportedProfiles.Count == 0
                ? "none"
                : string.Join(", ", SupportedProfiles.Select(profile => profile.Name));
            logger.LogInformation(
                "OPC UA PubSub DTLS 1.3 supported profiles: {Profiles}. Primitive support: {Support}.",
                supported,
                PrimitiveSupport);
        }

        /// <summary>
        /// Resolves a supported profile or throws a clear fail-closed error.
        /// </summary>
        public DtlsProfile Resolve(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
            {
                throw new ArgumentException("DTLS profile name is required.", nameof(profileName));
            }

            if (m_supportedByName.TryGetValue(profileName, out DtlsProfile? profile))
            {
                return profile;
            }

            if (m_knownByName.TryGetValue(profileName, out DtlsProfile? knownProfile))
            {
                throw new NotSupportedException(string.Format(
                    CultureInfo.InvariantCulture,
                    "DTLS profile '{0}' is not supported by the current .NET BCL/runtime. Required cipher '{1}', " +
                    "ECDHE curve '{2}', and certificate curve '{3}' must be available; no downgrade is allowed.",
                    knownProfile.Name,
                    knownProfile.CipherSuite,
                    knownProfile.KeyExchangeCurve,
                    knownProfile.CertificateCurve));
            }

            throw new NotSupportedException(string.Format(
                CultureInfo.InvariantCulture,
                "DTLS profile '{0}' is unknown and cannot be registered.",
                profileName));
        }

        /// <summary>
        /// Attempts to resolve a supported profile without throwing.
        /// </summary>
        public bool TryResolve(string profileName, out DtlsProfile? profile)
        {
            if (string.IsNullOrEmpty(profileName))
            {
                profile = null;
                return false;
            }

            return m_supportedByName.TryGetValue(profileName, out profile);
        }

        private static DtlsProfile[] CreateKnownProfiles()
        {
            return
            [
                new("ECC_curve25519", DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                    DtlsNamedCurve.Curve25519, DtlsNamedCurve.Curve25519, isMandatory: true),
                new("ECC_curve25519_AesGcm", DtlsCipherSuite.TlsAes128GcmSha256,
                    DtlsNamedCurve.Curve25519, DtlsNamedCurve.Curve25519, isMandatory: true),
                new("ECC_curve448", DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                    DtlsNamedCurve.Curve448, DtlsNamedCurve.Curve448, isMandatory: true),
                new("ECC_curve448_AesGcm", DtlsCipherSuite.TlsAes256GcmSha384,
                    DtlsNamedCurve.Curve448, DtlsNamedCurve.Curve448, isMandatory: true),
                new("ECC_nistP256", DtlsCipherSuite.TlsSha256Sha256,
                    DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false),
                new("ECC_nistP384", DtlsCipherSuite.TlsSha384Sha384,
                    DtlsNamedCurve.NistP384, DtlsNamedCurve.NistP384, isMandatory: false),
                new("ECC_brainpoolP256r1", DtlsCipherSuite.TlsSha256Sha256,
                    DtlsNamedCurve.BrainpoolP256r1, DtlsNamedCurve.BrainpoolP256r1, isMandatory: false),
                new("ECC_brainpoolP384r1", DtlsCipherSuite.TlsSha384Sha384,
                    DtlsNamedCurve.BrainpoolP384r1, DtlsNamedCurve.BrainpoolP384r1, isMandatory: false),
                new("ECC_nistP256_AesGcm", DtlsCipherSuite.TlsAes128GcmSha256,
                    DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false),
                new("ECC_nistP384_AesGcm", DtlsCipherSuite.TlsAes256GcmSha384,
                    DtlsNamedCurve.NistP384, DtlsNamedCurve.NistP384, isMandatory: false),
                new("ECC_brainpoolP256r1_AesGcm", DtlsCipherSuite.TlsAes128GcmSha256,
                    DtlsNamedCurve.BrainpoolP256r1, DtlsNamedCurve.BrainpoolP256r1, isMandatory: false),
                new("ECC_brainpoolP384r1_AesGcm", DtlsCipherSuite.TlsAes256GcmSha384,
                    DtlsNamedCurve.BrainpoolP384r1, DtlsNamedCurve.BrainpoolP384r1, isMandatory: false),
                new("ECC_nistP256_ChaChaPoly", DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                    DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false),
                new("ECC_nistP384_ChaChaPoly", DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                    DtlsNamedCurve.NistP384, DtlsNamedCurve.NistP384, isMandatory: false),
                new("ECC_brainpoolP256r1_ChaChaPoly", DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                    DtlsNamedCurve.BrainpoolP256r1, DtlsNamedCurve.BrainpoolP256r1, isMandatory: false),
                new("ECC_brainpoolP384r1_ChaChaPoly", DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                    DtlsNamedCurve.BrainpoolP384r1, DtlsNamedCurve.BrainpoolP384r1, isMandatory: false)
            ];
        }

        private readonly Dictionary<string, DtlsProfile> m_supportedByName;
        private readonly Dictionary<string, DtlsProfile> m_knownByName;
    }

    /// <summary>
    /// Runtime .NET BCL primitive support for DTLS profiles.
    /// </summary>
    public readonly record struct DtlsPrimitiveSupport(
        bool HasAesGcm,
        bool HasAes128Gcm,
        bool HasAes256Gcm,
        bool HasChaCha20Poly1305,
        bool HasHkdf,
        bool HasNistP256,
        bool HasNistP384,
        bool HasBrainpoolP256r1,
        bool HasBrainpoolP384r1)
    {
        /// <summary>
        /// Probes the current runtime using typed BCL APIs only.
        /// </summary>
        public static DtlsPrimitiveSupport Probe()
        {
#if NET8_0_OR_GREATER
            bool hasAesGcm = AesGcm.IsSupported;
            bool hasChaCha20Poly1305 = ChaCha20Poly1305.IsSupported;
            return new DtlsPrimitiveSupport(
                hasAesGcm,
                hasAesGcm,
                hasAesGcm,
                hasChaCha20Poly1305,
                ProbeHkdf(),
                CanCreateCurve(ECCurve.NamedCurves.nistP256),
                CanCreateCurve(ECCurve.NamedCurves.nistP384),
                CanCreateCurve(ECCurve.CreateFromValue("1.3.36.3.3.2.8.1.1.7")),
                CanCreateCurve(ECCurve.CreateFromValue("1.3.36.3.3.2.8.1.1.11")));
#else
            return new DtlsPrimitiveSupport(false, false, false, false, false, false, false, false, false);
#endif
        }

        /// <summary>
        /// Determines whether every primitive required by a profile is available.
        /// </summary>
        public bool Supports(DtlsProfile profile)
        {
            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return HasHkdf
                && SupportsCipher(profile.CipherSuite)
                && SupportsCurve(profile.KeyExchangeCurve)
                && SupportsCurve(profile.CertificateCurve);
        }

        private bool SupportsCipher(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite switch
            {
                DtlsCipherSuite.TlsAes128GcmSha256 => HasAesGcm && HasAes128Gcm,
                DtlsCipherSuite.TlsAes256GcmSha384 => HasAesGcm && HasAes256Gcm,
                DtlsCipherSuite.TlsChaCha20Poly1305Sha256 => HasChaCha20Poly1305,
                DtlsCipherSuite.TlsSha256Sha256 => true,
                DtlsCipherSuite.TlsSha384Sha384 => true,
                _ => false
            };
        }

        private bool SupportsCurve(DtlsNamedCurve curve)
        {
            return curve switch
            {
                DtlsNamedCurve.NistP256 => HasNistP256,
                DtlsNamedCurve.NistP384 => HasNistP384,
                DtlsNamedCurve.BrainpoolP256r1 => HasBrainpoolP256r1,
                DtlsNamedCurve.BrainpoolP384r1 => HasBrainpoolP384r1,
                DtlsNamedCurve.Curve25519 => false,
                DtlsNamedCurve.Curve448 => false,
                _ => false
            };
        }

#if NET8_0_OR_GREATER
        private static bool CanCreateCurve(ECCurve curve)
        {
            try
            {
                using ECDiffieHellman ecdh = ECDiffieHellman.Create(curve);
                return true;
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException
                or CryptographicException
                or NotSupportedException)
            {
                return false;
            }
        }

        private static bool ProbeHkdf()
        {
            Span<byte> output = stackalloc byte[32];
            try
            {
                HKDF.Extract(HashAlgorithmName.SHA256, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, output);
                CryptographicOperations.ZeroMemory(output);
                return true;
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException
                or CryptographicException
                or NotSupportedException)
            {
                CryptographicOperations.ZeroMemory(output);
                return false;
            }
        }
#endif
    }
}


