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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Identity
{
    /// <summary>
    /// Default <see cref="ITokenIssuer"/> that produces compact JWS JWTs
    /// signed with the configured GDS certificate.
    /// </summary>
    public sealed class CertificateJwtIssuer : ITokenIssuer
    {
        private readonly AuthorizationServiceOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly Lock m_lock = new();
        private ICertificateProvider? m_certificateProvider;
        private CertificateIdentifier? m_defaultSigningCertificate;
        private string? m_defaultIssuerUri;

        /// <summary>
        /// Creates an issuer that resolves certificates directly from the
        /// configured <see cref="CertificateIdentifier"/>.
        /// </summary>
        public CertificateJwtIssuer(
            IOptions<AuthorizationServiceOptions> options,
            ITelemetryContext telemetry)
            : this(options, null, telemetry, true)
        {
        }

        /// <summary>
        /// Creates an issuer that resolves signing keys through an
        /// <see cref="ICertificateProvider"/>.
        /// </summary>
        public CertificateJwtIssuer(
            IOptions<AuthorizationServiceOptions> options,
            ICertificateProvider certificateProvider,
            ITelemetryContext telemetry)
            : this(options, (ICertificateProvider?)certificateProvider, telemetry, true)
        {
        }

        /// <summary>
        /// Creates an issuer for tests and direct hosting.
        /// </summary>
        public CertificateJwtIssuer(
            AuthorizationServiceOptions options,
            ICertificateProvider certificateProvider,
            ITelemetryContext telemetry)
            : this(Options.Create(options), certificateProvider, telemetry, true)
        {
        }

        private CertificateJwtIssuer(
            IOptions<AuthorizationServiceOptions> options,
            ICertificateProvider? certificateProvider,
            ITelemetryContext telemetry,
            bool _)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
            m_certificateProvider = certificateProvider;
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <inheritdoc/>
        public string IssuerUri
        {
            get
            {
                lock (m_lock)
                {
                    return GetIssuerUriCore();
                }
            }
        }

        /// <inheritdoc/>
        public string ProfileUri => Profiles.JwtUserToken;

        /// <summary>
        /// Supplies hosted-GDS defaults after the application certificate
        /// has been created.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="certificateProvider"/> is <c>null</c>.</exception>
        internal void Initialize(
            ICertificateProvider certificateProvider,
            CertificateIdentifier? defaultSigningCertificate,
            string? defaultIssuerUri)
        {
            if (certificateProvider == null)
            {
                throw new ArgumentNullException(nameof(certificateProvider));
            }

            lock (m_lock)
            {
                m_certificateProvider ??= certificateProvider;
                m_defaultSigningCertificate ??= defaultSigningCertificate;
                if (string.IsNullOrEmpty(m_defaultIssuerUri))
                {
                    m_defaultIssuerUri = defaultIssuerUri;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AccessToken> IssueAsync(
            TokenIssuanceRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(request.Subject))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Token subject must be supplied.");
            }
            if (string.IsNullOrEmpty(request.AudienceUri))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Token audience must be supplied.");
            }

            CertificateIdentifier? signingIdentifier;
            ICertificateProvider? certificateProvider;
            string issuerUri;
            lock (m_lock)
            {
                signingIdentifier = m_options.SigningCertificate ?? m_defaultSigningCertificate;
                certificateProvider = m_certificateProvider;
                issuerUri = GetIssuerUriCore();
            }

            if (signingIdentifier == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "AuthorizationService signing certificate is not configured.");
            }

            using Certificate signingCertificate = await ResolveSigningCertificateAsync(
                signingIdentifier,
                certificateProvider,
                ct)
                .ConfigureAwait(false);

            DateTime issuedAt = DateTime.UtcNow;
            TimeSpan lifetime = request.RequestedLifetime > TimeSpan.Zero
                ? request.RequestedLifetime
                : m_options.DefaultTokenLifetime;
            if (lifetime <= TimeSpan.Zero)
            {
                lifetime = TimeSpan.FromMinutes(60);
            }
            DateTime expiresAt = issuedAt.Add(lifetime);
            string[] scopes = SelectScopes(request.RequestedScopes, m_options.DefaultScopes);

            byte[] signingInputBytes;
            byte[] signature;
            string header = CreateHeader(signingCertificate, out string alg);
            string payload = CreatePayload(
                issuerUri,
                request.Subject,
                request.AudienceUri,
                scopes,
                issuedAt,
                expiresAt,
                request.AdditionalClaims);
            string signingInput = Base64UrlEncode(Encoding.UTF8.GetBytes(header)) +
                "." +
                Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            signingInputBytes = Encoding.ASCII.GetBytes(signingInput);
            signature = Sign(signingCertificate, alg, signingInputBytes);

            string jwt = signingInput + "." + Base64UrlEncode(signature);
            return new AccessToken(
                Profiles.JwtUserToken,
                Encoding.UTF8.GetBytes(jwt),
                expiresAt,
                request.Subject,
                scopes);
        }

        private string GetIssuerUriCore()
        {
            if (!string.IsNullOrEmpty(m_options.IssuerUri))
            {
                return m_options.IssuerUri;
            }
            if (!string.IsNullOrEmpty(m_defaultIssuerUri))
            {
                return m_defaultIssuerUri!;
            }
            return "urn:opcua:gds:authorization-service";
        }

        private async ValueTask<Certificate> ResolveSigningCertificateAsync(
            CertificateIdentifier identifier,
            ICertificateProvider? certificateProvider,
            CancellationToken ct)
        {
            Certificate? certificate;
            if (certificateProvider != null)
            {
                certificate = await certificateProvider
                    .GetPrivateKeyCertificateAsync(identifier, null, null, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                certificate = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(identifier, null, null, m_telemetry, ct)
                    .ConfigureAwait(false);
            }

            if (certificate == null || !certificate.HasPrivateKey)
            {
                certificate?.Dispose();
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "AuthorizationService signing certificate could not be resolved with a private key.");
            }
            return certificate;
        }

        /// <summary>
        /// Creates the JWT header for the configured signing certificate.
        /// </summary>
        /// <remarks>
        /// <c>kid</c> is set to the certificate's SHA-1 thumbprint. Cross-stack tooling that expects
        /// JWKS <c>kid</c> or SKI-derived identifiers must be configured to match.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        private static string CreateHeader(Certificate certificate, out string algorithm)
        {
            using ECDsa? ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa != null)
            {
                algorithm = EcdsaAlgorithm(ecdsa.KeySize);
            }
            else
            {
                using RSA? rsa = certificate.GetRSAPrivateKey() ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "AuthorizationService signing certificate must use ECDSA or RSA.");
                algorithm = "RS256";
            }

            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("alg", algorithm);
                writer.WriteString("typ", "JWT");
                if (!string.IsNullOrEmpty(certificate.Thumbprint))
                {
                    writer.WriteString("kid", certificate.Thumbprint);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string CreatePayload(
            string issuerUri,
            string subject,
            string audience,
            string[] scopes,
            DateTime issuedAt,
            DateTime expiresAt,
            IReadOnlyDictionary<string, object?> additionalClaims)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("iss", issuerUri);
                writer.WriteString("sub", subject);
                writer.WriteString("aud", audience);
                writer.WriteNumber("iat", ToUnixTimeSeconds(issuedAt));
                writer.WriteNumber("nbf", ToUnixTimeSeconds(issuedAt.AddSeconds(-5)));
                writer.WriteNumber("exp", ToUnixTimeSeconds(expiresAt));
                if (scopes.Length != 0)
                {
                    writer.WriteString("scope", string.Join(" ", scopes));
                }

                foreach (KeyValuePair<string, object?> claim in additionalClaims)
                {
                    if (IsReservedClaim(claim.Key))
                    {
                        continue;
                    }
                    WriteClaim(writer, claim.Key, claim.Value);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static bool IsReservedClaim(string name)
        {
            return string.Equals(name, "iss", StringComparison.Ordinal) ||
                string.Equals(name, "sub", StringComparison.Ordinal) ||
                string.Equals(name, "aud", StringComparison.Ordinal) ||
                string.Equals(name, "iat", StringComparison.Ordinal) ||
                string.Equals(name, "nbf", StringComparison.Ordinal) ||
                string.Equals(name, "exp", StringComparison.Ordinal) ||
                string.Equals(name, "scope", StringComparison.Ordinal);
        }

        private static void WriteClaim(Utf8JsonWriter writer, string name, object? value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            switch (value)
            {
                case null:
                    writer.WriteNull(name);
                    break;
                case string stringValue:
                    writer.WriteString(name, stringValue);
                    break;
                case IEnumerable<string> stringValues:
                    writer.WriteStartArray(name);
                    foreach (string item in stringValues)
                    {
                        writer.WriteStringValue(item);
                    }
                    writer.WriteEndArray();
                    break;
                case bool boolValue:
                    writer.WriteBoolean(name, boolValue);
                    break;
                case int intValue:
                    writer.WriteNumber(name, intValue);
                    break;
                case long longValue:
                    writer.WriteNumber(name, longValue);
                    break;
                case double doubleValue:
                    writer.WriteNumber(name, doubleValue);
                    break;
                default:
                    writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static byte[] Sign(Certificate certificate, string algorithm, byte[] signingInput)
        {
            HashAlgorithmName hash = HashFromAlgorithm(algorithm);
            if (algorithm.StartsWith("ES", StringComparison.Ordinal))
            {
                using ECDsa ecdsa = certificate.GetECDsaPrivateKey()
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "ECDSA signing certificate has no private key.");
#if NET5_0_OR_GREATER
                return ecdsa.SignData(
                    signingInput,
                    hash,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
#else
                byte[] signature = ecdsa.SignData(signingInput, hash);
                int fieldLength = (ecdsa.KeySize + 7) / 8;
                return signature.Length == fieldLength * 2
                    ? signature
                    : ConvertDerToIeeeP1363(signature, fieldLength);
#endif
            }

            using RSA rsa = certificate.GetRSAPrivateKey()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "RSA signing certificate has no private key.");
            RSASignaturePadding padding = algorithm.StartsWith("PS", StringComparison.Ordinal)
                ? RSASignaturePadding.Pss
                : RSASignaturePadding.Pkcs1;
            return rsa.SignData(signingInput, hash, padding);
        }

        private static string[] SelectScopes(
            IReadOnlyList<string> requestedScopes,
            IEnumerable<string> defaultScopes)
        {
            IEnumerable<string> values = requestedScopes.Count != 0
                ? requestedScopes
                : defaultScopes;
            return [.. values
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)];
        }

        private static string EcdsaAlgorithm(int keySize)
        {
            if (keySize <= 256)
            {
                return "ES256";
            }
            if (keySize <= 384)
            {
                return "ES384";
            }
            return "ES512";
        }

        private static HashAlgorithmName HashFromAlgorithm(string algorithm)
        {
            return algorithm switch
            {
                "ES256" or "RS256" or "PS256" => HashAlgorithmName.SHA256,
                "ES384" or "RS384" or "PS384" => HashAlgorithmName.SHA384,
                "ES512" or "RS512" or "PS512" => HashAlgorithmName.SHA512,
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported JWT signing algorithm: " + algorithm)
            };
        }

        private static long ToUnixTimeSeconds(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

#if !NET5_0_OR_GREATER
        private static byte[] ConvertDerToIeeeP1363(byte[] derSignature, int fieldLength)
        {
            if (derSignature.Length < 8 || derSignature[0] != 0x30)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "ECDSA signature was not encoded as DER.");
            }

            int offset = 2;
            if ((derSignature[1] & 0x80) != 0)
            {
                int lengthBytes = derSignature[1] & 0x7f;
                offset += lengthBytes;
            }

            byte[] ieee = new byte[fieldLength * 2];
            offset = ReadDerInteger(derSignature, offset, ieee, 0, fieldLength);
            _ = ReadDerInteger(derSignature, offset, ieee, fieldLength, fieldLength);
            return ieee;
        }

        private static int ReadDerInteger(
            byte[] derSignature,
            int offset,
            byte[] destination,
            int destinationOffset,
            int fieldLength)
        {
            if (offset >= derSignature.Length || derSignature[offset] != 0x02)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "ECDSA DER signature is malformed.");
            }

            int length = derSignature[offset + 1];
            offset += 2;
            while (length > 0 && derSignature[offset] == 0)
            {
                offset++;
                length--;
            }
            if (length > fieldLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "ECDSA DER signature integer is too large.");
            }

            Buffer.BlockCopy(
                derSignature,
                offset,
                destination,
                destinationOffset + fieldLength - length,
                length);
            return offset + length;
        }
#endif
    }
}
