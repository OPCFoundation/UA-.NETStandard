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
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Measures the per-chunk symmetric crypto the secure channel performs, in
    /// isolation from the network.
    /// </summary>
    /// <remarks>
    /// Every other benchmark in the suite measures a full round trip, where the
    /// network dominates and a change of a few hundred nanoseconds is invisible.
    /// This one is what makes a claim about the cost of the per message path
    /// checkable: it is the only place the symmetric encrypt and sign work is
    /// measured on its own.
    /// <para>
    /// Run with:
    /// <c>dotnet run -c Release -f net10.0 -- --filter '*SymmetricChannelCryptoBenchmarks*'</c>
    /// </para>
    /// <para>
    /// The methods double as NUnit tests so the measured code stays correct and
    /// compiled even when nobody runs the benchmark.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("CryptoProvider")]
    [Category("Benchmark")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [MemoryDiagnoser]
    [BenchmarkCategory("SymmetricChannelCrypto")]
    [NonParallelizable]
    public class SymmetricChannelCryptoBenchmarks
    {
        /// <summary>
        /// The size of the message body, in bytes.
        /// </summary>
        /// <remarks>
        /// [Params(1024, 8192, 65536)]
        /// </remarks>
        public int PayloadSize { get; set; } = 8192;

        /// <summary>
        /// The security policy whose algorithms are measured.
        /// </summary>
        public string SecurityPolicyUri { get; set; } = SecurityPolicies.Basic256Sha256;

        [SetUp]
        [GlobalSetup]
        public void Setup()
        {
            m_policy = SecurityPolicyRegistry.Default.GetInfo(SecurityPolicyUri)
                ?? throw new InvalidOperationException(
                    $"{SecurityPolicyUri} is not supported on this platform.");

            m_encryptingKey = new byte[m_policy.SymmetricEncryptionKeyLength];
            m_signingKey = new byte[m_policy.DerivedSignatureKeyLength];
            m_iv = new byte[m_policy.InitializationVectorLength];

            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(m_encryptingKey);
            rng.GetBytes(m_signingKey);
            rng.GetBytes(m_iv);

            m_hmac = m_policy.CreateSignatureHmac(m_signingKey);

            // Leave room for padding and the signature, as the channel does.
            m_buffer = new byte[PayloadSize + 512];
            rng.GetBytes(m_buffer);
        }

        [TearDown]
        [GlobalCleanup]
        public void Cleanup()
        {
            m_hmac?.Dispose();
            m_hmac = null;
        }

        /// <summary>
        /// Encrypt and sign one chunk, which is what the channel does per message.
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void EncryptAndSign()
        {
            var data = new ArraySegment<byte>(m_buffer, HeaderSize, PayloadSize);

            ArraySegment<byte> result = CryptoUtils.SymmetricEncryptAndSign(
                data, m_policy, m_encryptingKey, m_iv, m_signingKey, m_hmac);

            Assert.That(result, Is.Not.Empty);
        }

        /// <summary>
        /// Sign only, which is what a channel in Sign mode does per message.
        /// </summary>
        [Benchmark]
        [Test]
        public void SignOnly()
        {
            var data = new ArraySegment<byte>(m_buffer, HeaderSize, PayloadSize);

            ArraySegment<byte> result = CryptoUtils.SymmetricEncryptAndSign(
                data, m_policy, m_encryptingKey, m_iv, m_signingKey, m_hmac, signOnly: true);

            Assert.That(result, Is.Not.Empty);
        }

        /// <summary>
        /// A round trip, which is what a request and its response cost together.
        /// </summary>
        /// <remarks>
        /// The decrypt side is given the pre-allocated HMAC, which is what the
        /// channel does: it keeps one per token rather than building one per
        /// chunk.
        /// </remarks>
        [Benchmark]
        [Test]
        public void EncryptSignThenDecryptVerify()
        {
            var data = new ArraySegment<byte>(m_buffer, HeaderSize, PayloadSize);

            ArraySegment<byte> protectedData = CryptoUtils.SymmetricEncryptAndSign(
                data, m_policy, m_encryptingKey, m_iv, m_signingKey, m_hmac);

            var toVerify = new ArraySegment<byte>(
                protectedData.Array!, HeaderSize, protectedData.Count - HeaderSize);

            ArraySegment<byte> plain = CryptoUtils.SymmetricDecryptAndVerify(
                toVerify, m_policy, m_encryptingKey, m_iv, m_signingKey,
                signOnly: false, tokenId: 0, lastSequenceNumber: 0, hmac: m_hmac);

            Assert.That(plain, Is.Not.Empty);
        }

        /// <summary>
        /// The same round trip without a caller supplied HMAC, which is the
        /// fallback path for callers outside the channel.
        /// </summary>
        [Benchmark]
        [Test]
        public void EncryptSignThenDecryptVerifyWithoutSharedHmac()
        {
            var data = new ArraySegment<byte>(m_buffer, HeaderSize, PayloadSize);

            ArraySegment<byte> protectedData = CryptoUtils.SymmetricEncryptAndSign(
                data, m_policy, m_encryptingKey, m_iv, m_signingKey, m_hmac);

            var toVerify = new ArraySegment<byte>(
                protectedData.Array!, HeaderSize, protectedData.Count - HeaderSize);

            ArraySegment<byte> plain = CryptoUtils.SymmetricDecryptAndVerify(
                toVerify, m_policy, m_encryptingKey, m_iv, m_signingKey);

            Assert.That(plain, Is.Not.Empty);
        }

        /// <summary>
        /// The same round trip through a registered symmetric provider, which is
        /// what a deployment running a validated module pays.
        /// </summary>
        /// <remarks>
        /// This is the measurement the seam is gated on. The benchmark above is
        /// the baseline: the difference between the two is the whole cost of the
        /// indirection, and it is only paid by a deployment that asked for it —
        /// resolution yields nothing when no provider is registered, so the
        /// default configuration takes the path measured above with no interface
        /// dispatch at all.
        /// </remarks>
        [Benchmark]
        [Test]
        public void EncryptSignThenDecryptVerifyThroughProvider()
        {
            var data = new ArraySegment<byte>(m_buffer, HeaderSize, PayloadSize);

            ArraySegment<byte> protectedData = CryptoUtils.SymmetricEncryptAndSign(
                data, m_policy, m_encryptingKey, m_iv, m_signingKey, null,
                signOnly: false, tokenId: 0, lastSequenceNumber: 0,
                provider: PlatformSymmetricCryptoProvider.Instance);

            var toVerify = new ArraySegment<byte>(
                protectedData.Array!, HeaderSize, protectedData.Count - HeaderSize);

            ArraySegment<byte> plain = CryptoUtils.SymmetricDecryptAndVerify(
                toVerify, m_policy, m_encryptingKey, m_iv, m_signingKey,
                signOnly: false, tokenId: 0, lastSequenceNumber: 0, hmac: null,
                provider: PlatformSymmetricCryptoProvider.Instance);

            Assert.That(plain, Is.Not.Empty);
        }

        private const int HeaderSize = 24;

        private SecurityPolicyInfo m_policy;
        private byte[] m_buffer;
        private byte[] m_encryptingKey;
        private byte[] m_signingKey;
        private byte[] m_iv;
        private HMAC m_hmac;
    }
}
