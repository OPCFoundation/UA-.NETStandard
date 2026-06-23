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
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// ECDHE key_share support for DTLS 1.3 PubSub profiles.
    /// </summary>
    internal sealed class DtlsEcdheKeyExchange : IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsEcdheKeyExchange"/> and generates an ephemeral
        /// key pair on the supplied named curve.
        /// </summary>
        public DtlsEcdheKeyExchange(DtlsNamedCurve curve)
        {
            Curve = curve;
            m_ecdh = ECDiffieHellman.Create(ToEccCurve(curve));
            ECParameters publicParameters = m_ecdh.ExportParameters(includePrivateParameters: false);
            try
            {
                PublicKey = EncodePoint(curve, publicParameters.Q);
            }
            finally
            {
                ClearPoint(publicParameters.Q);
            }
        }

        /// <summary>
        /// Named group this key exchange was created for.
        /// </summary>
        public DtlsNamedCurve Curve { get; }

        /// <summary>
        /// Encoded ephemeral public key share sent to the peer.
        /// </summary>
        public byte[] PublicKey { get; }

        /// <summary>
        /// Derives the raw ECDHE shared secret from the peer key share.
        /// </summary>
        public byte[] DeriveSharedSecret(ReadOnlySpan<byte> peerKeyShare)
        {
            ECPoint peerPoint = DecodePoint(Curve, peerKeyShare);
            var peerParameters = new ECParameters
            {
                Curve = ToEccCurve(Curve),
                Q = peerPoint
            };
            try
            {
                using var peer = ECDiffieHellman.Create(peerParameters);
#if NET8_0_OR_GREATER
                return m_ecdh.DeriveRawSecretAgreement(peer.PublicKey);
#else
                throw new NotSupportedException("Raw ECDHE shared-secret extraction requires .NET 8 or later.");
#endif
            }
            finally
            {
                ClearPoint(peerPoint);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_ecdh.Dispose();
            DtlsCryptographicOperations.ZeroMemory(PublicKey);
            m_disposed = true;
        }

        /// <summary>
        /// Maps a <see cref="DtlsNamedCurve"/> to the matching BCL <see cref="ECCurve"/>,
        /// rejecting curves the portable .NET BCL cannot support.
        /// </summary>
        public static ECCurve ToEccCurve(DtlsNamedCurve curve)
        {
            return curve switch
            {
                DtlsNamedCurve.NistP256 => ECCurve.NamedCurves.nistP256,
                DtlsNamedCurve.NistP384 => ECCurve.NamedCurves.nistP384,
                DtlsNamedCurve.BrainpoolP256r1 => ECCurve.CreateFromValue("1.3.36.3.3.2.8.1.1.7"),
                DtlsNamedCurve.BrainpoolP384r1 => ECCurve.CreateFromValue("1.3.36.3.3.2.8.1.1.11"),
                DtlsNamedCurve.Curve25519 => throw new DtlsHandshakeException(
                    "Curve25519 is unsupported by portable .NET BCL ECDH and is rejected fail-closed."),
                DtlsNamedCurve.Curve448 => throw new DtlsHandshakeException(
                    "Curve448 is unsupported by portable .NET BCL ECDH and is rejected fail-closed."),
                _ => throw new DtlsHandshakeException("Unsupported DTLS ECDHE named group.")
            };
        }

        private static byte[] EncodePoint(DtlsNamedCurve curve, ECPoint point)
        {
            int coordinateLength = GetCoordinateLength(curve);
            if (point.X is null ||
                point.Y is null ||
                point.X.Length != coordinateLength ||
                point.Y.Length != coordinateLength)
            {
                throw new CryptographicException("ECDHE public point length does not match the selected group.");
            }

            byte[] output = new byte[1 + coordinateLength + coordinateLength];
            output[0] = 0x04;
            Buffer.BlockCopy(point.X, 0, output, 1, coordinateLength);
            Buffer.BlockCopy(point.Y, 0, output, 1 + coordinateLength, coordinateLength);
            return output;
        }

        private static ECPoint DecodePoint(DtlsNamedCurve curve, ReadOnlySpan<byte> encoded)
        {
            int coordinateLength = GetCoordinateLength(curve);
            if (encoded.Length != 1 + coordinateLength + coordinateLength || encoded[0] != 0x04)
            {
                throw new DtlsHandshakeException("ECDHE key_share must be an uncompressed EC point for the selected group.");
            }

            return new ECPoint
            {
                X = encoded.Slice(1, coordinateLength).ToArray(),
                Y = encoded.Slice(1 + coordinateLength, coordinateLength).ToArray()
            };
        }

        private static int GetCoordinateLength(DtlsNamedCurve curve)
        {
            return curve switch
            {
                DtlsNamedCurve.NistP256 or DtlsNamedCurve.BrainpoolP256r1 => 32,
                DtlsNamedCurve.NistP384 or DtlsNamedCurve.BrainpoolP384r1 => 48,
                DtlsNamedCurve.Curve25519 or DtlsNamedCurve.Curve448 => throw new DtlsHandshakeException(
                    "RFC 7748 groups are unavailable through the .NET BCL and are rejected fail-closed."),
                _ => throw new DtlsHandshakeException("Unsupported DTLS ECDHE named group.")
            };
        }

        private static void ClearPoint(ECPoint point)
        {
            if (point.X is not null)
            {
                DtlsCryptographicOperations.ZeroMemory(point.X);
            }

            if (point.Y is not null)
            {
                DtlsCryptographicOperations.ZeroMemory(point.Y);
            }
        }

        private readonly ECDiffieHellman m_ecdh;
        private bool m_disposed;
    }
}
