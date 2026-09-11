/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Why a QUIC connection was refused because the TLS peer could not
    /// be proved to be the OPC UA peer (Part 6 errata 7.6.1).
    /// </summary>
    public enum QuicPeerBindingResult
    {
        /// <summary>
        /// All three certificates carry the same subjectPublicKeyInfo.
        /// </summary>
        Bound = 0,

        /// <summary>
        /// The TLS handshake presented no certificate.
        /// </summary>
        NoTlsCertificate,

        /// <summary>
        /// The EndpointDescription carried no serverCertificate.
        /// </summary>
        NoEndpointCertificate,

        /// <summary>
        /// The OpenSecureChannel exchange carried no certificate.
        /// </summary>
        NoSecureChannelCertificate,

        /// <summary>
        /// The TLS certificate and the EndpointDescription certificate
        /// carry different keys.
        /// </summary>
        EndpointKeyMismatch,

        /// <summary>
        /// The TLS certificate and the OpenSecureChannel certificate
        /// carry different keys.
        /// </summary>
        SecureChannelKeyMismatch,

        /// <summary>
        /// A certificate could not be parsed.
        /// </summary>
        MalformedCertificate
    }

    /// <summary>
    /// Binds the TLS peer of a QUIC connection to the OPC UA peer that
    /// the control stream authenticated (Part 6 errata 7.6.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The TransportSecured profile rests on one premise: that the TLS
    /// connection carrying the frames terminates at the same application
    /// the control stream authenticated. Without it, any party able to
    /// terminate QUIC between the two byte-forwards the end-to-end
    /// secured control stream so that OpenSecureChannel, CreateSession
    /// and ActivateSession all succeed and every certificate check
    /// passes, while reading, modifying, dropping and injecting every
    /// data channel frame in the clear. Both ends would report a fully
    /// authenticated SignAndEncrypt SecureChannel.
    /// </para>
    /// <para>
    /// The binding is therefore <b>by key, not by name</b>. Equality of
    /// an ApplicationUri subjectAltName is necessary but not sufficient:
    /// a certificate asserting an ApplicationUri proves only that some CA
    /// in the trust list issued it, and CA and GDS implementations
    /// commonly populate the URI SAN from the requester's own CSR without
    /// checking it against an authoritative registry. Comparing the key
    /// removes the CA from the trust decision entirely, and costs one
    /// comparison the verifier is already positioned to make.
    /// </para>
    /// <para>
    /// The obligation follows the <b>TLS role</b>, not the OPC UA role.
    /// Under a normal connection the TLS server is the OPC UA Server and
    /// the two readings coincide; under reverse connect the roles invert
    /// and the rule binds the OPC UA Client's TLS certificate instead.
    /// </para>
    /// </remarks>
    public static class QuicPeerBinding
    {
        /// <summary>
        /// Verifies that the certificate presented in the TLS handshake,
        /// the certificate of the selected EndpointDescription and the
        /// certificate returned by OpenSecureChannel all carry the same
        /// public key.
        /// </summary>
        /// <param name="tlsCertificate">The certificate the peer acting
        /// as the TLS server presented.</param>
        /// <param name="endpointCertificate">The serverCertificate of the
        /// selected EndpointDescription, DER encoded.</param>
        /// <param name="secureChannelCertificate">The certificate carried
        /// by the OpenSecureChannel exchange, DER encoded.</param>
        /// <returns>The outcome. Anything but
        /// <see cref="QuicPeerBindingResult.Bound"/> obliges the verifier
        /// to abort the SecureChannel.</returns>
        public static QuicPeerBindingResult Verify(
            X509Certificate2? tlsCertificate,
            ReadOnlySpan<byte> endpointCertificate,
            ReadOnlySpan<byte> secureChannelCertificate)
        {
            if (tlsCertificate == null)
            {
                return QuicPeerBindingResult.NoTlsCertificate;
            }

            if (endpointCertificate.IsEmpty)
            {
                return QuicPeerBindingResult.NoEndpointCertificate;
            }

            if (secureChannelCertificate.IsEmpty)
            {
                return QuicPeerBindingResult.NoSecureChannelCertificate;
            }

            byte[] tlsKey;

            try
            {
                tlsKey = ExportSubjectPublicKeyInfo(tlsCertificate);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return QuicPeerBindingResult.MalformedCertificate;
            }

            if (!TryGetPublicKey(endpointCertificate, out byte[]? endpointKey))
            {
                return QuicPeerBindingResult.MalformedCertificate;
            }

            if (!TryGetPublicKey(secureChannelCertificate, out byte[]? channelKey))
            {
                return QuicPeerBindingResult.MalformedCertificate;
            }

            if (!AreEqual(tlsKey, endpointKey))
            {
                return QuicPeerBindingResult.EndpointKeyMismatch;
            }

            return AreEqual(tlsKey, channelKey)
                ? QuicPeerBindingResult.Bound
                : QuicPeerBindingResult.SecureChannelKeyMismatch;
        }

        /// <summary>
        /// Verifies that the certificate presented in the TLS handshake
        /// and the certificate carried by OpenSecureChannel carry the same
        /// public key.
        /// </summary>
        /// <param name="tlsCertificate">The certificate the peer acting
        /// as the TLS client presented.</param>
        /// <param name="secureChannelCertificate">The certificate carried
        /// by the OpenSecureChannel exchange, DER encoded.</param>
        /// <returns>The outcome. Anything but
        /// <see cref="QuicPeerBindingResult.Bound"/> obliges the verifier
        /// to abort the SecureChannel.</returns>
        public static QuicPeerBindingResult Verify(
            X509Certificate2? tlsCertificate,
            ReadOnlySpan<byte> secureChannelCertificate)
        {
            if (tlsCertificate == null)
            {
                return QuicPeerBindingResult.NoTlsCertificate;
            }

            if (secureChannelCertificate.IsEmpty)
            {
                return QuicPeerBindingResult.NoSecureChannelCertificate;
            }

            byte[] tlsKey;

            try
            {
                tlsKey = ExportSubjectPublicKeyInfo(tlsCertificate);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return QuicPeerBindingResult.MalformedCertificate;
            }

            if (!TryGetPublicKey(secureChannelCertificate, out byte[]? channelKey))
            {
                return QuicPeerBindingResult.MalformedCertificate;
            }

            return AreEqual(tlsKey, channelKey)
                ? QuicPeerBindingResult.Bound
                : QuicPeerBindingResult.SecureChannelKeyMismatch;
        }

        /// <summary>
        /// The StatusCode a failed binding aborts the SecureChannel with.
        /// </summary>
        /// <param name="result">The outcome.</param>
        public static StatusCode ToStatusCode(QuicPeerBindingResult result)
        {
            return result switch
            {
                QuicPeerBindingResult.Bound => StatusCodes.Good,
                QuicPeerBindingResult.EndpointKeyMismatch => StatusCodes.BadCertificateInvalid,
                QuicPeerBindingResult.MalformedCertificate => StatusCodes.BadCertificateInvalid,
                _ => StatusCodes.BadSecurityChecksFailed
            };
        }

        private static bool TryGetPublicKey(ReadOnlySpan<byte> der, out byte[]? key)
        {
            key = null;

            try
            {
#if NET9_0_OR_GREATER
                using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(der);
#else
                using var certificate = new X509Certificate2(der.ToArray());
#endif
                key = ExportSubjectPublicKeyInfo(certificate);
                return true;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static byte[] ExportSubjectPublicKeyInfo(X509Certificate2 certificate)
        {
#if NET5_0_OR_GREATER
            return certificate.PublicKey.ExportSubjectPublicKeyInfo();
#else
            PublicKey publicKey = certificate.PublicKey;
            byte[] algorithmIdentifier = EncodeSequence(
                EncodeOid(publicKey.Oid.Value),
                publicKey.EncodedParameters.RawData);
            byte[] subjectPublicKey = EncodeBitString(publicKey.EncodedKeyValue.RawData);
            return EncodeSequence(algorithmIdentifier, subjectPublicKey);
#endif
        }

#if !NET5_0_OR_GREATER
        private static byte[] EncodeSequence(params byte[][] values)
        {
            int length = 0;
            foreach (byte[] value in values)
            {
                length += value.Length;
            }

            byte[] encodedLength = EncodeLength(length);
            byte[] result = new byte[1 + encodedLength.Length + length];
            result[0] = 0x30;
            Buffer.BlockCopy(encodedLength, 0, result, 1, encodedLength.Length);

            int offset = 1 + encodedLength.Length;
            foreach (byte[] value in values)
            {
                Buffer.BlockCopy(value, 0, result, offset, value.Length);
                offset += value.Length;
            }

            return result;
        }

        private static byte[] EncodeBitString(byte[] value)
        {
            byte[] encodedLength = EncodeLength(value.Length + 1);
            byte[] result = new byte[1 + encodedLength.Length + 1 + value.Length];
            result[0] = 0x03;
            Buffer.BlockCopy(encodedLength, 0, result, 1, encodedLength.Length);
            result[1 + encodedLength.Length] = 0;
            Buffer.BlockCopy(value, 0, result, 1 + encodedLength.Length + 1, value.Length);
            return result;
        }

        private static byte[] EncodeOid(string? dottedDecimal)
        {
            if (string.IsNullOrWhiteSpace(dottedDecimal))
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "The certificate public key algorithm OID is missing.");
            }

            string[] parts = dottedDecimal.Split('.');
            if (parts.Length < 2 ||
                !uint.TryParse(parts[0], out uint first) ||
                !uint.TryParse(parts[1], out uint second))
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "The certificate public key algorithm OID is invalid.");
            }

            var body = new List<byte>
            {
                checked((byte)((first * 40) + second))
            };

            for (int ii = 2; ii < parts.Length; ii++)
            {
                if (!ulong.TryParse(parts[ii], out ulong value))
                {
                    throw new System.Security.Cryptography.CryptographicException(
                        "The certificate public key algorithm OID is invalid.");
                }

                var segment = new Stack<byte>();
                segment.Push((byte)(value & 0x7F));
                value >>= 7;
                while (value > 0)
                {
                    segment.Push((byte)(0x80 | (value & 0x7F)));
                    value >>= 7;
                }

                while (segment.Count > 0)
                {
                    body.Add(segment.Pop());
                }
            }

            byte[] encodedLength = EncodeLength(body.Count);
            byte[] result = new byte[1 + encodedLength.Length + body.Count];
            result[0] = 0x06;
            Buffer.BlockCopy(encodedLength, 0, result, 1, encodedLength.Length);
            body.CopyTo(result, 1 + encodedLength.Length);
            return result;
        }

        private static byte[] EncodeLength(int length)
        {
            if (length < 0x80)
            {
                return new[] { (byte)length };
            }

            var bytes = new Stack<byte>();
            int value = length;
            while (value > 0)
            {
                bytes.Push((byte)(value & 0xFF));
                value >>= 8;
            }

            byte[] result = new byte[1 + bytes.Count];
            result[0] = (byte)(0x80 | bytes.Count);
            int offset = 1;
            while (bytes.Count > 0)
            {
                result[offset++] = bytes.Pop();
            }

            return result;
        }
#endif

        private static bool AreEqual(byte[] left, byte[]? right)
        {
            if (right == null || left.Length != right.Length)
            {
                return false;
            }

            // A public key is not secret, but comparing in constant time
            // costs nothing and keeps the check free of an early exit an
            // attacker could time.
            int difference = 0;

            for (int ii = 0; ii < left.Length; ii++)
            {
                difference |= left[ii] ^ right[ii];
            }

            return difference == 0;
        }
    }
}
