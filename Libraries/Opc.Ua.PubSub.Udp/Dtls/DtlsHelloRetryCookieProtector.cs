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
using System.Net;
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Stateless HelloRetryRequest cookie protection per RFC 9147 §5.1.
    /// </summary>
    internal sealed class DtlsHelloRetryCookieProtector : IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsHelloRetryCookieProtector"/> with the MAC key
        /// used to authenticate stateless HelloRetryRequest cookies.
        /// </summary>
        public DtlsHelloRetryCookieProtector(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty)
            {
                throw new ArgumentException("Cookie MAC key is required.", nameof(key));
            }

            m_key = key.ToArray();
        }

        /// <summary>
        /// Creates a stateless cookie binding the remote endpoint to the initial ClientHello.
        /// </summary>
        public byte[] CreateCookie(EndPoint remoteEndPoint, ReadOnlySpan<byte> clientHello)
        {
            byte[] mac = ComputeMac(remoteEndPoint, clientHello);
            byte[] cookie = new byte[1 + mac.Length];
            cookie[0] = Version;
            Buffer.BlockCopy(mac, 0, cookie, 1, mac.Length);
            CryptoUtils.ZeroMemory(mac);
            return cookie;
        }

        /// <summary>
        /// Validates a cookie returned by the client against the remote endpoint and ClientHello.
        /// </summary>
        public bool ValidateCookie(EndPoint remoteEndPoint, ReadOnlySpan<byte> clientHello, ReadOnlySpan<byte> cookie)
        {
            if (cookie.Length != 1 + MacLength || cookie[0] != Version)
            {
                return false;
            }

            byte[] expected = CreateCookie(remoteEndPoint, clientHello);
            try
            {
                return CryptoUtils.FixedTimeEquals(expected, cookie);
            }
            finally
            {
                CryptoUtils.ZeroMemory(expected);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            CryptoUtils.ZeroMemory(m_key);
            m_disposed = true;
        }

        private byte[] ComputeMac(EndPoint remoteEndPoint, ReadOnlySpan<byte> clientHello)
        {
            byte[] key = (byte[])m_key.Clone();
            try
            {
                using HMACSHA256 hmac = new(key);
                byte[] endpointBytes = System.Text.Encoding.UTF8.GetBytes(remoteEndPoint.ToString() ?? string.Empty);
                byte[] helloBytes = clientHello.ToArray();
                try
                {
                    _ = hmac.TransformBlock(endpointBytes, 0, endpointBytes.Length, endpointBytes, 0);
                    _ = hmac.TransformFinalBlock(helloBytes, 0, helloBytes.Length);
                    byte[] hash = hmac.Hash ?? throw new CryptographicException("Cookie HMAC did not produce a hash.");
                    Array.Resize(ref hash, MacLength);
                    return hash;
                }
                finally
                {
                    CryptoUtils.ZeroMemory(endpointBytes);
                    CryptoUtils.ZeroMemory(helloBytes);
                }
            }
            finally
            {
                CryptoUtils.ZeroMemory(key);
            }
        }

        private const byte Version = 1;
        private const int MacLength = 32;

        private readonly byte[] m_key;
        private bool m_disposed;
    }
}
