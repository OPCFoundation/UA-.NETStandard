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
using Opc.Ua;
using Opc.Ua.PubSub.Security;

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// Demo-only shared symmetric key material wiring the reference
    /// publisher and subscriber for <c>SignAndEncrypt</c> over the
    /// <c>PubSub-Aes256-CTR</c> policy without an external Security Key
    /// Service. The publisher and subscriber both build an identical
    /// <see cref="StaticSecurityKeyProvider"/> from the constants below.
    /// </summary>
    /// <remarks>
    /// The fixed keys here are a sample convenience ONLY. Production
    /// deployments must source keys from a real SKS (see
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3</see>) and never embed key material in source.
    /// </remarks>
    public static class SampleSecurity
    {
        /// <summary>
        /// SecurityGroupId shared by the demo publisher and subscriber.
        /// </summary>
        public const string SecurityGroupId = "DemoSecurityGroup";

        /// <summary>
        /// Placeholder Security Key Service endpoint URL. The demo sources
        /// keys from a local <see cref="StaticSecurityKeyProvider"/>, but
        /// the configuration validator requires a secured group to declare
        /// at least one SecurityKeyService endpoint per
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.5.4">
        /// Part 14 §6.2.5.4</see>.
        /// </summary>
        public const string SecurityKeyServiceUrl = "opc.tcp://localhost:4840/SecurityKeyService";

        private const uint TokenId = 1U;

        /// <summary>
        /// Builds the shared static key provider. Identical on both the
        /// publisher and subscriber so secured frames round-trip.
        /// </summary>
        /// <param name="timeProvider">Clock for the key ring.</param>
        /// <returns>A configured key provider.</returns>
        public static IPubSubSecurityKeyProvider CreateKeyProvider(
            TimeProvider? timeProvider = null)
        {
            byte[] signingKey = BuildKey(0x10, 32);
            byte[] encryptingKey = BuildKey(0x20, 32);
            byte[] keyNonce = BuildKey(0x30, 12);

            PubSubSecurityKey? key = null;
            PubSubSecurityKeyRing? ring = null;
            try
            {
                key = new PubSubSecurityKey(
                    TokenId,
                    ByteString.Create(signingKey),
                    ByteString.Create(encryptingKey),
                    ByteString.Create(keyNonce),
                    DateTimeUtc.From(DateTime.UtcNow),
                    TimeSpan.FromHours(24));

                ring = new PubSubSecurityKeyRing(SecurityGroupId, timeProvider);
                ring.SetCurrent(key);
                // Ownership of the key transfers to the ring; null out the
                // local so it is not disposed here on the success path.
                key = null;

                var provider = new StaticSecurityKeyProvider(SecurityGroupId, ring);
                // Ownership of the ring transfers to the returned provider,
                // which lives for the application lifetime.
                ring = null;
                return provider;
            }
            finally
            {
                ring?.Dispose();
                key?.Dispose();
            }
        }

        private static byte[] BuildKey(byte seed, int length)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)((seed + (i * 7)) & 0xFF);
            }
            return bytes;
        }
    }
}
