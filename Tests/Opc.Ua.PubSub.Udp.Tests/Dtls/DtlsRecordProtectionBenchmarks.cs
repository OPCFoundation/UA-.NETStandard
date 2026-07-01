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

#if NET8_0_OR_GREATER
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Post-handshake DTLS record throughput benchmark for Part 14 §7.3.2.4.
    /// The <see cref="BenchmarkDotNet.Attributes.BenchmarkAttribute"/> methods
    /// measure the seal/open hot path; the NUnit tests keep them exercised in CI.
    /// </summary>
    [TestFixture]
    [Category("Benchmark")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    public class DtlsRecordProtectionBenchmarks
    {
        /// <summary>
        /// Payload size in bytes for the protected datagram.
        /// </summary>
        [Params(64, 256, 1024)]
        public int PayloadSize { get; set; } = 256;

        private DtlsProfile m_profile;
        private byte[] m_trafficSecret;
        private byte[] m_payload;
        private DtlsRecordProtection m_writer;
        private DtlsRecordProtection m_reader;

        /// <summary>
        /// Allocates the keys, payload, and the writer/reader record contexts.
        /// </summary>
        [GlobalSetup]
        [OneTimeSetUp]
        public void Setup()
        {
            var registry = new DtlsProfileRegistry();
            if (!registry.TryResolve("ECC_nistP256_AesGcm", out DtlsProfile? profile))
            {
                Assert.Ignore("DTLS profile 'ECC_nistP256_AesGcm' is not available from this platform BCL.");
                return;
            }
            m_profile = profile!;
            m_trafficSecret = RandomNumberGenerator.GetBytes(32);
            m_payload = RandomNumberGenerator.GetBytes(PayloadSize);
            m_writer = new DtlsRecordProtection(m_profile, m_trafficSecret, epoch: 3);
            m_reader = new DtlsRecordProtection(m_profile, m_trafficSecret, epoch: 3);
        }

        /// <summary>
        /// Releases the record contexts and zeroizes the key material.
        /// </summary>
        [GlobalCleanup]
        [OneTimeTearDown]
        public void Cleanup()
        {
            m_writer?.Dispose();
            m_reader?.Dispose();
            if (m_trafficSecret is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(m_trafficSecret);
            }
            if (m_payload is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(m_payload);
            }
        }

        /// <summary>
        /// Benchmarks sealing a single DTLS record.
        /// </summary>
        [Benchmark]
        public byte[] Seal()
        {
            return m_writer.Seal(m_payload);
        }

        /// <summary>
        /// Benchmarks the full seal then open round-trip of a DTLS record.
        /// </summary>
        [Benchmark]
        public byte[] SealAndOpen()
        {
            byte[] record = m_writer.Seal(m_payload);
            return m_reader.Open(record);
        }

        /// <summary>
        /// Verifies the benchmarked seal/open round-trip recovers the payload
        /// (Part 14 §7.3.2.4 DTLS record protection).
        /// </summary>
        [Test]
        public void SealAndOpenRoundTripsPayload()
        {
            byte[] record = m_writer.Seal(m_payload);
            byte[] plaintext = m_reader.Open(record);
            Assert.That(plaintext, Is.EqualTo(m_payload));
        }
    }
}
#endif
