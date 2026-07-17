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
            int keyNonceLength = 12)
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
