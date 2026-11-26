// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Helper for building <see cref="PubSubSecurityKey"/> instances
    /// in security-subsystem tests.
    /// </summary>
    internal static class TestSecurityKeyFactory
    {
        public static PubSubSecurityKey Create(
            uint tokenId,
            int signingKeyLength = 32,
            int encryptingKeyLength = 16,
            int keyNonceLength = AesCtrNonceLayout.KeyNonceLength)
        {
            byte[] signing = new byte[signingKeyLength];
            byte[] encrypting = new byte[encryptingKeyLength];
            byte[] keyNonce = new byte[keyNonceLength];
            for (int i = 0; i < signing.Length; i++)
            {
                signing[i] = (byte)(((tokenId * 31u) + (uint)i) & 0xFF);
            }
            for (int i = 0; i < encrypting.Length; i++)
            {
                encrypting[i] = (byte)(((tokenId * 17u) + (uint)i + 1u) & 0xFF);
            }
            for (int i = 0; i < keyNonce.Length; i++)
            {
                keyNonce[i] = (byte)(((tokenId * 7u) + (uint)i + 2u) & 0xFF);
            }
            return new PubSubSecurityKey(
                tokenId,
                ByteString.Create(signing),
                ByteString.Create(encrypting),
                ByteString.Create(keyNonce),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(5));
        }
    }
}
