/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
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
        public DtlsHelloRetryCookieProtector(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty)
            {
                throw new ArgumentException("Cookie MAC key is required.", nameof(key));
            }

            m_key = key.ToArray();
        }

        public byte[] CreateCookie(EndPoint remoteEndPoint, ReadOnlySpan<byte> clientHello)
        {
            byte[] mac = ComputeMac(remoteEndPoint, clientHello);
            byte[] cookie = new byte[1 + mac.Length];
            cookie[0] = Version;
            Buffer.BlockCopy(mac, 0, cookie, 1, mac.Length);
            CryptographicOperations.ZeroMemory(mac);
            return cookie;
        }

        public bool ValidateCookie(EndPoint remoteEndPoint, ReadOnlySpan<byte> clientHello, ReadOnlySpan<byte> cookie)
        {
            if (cookie.Length != 1 + MacLength || cookie[0] != Version)
            {
                return false;
            }

            byte[] expected = CreateCookie(remoteEndPoint, clientHello);
            try
            {
                return CryptographicOperations.FixedTimeEquals(expected, cookie);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(m_key);
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
                    CryptographicOperations.ZeroMemory(endpointBytes);
                    CryptographicOperations.ZeroMemory(helloBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private const byte Version = 1;
        private const int MacLength = 32;

        private readonly byte[] m_key;
        private bool m_disposed;
    }
}
