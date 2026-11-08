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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Onboarding
{
    /// <summary>
    /// Writes Part 21 ticket envelopes using a signing key resolved from a certificate store.
    /// </summary>
    public sealed class OnboardingTicketWriter
    {
        /// <summary>
        /// Creates a writer that borrows its certificate provider and UA encoding context.
        /// </summary>
        public OnboardingTicketWriter(
            ICertificateProvider certificateProvider,
            IServiceMessageContext messageContext)
        {
            m_certificateProvider = certificateProvider ??
                throw new ArgumentNullException(nameof(certificateProvider));
            m_messageContext = messageContext ?? throw new ArgumentNullException(nameof(messageContext));
        }

        /// <summary>
        /// Encodes a device or composite ticket as reversible JSON and signs its general JWS envelope.
        /// Issuers are DER certificates ordered from the signing certificate's issuer to the root.
        /// A composite builder supplies its CompositeInstanceUri in the protected opc-uri header.
        /// The certificate provider retains ownership of its cache; the writer releases its own handle.
        /// </summary>
        public ValueTask<string> WriteAsync(
            BaseTicketType ticket,
            CertificateIdentifier signingCertificate,
            ArrayOf<ByteString> issuerCertificates = default,
            string compositeInstanceUri = "",
            CancellationToken ct = default)
        {
            if (ticket == null)
            {
                throw new ArgumentNullException(nameof(ticket));
            }
            if (signingCertificate == null)
            {
                throw new ArgumentNullException(nameof(signingCertificate));
            }
            if (compositeInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(compositeInstanceUri));
            }
            if (compositeInstanceUri.Length > 0 &&
                !Uri.TryCreate(compositeInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "Composite identity must be an absolute URI.", nameof(compositeInstanceUri));
            }
            ct.ThrowIfCancellationRequested();
            string contentType = ticket switch
            {
                DeviceIdentityTicketType => "opc-ticket+json;type=DeviceIdentityTicketType",
                CompositeIdentityTicketType => "opc-ticket+json;type=CompositeIdentityTicketType",
                _ => throw new ArgumentException(
                    "Only device and composite identity tickets are supported.", nameof(ticket))
            };
            string payload;
            using (var encoder = new JsonEncoder(m_messageContext, JsonEncoderOptions.Compact))
            {
                encoder.WriteEncodeable("ticket", ticket, ticket.TypeId);
                using JsonDocument document = JsonDocument.Parse(encoder.CloseAndReturnText());
                string ticketJson = document.RootElement.GetProperty("ticket").GetRawText();
                payload = EncodeBase64Url(Encoding.UTF8.GetBytes(ticketJson));
            }
            return SignEnvelopeAsync(
                payload, contentType, signingCertificate, issuerCertificates, compositeInstanceUri, null, ct);
        }

        /// <summary>
        /// Validates an existing ticket before appending a signature over its unchanged encoded payload.
        /// The required validation callback must verify at least one trusted signature and return its decoded ticket;
        /// a registrar can use its existing ticket validator with the appropriate ticket source.
        /// Existing signatures and vendor metadata are retained. The composite URI identifies this new signer only.
        /// </summary>
        public async ValueTask<string> AppendSignatureAsync(
            string encodedTicket,
            CertificateIdentifier signingCertificate,
            Func<string, CancellationToken, ValueTask<BaseTicketType>> validateTicket,
            ArrayOf<ByteString> issuerCertificates = default,
            string compositeInstanceUri = "",
            CancellationToken ct = default)
        {
            if (encodedTicket == null)
            {
                throw new ArgumentNullException(nameof(encodedTicket));
            }
            if (signingCertificate == null)
            {
                throw new ArgumentNullException(nameof(signingCertificate));
            }
            if (validateTicket == null)
            {
                throw new ArgumentNullException(nameof(validateTicket));
            }
            if (compositeInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(compositeInstanceUri));
            }
            if (compositeInstanceUri.Length > 0 &&
                !Uri.TryCreate(compositeInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "Composite identity must be an absolute URI.", nameof(compositeInstanceUri));
            }
            ct.ThrowIfCancellationRequested();
            if (encodedTicket.Length > MaximumEncodedLength)
            {
                throw new ServiceResultException(StatusCodes.BadTicketInvalid);
            }
            BaseTicketType validated = await validateTicket(encodedTicket, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            string contentType = validated switch
            {
                DeviceIdentityTicketType => "opc-ticket+json;type=DeviceIdentityTicketType",
                CompositeIdentityTicketType => "opc-ticket+json;type=CompositeIdentityTicketType",
                _ => throw new ServiceResultException(StatusCodes.BadTicketInvalid)
            };
            using JsonDocument envelope = JsonDocument.Parse(encodedTicket);
            JsonElement root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("payload", out JsonElement payload) ||
                payload.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("signatures", out JsonElement signatures) ||
                signatures.ValueKind != JsonValueKind.Array ||
                signatures.GetArrayLength() is < 1 or >= MaximumSignatureCount)
            {
                throw new ServiceResultException(StatusCodes.BadTicketInvalid);
            }
            string encodedPayload = payload.GetString()!;
            if (encodedPayload.Length == 0)
            {
                throw new ServiceResultException(StatusCodes.BadTicketInvalid);
            }
            return await SignEnvelopeAsync(
                encodedPayload, contentType, signingCertificate, issuerCertificates,
                compositeInstanceUri, root, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates one protected signature and retains the original envelope's members without re-encoding its payload.
        /// </summary>
        private async ValueTask<string> SignEnvelopeAsync(
            string payload,
            string contentType,
            CertificateIdentifier signingCertificate,
            ArrayOf<ByteString> issuerCertificates,
            string compositeInstanceUri,
            JsonElement? existingEnvelope,
            CancellationToken ct)
        {
            if (issuerCertificates.Count >= MaximumCertificateCount)
            {
                throw new ServiceResultException(StatusCodes.BadTicketInvalid);
            }
            using Certificate certificate = await m_certificateProvider.GetPrivateKeyCertificateAsync(
                signingCertificate, ct: ct).ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The ticket signing certificate is not available.");
            string algorithm = GetAlgorithm(certificate);
            string protectedHeader;
            using (var buffer = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartObject();
                    writer.WriteString("alg", algorithm);
                    writer.WriteString("cty", contentType);
                    if (compositeInstanceUri.Length > 0)
                    {
                        writer.WriteString("opc-uri", compositeInstanceUri);
                    }
                    writer.WriteStartArray("x5c");
                    writer.WriteStringValue(Convert.ToBase64String(certificate.RawData));
                    for (int i = 0; i < issuerCertificates.Count; i++)
                    {
                        writer.WriteStringValue(Convert.ToBase64String(issuerCertificates[i].ToArray()));
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                protectedHeader = EncodeBase64Url(buffer.ToArray());
            }
            ct.ThrowIfCancellationRequested();
            byte[] signature = Sign(
                certificate, algorithm, Encoding.ASCII.GetBytes(protectedHeader + "." + payload));
            using var envelope = new MemoryStream();
            using (var writer = new Utf8JsonWriter(envelope))
            {
                writer.WriteStartObject();
                if (existingEnvelope is { } existing)
                {
                    foreach (JsonProperty property in existing.EnumerateObject())
                    {
                        if (!property.NameEquals("signatures"))
                        {
                            property.WriteTo(writer);
                        }
                    }
                }
                else
                {
                    writer.WriteString("payload", payload);
                }
                writer.WriteStartArray("signatures");
                if (existingEnvelope is { } original)
                {
                    foreach (JsonElement entry in original.GetProperty("signatures").EnumerateArray())
                    {
                        entry.WriteTo(writer);
                    }
                }
                writer.WriteStartObject();
                writer.WriteString("protected", protectedHeader);
                writer.WriteString("signature", EncodeBase64Url(signature));
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            string encoded = Encoding.UTF8.GetString(envelope.ToArray());
            if (encoded.Length > MaximumEncodedLength)
            {
                throw new ServiceResultException(StatusCodes.BadTicketInvalid);
            }
            return encoded;
        }

        /// <summary>
        /// Selects a SHA-2 algorithm that matches the signing key.
        /// </summary>
        private static string GetAlgorithm(Certificate certificate)
        {
            using ECDsa? ec = certificate.GetECDsaPrivateKey();
            if (ec != null)
            {
                return ec.KeySize switch
                {
                    256 => "ES256",
                    384 => "ES384",
                    521 => "ES512",
                    _ => throw new ServiceResultException(
                        StatusCodes.BadSecurityPolicyRejected, "Unsupported ticket signing curve.")
                };
            }
            using RSA? rsa = certificate.GetRSAPrivateKey();
            if (rsa == null || rsa.KeySize < 2048)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Ticket signing requires RSA 2048 or a supported ECDSA curve.");
            }
            return "RS256";
        }

        /// <summary>
        /// Signs the exact protected-header and payload representation.
        /// </summary>
        private static byte[] Sign(Certificate certificate, string algorithm, byte[] input)
        {
            HashAlgorithmName hash = algorithm switch
            {
                "ES384" => HashAlgorithmName.SHA384,
                "ES512" => HashAlgorithmName.SHA512,
                _ => HashAlgorithmName.SHA256
            };
            if (algorithm.StartsWith("ES", StringComparison.Ordinal))
            {
                using ECDsa key = certificate.GetECDsaPrivateKey() ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
#if NET5_0_OR_GREATER
                return key.SignData(input, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
#else
                return key.SignData(input, hash);
#endif
            }
            using RSA rsa = certificate.GetRSAPrivateKey() ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            return rsa.SignData(input, hash, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Encodes signed components without base64 padding.
        /// </summary>
        private static string EncodeBase64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// The maximum encoded ticket length accepted by the writer and default reader.
        /// </summary>
        public const int MaximumEncodedLength = 1024 * 1024;

        /// <summary>
        /// The maximum number of signatures accepted by the writer and default reader.
        /// </summary>
        public const int MaximumSignatureCount = 64;

        /// <summary>
        /// The maximum certificate count, including the signer, in a protected x5c chain.
        /// </summary>
        public const int MaximumCertificateCount = 16;

        /// <summary>
        /// The borrowed managed certificate provider.
        /// </summary>
        private readonly ICertificateProvider m_certificateProvider;

        /// <summary>
        /// The generated UA type encoding context.
        /// </summary>
        private readonly IServiceMessageContext m_messageContext;
    }
}
