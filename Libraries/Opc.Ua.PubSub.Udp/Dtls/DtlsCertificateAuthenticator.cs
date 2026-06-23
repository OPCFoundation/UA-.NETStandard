/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// TLS 1.3 Certificate and CertificateVerify helpers from RFC 8446 §4.4.2-§4.4.3.
    /// </summary>
    internal static class DtlsCertificateAuthenticator
    {
        public static byte[] EncodeCertificate(IReadOnlyList<X509Certificate2> chain)
        {
            if (chain is null || chain.Count == 0)
            {
                throw new ArgumentException("DTLS certificate chain is required.", nameof(chain));
            }

            var entries = new DtlsHandshakeWriter();
            foreach (X509Certificate2 certificate in chain)
            {
                byte[] rawData = certificate.RawData;
                WriteOpaque24(entries, rawData);
                entries.WriteOpaque16(ReadOnlySpan<byte>.Empty);
            }

            byte[] entryBytes = entries.ToArray();
            var writer = new DtlsHandshakeWriter();
            writer.WriteOpaque8(ReadOnlySpan<byte>.Empty);
            WriteOpaque24(writer, entryBytes);
            return writer.ToArray();
        }

        public static IReadOnlyList<X509Certificate2> DecodeCertificate(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            if (reader.ReadOpaque8().Length != 0)
            {
                throw new DtlsHandshakeException("DTLS client/server certificate_request_context must be empty.");
            }

            byte[] certificateList = ReadOpaque24(ref reader);
            var entryReader = new DtlsHandshakeReader(certificateList);
            var certificates = new List<X509Certificate2>();
            while (!entryReader.EndOfData)
            {
                byte[] rawData = ReadOpaque24(ref entryReader);
                if (entryReader.ReadOpaque16().Length != 0)
                {
                    throw new DtlsHandshakeException("CertificateEntry extensions are not supported for PubSub DTLS.");
                }

                #if NET9_0_OR_GREATER
                certificates.Add(X509CertificateLoader.LoadCertificate(rawData));
#else
                certificates.Add(new X509Certificate2(rawData));
#endif
            }

            reader.EnsureComplete();
            if (certificates.Count == 0)
            {
                throw new DtlsHandshakeException("DTLS peer did not provide a certificate.");
            }

            return certificates;
        }

        public static byte[] SignCertificateVerify(
            X509Certificate2 certificate,
            DtlsCipherSuite cipherSuite,
            ReadOnlySpan<byte> transcriptHash)
        {
            if (certificate is null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            using ECDsa? ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa is null)
            {
                throw new DtlsHandshakeException("DTLS CertificateVerify requires an ECC certificate with ECDSA key.");
            }

            DtlsSignatureScheme scheme = GetSignatureScheme(cipherSuite);
            byte[] signedContent = BuildCertificateVerifyContent(isServer: true, transcriptHash);
            byte[] signature;
            try
            {
                signature = ecdsa.SignData(signedContent, GetHashAlgorithm(cipherSuite));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signedContent);
            }

            var writer = new DtlsHandshakeWriter();
            writer.WriteUInt16((ushort)scheme);
            writer.WriteOpaque16(signature);
            CryptographicOperations.ZeroMemory(signature);
            return writer.ToArray();
        }

        public static void VerifyCertificateVerify(
            X509Certificate2 certificate,
            DtlsCipherSuite cipherSuite,
            ReadOnlySpan<byte> transcriptHash,
            ReadOnlySpan<byte> certificateVerifyBody,
            bool isServer)
        {
            if (certificate is null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            using ECDsa? ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa is null)
            {
                throw new DtlsHandshakeException("DTLS peer certificate is not an ECC ECDSA certificate.");
            }

            var reader = new DtlsHandshakeReader(certificateVerifyBody);
            ushort scheme = reader.ReadUInt16();
            byte[] signature = reader.ReadOpaque16();
            reader.EnsureComplete();
            if (scheme != (ushort)GetSignatureScheme(cipherSuite))
            {
                throw new DtlsHandshakeException("DTLS CertificateVerify signature scheme does not match the profile hash.");
            }

            byte[] signedContent = BuildCertificateVerifyContent(isServer, transcriptHash);
            try
            {
                if (!ecdsa.VerifyData(signedContent, signature, GetHashAlgorithm(cipherSuite)))
                {
                    throw new DtlsHandshakeException("DTLS CertificateVerify signature validation failed.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signedContent);
                CryptographicOperations.ZeroMemory(signature);
            }
        }

        public static async ValueTask ValidatePeerCertificateAsync(
            ICertificateValidatorEx validator,
            IReadOnlyList<X509Certificate2> chain,
            CancellationToken cancellationToken)
        {
            if (validator is null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            if (chain is null || chain.Count == 0)
            {
                throw new DtlsHandshakeException("DTLS peer certificate chain is empty.");
            }

            using var peerCertificate = new Certificate(chain[0].RawData);
            CertificateValidationResult result = await validator
                .ValidateAsync(peerCertificate, ct: cancellationToken)
                .ConfigureAwait(false);
            result.ThrowIfInvalid();
        }

        private static byte[] BuildCertificateVerifyContent(bool isServer, ReadOnlySpan<byte> transcriptHash)
        {
            string context = isServer
                ? "TLS 1.3, server CertificateVerify"
                : "TLS 1.3, client CertificateVerify";
            byte[] contextBytes = System.Text.Encoding.ASCII.GetBytes(context);
            byte[] content = new byte[64 + contextBytes.Length + 1 + transcriptHash.Length];
            content.AsSpan(0, 64).Fill(0x20);
            Buffer.BlockCopy(contextBytes, 0, content, 64, contextBytes.Length);
            transcriptHash.CopyTo(content.AsSpan(65 + contextBytes.Length));
            CryptographicOperations.ZeroMemory(contextBytes);
            return content;
        }

        private static DtlsSignatureScheme GetSignatureScheme(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384 or DtlsCipherSuite.TlsSha384Sha384
                ? DtlsSignatureScheme.EcdsaSecp384r1Sha384
                : DtlsSignatureScheme.EcdsaSecp256r1Sha256;
        }

        private static HashAlgorithmName GetHashAlgorithm(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384 or DtlsCipherSuite.TlsSha384Sha384
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;
        }

        private static void WriteOpaque24(DtlsHandshakeWriter writer, ReadOnlySpan<byte> value)
        {
            if (value.Length > 0xffffff)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Span<byte> length = stackalloc byte[3];
            DtlsHandshakeCodec.WriteUInt24(length, value.Length);
            writer.WriteBytes(length);
            writer.WriteBytes(value);
        }

        private static byte[] ReadOpaque24(ref DtlsHandshakeReader reader)
        {
            byte[] lengthBytes = reader.ReadBytes(3);
            int length = DtlsHandshakeCodec.ReadUInt24(lengthBytes);
            return reader.ReadBytes(length);
        }
    }
}


