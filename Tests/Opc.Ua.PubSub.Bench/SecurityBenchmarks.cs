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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Bench
{
    /// <summary>
    /// AES-128-CTR sign+encrypt round-trip benchmark per
    /// NetworkMessage. Drives
    /// <see cref="UadpSecurityWrapper"/> with a fixed key ring and a
    /// 256-byte payload to measure the per-message security overhead.
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3">
    /// Part 14 §7.2.4.4.3 PubSub message security</see>.
    /// </summary>
    [MemoryDiagnoser]
    public class SecurityBenchmarks
    {
        private static readonly byte[] s_outerPrefix =
            [0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x01];

        private byte[] m_payload = null!;
        private UadpSecurityWrapper m_sender = null!;
        private UadpSecurityWrapper m_receiver = null!;
        private ReadOnlyMemory<byte> m_wrapped;

        /// <summary>
        /// Cleartext payload size in bytes.
        /// </summary>
        [Params(64, 256, 1024)]
        public int PayloadSize { get; set; }

        [GlobalSetup]
        public async Task SetupAsync()
        {
            m_payload = new byte[PayloadSize];
            for (int i = 0; i < PayloadSize; i++)
            {
                m_payload[i] = (byte)(i & 0xFF);
            }

            PubSubAes128CtrPolicy policy = PubSubAes128CtrPolicy.Instance;
            const uint tokenId = 7U;
            byte[] signing = new byte[policy.SigningKeyLength];
            byte[] encrypting = new byte[policy.EncryptingKeyLength];
            byte[] keyNonce = new byte[policy.NonceLength];
            for (int i = 0; i < signing.Length; i++)
            {
                signing[i] = (byte)((tokenId * 31u + (uint)i) & 0xFF);
            }
            for (int i = 0; i < encrypting.Length; i++)
            {
                encrypting[i] = (byte)((tokenId * 17u + (uint)i + 1u) & 0xFF);
            }
            for (int i = 0; i < keyNonce.Length; i++)
            {
                keyNonce[i] = (byte)((tokenId * 7u + (uint)i + 2u) & 0xFF);
            }
            var key = new PubSubSecurityKey(
                tokenId,
                ByteString.Create(signing),
                ByteString.Create(encrypting),
                ByteString.Create(keyNonce),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(5));

            var senderRing = new PubSubSecurityKeyRing("group");
            senderRing.SetCurrent(key);
            var senderProvider = new StaticSecurityKeyProvider("group", senderRing);
            var nonceProvider = new RandomNonceProvider(PublisherId.FromUInt32(0xDEADBEEFU));
            var senderWindow = new SecurityTokenWindow();
            ITelemetryContext telemetry = NullTelemetryContext.Instance;
            m_sender = new UadpSecurityWrapper(
                policy, senderProvider, nonceProvider, senderWindow, telemetry);

            var receiverRing = new PubSubSecurityKeyRing("group");
            receiverRing.SetCurrent(key);
            var receiverProvider = new StaticSecurityKeyProvider("group", receiverRing);
            var receiverWindow = new SecurityTokenWindow();
            receiverWindow.RegisterToken(tokenId);
            m_receiver = new UadpSecurityWrapper(
                policy, receiverProvider,
                new RandomNonceProvider(PublisherId.FromUInt32(0xDEADBEEFU)),
                receiverWindow,
                telemetry);

            m_wrapped = await m_sender.WrapAsync(s_outerPrefix, m_payload).ConfigureAwait(false);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> WrapAsync()
            => m_sender.WrapAsync(s_outerPrefix, m_payload);

        [Benchmark]
        public ValueTask<UadpSecurityWrapper.UnwrapResult> UnwrapAsync()
            => m_receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                m_wrapped.Slice(s_outerPrefix.Length));

        private sealed class NullTelemetryContext : TelemetryContextBase
        {
            public static readonly NullTelemetryContext Instance = new();

            private NullTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
