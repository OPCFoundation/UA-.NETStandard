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
 *
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

#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Nonce validation on the secure-channel path for the RSA Diffie-Hellman
    /// security policies.
    /// </summary>
    /// <remarks>
    /// The peer's nonce carries its Diffie-Hellman public value, and a value
    /// outside 2 &lt;= y &lt;= p-2 is rejected. That rejection is raised as an
    /// exception from the key-agreement code; the channel has to answer
    /// <see langword="false"/> so the caller reports BadNonceInvalid, rather
    /// than letting an unauthenticated peer turn a chosen nonce into an
    /// unhandled exception on the receive path.
    /// </remarks>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class UaSCBinaryChannelNonceTests
    {
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;

        [SetUp]
        public void SetUp()
        {
            // The RSA Diffie-Hellman policies are AEAD, so a platform without
            // AES-GCM does not offer them and the channel would have no security
            // policy to validate against - every case below would then pass by
            // rejecting everything, which proves nothing.
            if (SecurityPolicies.Default.GetInfo(SecurityPolicies.RSA_DH_AesGcm) == null)
            {
                Assert.Ignore(
                    "The RSA_DH_AesGcm security policy is not supported on this platform.");
            }

            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("nonce-test", 8192, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry));
        }

        /// <summary>
        /// Zero, one and a value at or above the modulus all pin the shared
        /// secret to something the peer already knows.
        /// </summary>
        [TestCase((byte)0x00, false)]
        [TestCase((byte)0x01, false)]
        [TestCase((byte)0xFF, true)]
        public void ValidateNonceRejectsDegenerateDiffieHellmanValues(
            byte lastByte,
            bool fillAll)
        {
            using TestChannel channel = CreateChannel();

            byte[] nonce = new byte[NonceLength];

            if (fillAll)
            {
                for (int ii = 0; ii < nonce.Length; ii++)
                {
                    nonce[ii] = lastByte;
                }
            }
            else
            {
                nonce[^1] = lastByte;
            }

            Assert.That(channel.ValidateNonceForTest(nonce), Is.False);
        }

        /// <summary>
        /// A nonce the stack itself produces is accepted, so the range check
        /// rejects the degenerate values and nothing else.
        /// </summary>
        [Test]
        public void ValidateNonceAcceptsAWellFormedNonce()
        {
            using TestChannel channel = CreateChannel();
            using Nonce nonce = Nonce.CreateNonce(SecurityPolicyInfo.RSA_DH_AesGcm);

            byte[]? data = nonce.Data;
            Assert.That(data, Is.Not.Null);
            Assert.That(channel.ValidateNonceForTest(data!), Is.True);
        }

        [Test]
        public void ValidateNonceRejectsAWrongLengthNonce()
        {
            using TestChannel channel = CreateChannel();

            Assert.That(channel.ValidateNonceForTest(new byte[NonceLength - 1]), Is.False);
        }

        private const int NonceLength = 384;

        private TestChannel CreateChannel()
        {
            return new TestChannel(m_buffers, m_quotas, m_telemetry);
        }

        private sealed class TestChannel : UaSCUaBinaryChannel
        {
            public TestChannel(
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry)
                : base(
                    "nonce-test",
                    bufferManager,
                    quotas,
                    (Certificate?)null,
                    new List<EndpointDescription>(),
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.RSA_DH_AesGcm,
                    telemetry)
            {
            }

            public bool ValidateNonceForTest(byte[] nonce)
            {
                return ValidateNonce(null, nonce);
            }
        }
    }
}
