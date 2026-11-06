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
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Verifies Part 21 general-serialization JWS tickets before decoding their payloads.
    /// </summary>
    public sealed class OnboardingTicketValidator : IOnboardingTicketValidator
    {
        /// <summary>
        /// Creates a validator using managed authority trust and the UA JSON context.
        /// </summary>
        public OnboardingTicketValidator(
            IOnboardingTicketTrustProvider trustProvider,
            IServiceMessageContext messageContext)
        {
            m_trustProvider = trustProvider ?? throw new ArgumentNullException(nameof(trustProvider));
            m_messageContext = messageContext ?? throw new ArgumentNullException(nameof(messageContext));
        }

        /// <inheritdoc/>
        public async ValueTask<ValidatedOnboardingTicket> ValidateAsync(
            string encodedTicket,
            OnboardingTicketSource source,
            CancellationToken ct = default)
        {
            if (encodedTicket == null)
            {
                throw new ArgumentNullException(nameof(encodedTicket));
            }
            if (source is not OnboardingTicketSource.OutOfBand and not OnboardingTicketSource.Device)
            {
                throw new ArgumentOutOfRangeException(nameof(source));
            }
            ct.ThrowIfCancellationRequested();
            if (encodedTicket.Length > OnboardingTicketWriter.MaximumEncodedLength)
            {
                throw InvalidTicket("Ticket exceeds the supported envelope size.");
            }

            try
            {
                return await ValidateEnvelopeAsync(encodedTicket, source, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or DecoderFallbackException)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTicketInvalid, "Ticket contains an invalid JSON or base64 encoding.", ex);
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadDecodingError ||
                ex.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTicketInvalid, "Ticket payload is not valid reversible UA JSON.", ex);
            }
        }

        /// <summary>
        /// Verifies a bounded envelope, accepting one valid signature from a trusted authority.
        /// </summary>
        private async ValueTask<ValidatedOnboardingTicket> ValidateEnvelopeAsync(
            string encodedTicket,
            OnboardingTicketSource source,
            CancellationToken ct)
        {
            using JsonDocument document = JsonDocument.Parse(encodedTicket);
            JsonElement root = document.RootElement;
            if (!HasUniqueProperties(root, recursive: false) ||
                !TryGetString(root, "payload", out string payload) ||
                !root.TryGetProperty("signatures", out JsonElement signatures) ||
                signatures.ValueKind != JsonValueKind.Array ||
                signatures.GetArrayLength() is < 1 or > OnboardingTicketWriter.MaximumSignatureCount ||
                root.TryGetProperty("signature", out _) ||
                root.TryGetProperty("protected", out _) ||
                root.TryGetProperty("header", out _))
            {
                throw InvalidTicket("Ticket must use RFC 7515 general JSON serialization.");
            }
            byte[] payloadBytes = DecodeBase64Url(payload);
            foreach (JsonElement signature in signatures.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                ValidatedOnboardingTicket? accepted = await ValidateSignatureAsync(
                    signature, payload, payloadBytes, source, ct).ConfigureAwait(false);
                if (accepted != null)
                {
                    return accepted;
                }
            }
            throw InvalidTicket("No signature from a trusted ticket authority validates the payload.");
        }

        /// <summary>
        /// Verifies the protected header, signing key and trust of one signature.
        /// </summary>
        private async ValueTask<ValidatedOnboardingTicket?> ValidateSignatureAsync(
            JsonElement signature,
            string payload,
            byte[] payloadBytes,
            OnboardingTicketSource source,
            CancellationToken ct)
        {
            if (!HasUniqueProperties(signature) ||
                !TryGetString(signature, "protected", out string protectedText) ||
                !TryGetString(signature, "signature", out string signatureText))
            {
                return null;
            }
            try
            {
                using JsonDocument protectedDocument = JsonDocument.Parse(DecodeBase64Url(protectedText));
                JsonElement header = protectedDocument.RootElement;
                if (!HasUniqueProperties(header) ||
                    !TryGetString(header, "alg", out string algorithm) ||
                    !TryGetString(header, "cty", out string contentType) ||
                    !header.TryGetProperty("x5c", out JsonElement chain) ||
                    chain.ValueKind != JsonValueKind.Array ||
                    chain.GetArrayLength() is < 1 or > OnboardingTicketWriter.MaximumCertificateCount ||
                    header.TryGetProperty("crit", out _) ||
                    header.TryGetProperty("b64", out _))
                {
                    return null;
                }
                if (!TryGetTicketKind(contentType, out TicketKind kind))
                {
                    return null;
                }
                string compositeUri = string.Empty;
                if (header.TryGetProperty("opc-uri", out _) &&
                    (!TryGetString(header, "opc-uri", out compositeUri) || !IsAbsoluteUri(compositeUri)))
                {
                    return null;
                }
                if (signature.TryGetProperty("header", out JsonElement unprotectedHeader))
                {
                    if (!HasUniqueProperties(unprotectedHeader))
                    {
                        return null;
                    }
                    foreach (JsonProperty property in unprotectedHeader.EnumerateObject())
                    {
                        if (header.TryGetProperty(property.Name, out _) ||
                            property.Name is "alg" or "x5c" or "cty" or "opc-uri" or "crit" or "b64")
                        {
                            return null;
                        }
                    }
                }
                using var certificates = new CertificateCollection();
                foreach (JsonElement item in chain.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }
                    byte[] encodedCertificate = Convert.FromBase64String(item.GetString()!);
                    using Certificate certificate = Certificate.FromRawData(encodedCertificate);
                    if (!certificate.RawData.AsSpan().SequenceEqual(encodedCertificate))
                    {
                        return null;
                    }
                    certificates.Add(certificate);
                }
                byte[] signingInput = Encoding.ASCII.GetBytes(protectedText + "." + payload);
                if (!Verify(certificates[0], algorithm, signingInput, DecodeBase64Url(signatureText)) ||
                    !await m_trustProvider.IsTrustedAsync(certificates, source, ct).ConfigureAwait(false))
                {
                    return null;
                }
                using JsonDocument payloadDocument = JsonDocument.Parse(payloadBytes);
                if (!HasUniqueProperties(payloadDocument.RootElement))
                {
                    throw InvalidTicket("Ticket payload contains duplicate or invalid JSON members.");
                }
                using var decoder = new JsonDecoder(s_utf8.GetString(payloadBytes), m_messageContext);
                BaseTicketType ticket = kind switch
                {
                    TicketKind.DeviceIdentity => new DeviceIdentityTicketType(),
                    TicketKind.CompositeIdentity => new CompositeIdentityTicketType(),
                    _ => throw InvalidTicket("Ticket content type is not supported.")
                };
                ticket.Decode(decoder);
                string? identityUri = ticket switch
                {
                    DeviceIdentityTicketType device => device.ProductInstanceUri,
                    CompositeIdentityTicketType composite => composite.CompositeInstanceUri,
                    _ => string.Empty
                };
                if (!IsAbsoluteUri(identityUri) ||
                    string.IsNullOrWhiteSpace(ticket.ManufacturerName) ||
                    ticket.Authorities.Count == 0)
                {
                    throw InvalidTicket("Ticket must identify its subject and identity authorities.");
                }
                for (int i = 0; i < ticket.Authorities.Count; i++)
                {
                    CertificateAuthorityType authority = ticket.Authorities[i];
                    if (authority == null || authority.AuthorityCertificate.IsNull)
                    {
                        throw InvalidTicket("Ticket authority must contain a DER certificate.");
                    }
                    using Certificate authorityCertificate = Certificate.FromRawData(
                        authority.AuthorityCertificate.ToArray());
                    if (!authorityCertificate.RawData.AsSpan().SequenceEqual(authority.AuthorityCertificate.Span))
                    {
                        throw InvalidTicket("Ticket authority must contain a DER certificate.");
                    }
                    for (int j = 0; j < authority.IssuerCertificates.Count; j++)
                    {
                        using Certificate issuer = Certificate.FromRawData(authority.IssuerCertificates[j].ToArray());
                        if (!issuer.RawData.AsSpan().SequenceEqual(authority.IssuerCertificates[j].Span))
                        {
                            throw InvalidTicket("Ticket issuer must contain a DER certificate.");
                        }
                    }
                }
                if (ticket is CompositeIdentityTicketType compositeTicket)
                {
                    for (int i = 0; i < compositeTicket.Devices.Count; i++)
                    {
                        if (!IsAbsoluteUri(compositeTicket.Devices[i]))
                        {
                            throw InvalidTicket("Composite device references must be absolute URIs.");
                        }
                    }
                    for (int i = 0; i < compositeTicket.Composites.Count; i++)
                    {
                        if (!IsAbsoluteUri(compositeTicket.Composites[i]))
                        {
                            throw InvalidTicket("Composite references must be absolute URIs.");
                        }
                    }
                }
                return new ValidatedOnboardingTicket(ticket, compositeUri);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
            {
                // Other independently signed entries may still authenticate the same payload.
                return null;
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadTicketInvalid ||
                ex.StatusCode == StatusCodes.BadDecodingError ||
                ex.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                return null;
            }
        }

        /// <summary>
        /// Parses the media-type header without depending on parameter whitespace or quoting.
        /// </summary>
        private static bool TryGetTicketKind(string contentType, out TicketKind kind)
        {
            kind = TicketKind.Unspecified;
            if (!MediaTypeHeaderValue.TryParse("application/" + contentType, out MediaTypeHeaderValue? parsed) ||
                !string.Equals(parsed.MediaType, "application/opc-ticket+json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            bool found = false;
            foreach (NameValueHeaderValue parameter in parsed.Parameters)
            {
                if (!string.Equals(parameter.Name, "type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (found)
                {
                    return false;
                }
                found = true;
                kind = parameter.Value?.Trim('"') switch
                {
                    "DeviceIdentityTicketType" => TicketKind.DeviceIdentity,
                    "CompositeIdentityTicketType" => TicketKind.CompositeIdentity,
                    _ => TicketKind.Unspecified
                };
            }
            return found && kind != TicketKind.Unspecified;
        }

        /// <summary>
        /// Verifies a SHA-2 asymmetric JWS signature with a matching key type and size.
        /// </summary>
        private static bool Verify(Certificate certificate, string algorithm, byte[] input, byte[] signature)
        {
            HashAlgorithmName hash = algorithm switch
            {
                "RS256" or "PS256" or "ES256" => HashAlgorithmName.SHA256,
                "RS384" or "PS384" or "ES384" => HashAlgorithmName.SHA384,
                "RS512" or "PS512" or "ES512" => HashAlgorithmName.SHA512,
                _ => default
            };
            if (hash == default)
            {
                return false;
            }
            if (algorithm.StartsWith("ES", StringComparison.Ordinal))
            {
                using ECDsa? key = certificate.GetECDsaPublicKey();
                int keySize = algorithm switch
                {
                    "ES256" => 256,
                    "ES384" => 384,
                    _ => 521
                };
                if (key == null || key.KeySize != keySize || signature.Length != 2 * ((keySize + 7) / 8))
                {
                    return false;
                }
#if NET5_0_OR_GREATER
                return key.VerifyData(input, signature, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
#else
                return key.VerifyData(input, signature, hash);
#endif
            }
            using RSA? rsa = certificate.GetRSAPublicKey();
            return rsa != null && rsa.KeySize >= 2048 &&
                rsa.VerifyData(input, signature, hash, algorithm.StartsWith("PS", StringComparison.Ordinal)
                    ? RSASignaturePadding.Pss
                    : RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Rejects padded, noncanonical and non-URL base64 encodings.
        /// </summary>
        private static byte[] DecodeBase64Url(string value)
        {
            foreach (char character in value)
            {
                if (character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and
                    not (>= '0' and <= '9') and not '-' and not '_')
                {
                    throw new FormatException("Invalid base64url character.");
                }
            }
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += (value.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException("Invalid base64url length.")
            };
            byte[] decoded = Convert.FromBase64String(padded);
            string canonical = Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!string.Equals(value, canonical, StringComparison.Ordinal))
            {
                throw new FormatException("Noncanonical base64url encoding.");
            }
            return decoded;
        }

        /// <summary>
        /// Rejects duplicate member names recursively, including inside vendor metadata.
        /// </summary>
        private static bool HasUniqueProperties(JsonElement element, bool recursive = true)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || (recursive && !HasUniqueChildren(property.Value)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks nested objects and arrays for ambiguous member names.
        /// </summary>
        private static bool HasUniqueChildren(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                return HasUniqueProperties(element);
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    if (!HasUniqueChildren(child))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Reads a nonempty string without applying JSON type coercions.
        /// </summary>
        private static bool TryGetString(JsonElement element, string name, out string value)
        {
            value = string.Empty;
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            value = property.GetString()!;
            return value.Length > 0;
        }

        /// <summary>
        /// Checks an identity URI without changing its case-sensitive representation.
        /// </summary>
        private static bool IsAbsoluteUri(string? value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out _);
        }

        /// <summary>
        /// Creates the method-level status required for an invalid ticket.
        /// </summary>
        private static ServiceResultException InvalidTicket(string message)
        {
            return new ServiceResultException(StatusCodes.BadTicketInvalid, message);
        }

        /// <summary>
        /// Strict payload decoding preserves signed UTF-8 bytes.
        /// </summary>
        private static readonly UTF8Encoding s_utf8 = new(false, true);

        /// <summary>
        /// The authority validation provider.
        /// </summary>
        private readonly IOnboardingTicketTrustProvider m_trustProvider;

        /// <summary>
        /// The generated UA type decoding context.
        /// </summary>
        private readonly IServiceMessageContext m_messageContext;
    }
}
