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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers the guard that rejects key material a provider did not produce.
    /// </summary>
    /// <remarks>
    /// <c>DeriveKey</c> and <c>GetBytes</c> return <see langword="void"/>, so a
    /// provider that no-ops or clears without filling is indistinguishable from
    /// one that succeeded, and the buffer becomes channel keys and nonces. Both
    /// ends of a channel usually run the same image, so both would derive the
    /// same dead keys and the handshake would complete - traffic flowing with no
    /// confidentiality, invisible to the operator and to the peer.
    /// </remarks>
    [TestFixture]
    [Category("Security")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public class CryptoProviderOutputTests
    {
        [Test]
        public void ASourceThatNeverWritesIsRejected()
        {
            var source = new NoOpRandomSource();

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => Nonce.CreateRandomNonceData(32, false, source))!;

            Assert.Multiple(() =>
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(sre.Message, Does.Contain("NoOp"));
            });
        }

        [Test]
        public void ASourceThatOnlyZeroesIsRejected()
        {
            var source = new ZeroingRandomSource();

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => Nonce.CreateRandomNonceData(32, false, source))!;

            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void ASourceThatFillsIsAccepted()
        {
            var source = new CountingRandomSource();

            byte[] nonce = Nonce.CreateRandomNonceData(32, false, source);

            Assert.Multiple(() =>
            {
                Assert.That(nonce, Has.Length.EqualTo(32));
                Assert.That(source.Calls, Is.EqualTo(1));

                // The stamp must not survive into the nonce.
                Assert.That(Array.TrueForAll(nonce, b => b == 0xA5), Is.False);
            });
        }

        /// <summary>
        /// The platform source is unaffected: it is not routed through the guard,
        /// because it cannot fail this way.
        /// </summary>
        [Test]
        public void ThePlatformSourceStillProducesANonce()
        {
            byte[] nonce = Nonce.CreateRandomNonceData(32, false, null);

            Assert.That(nonce, Has.Length.EqualTo(32));
        }

        private sealed class NoOpRandomSource : ISecureRandomSource
        {
            public void GetBytes(Span<byte> buffer)
            {
                // Deliberately does nothing, as a module whose remote call failed
                // and whose implementation logs and returns would.
            }

            public override string ToString()
            {
                return "NoOp";
            }
        }

        private sealed class ZeroingRandomSource : ISecureRandomSource
        {
            public void GetBytes(Span<byte> buffer)
            {
                buffer.Clear();
            }
        }

        private sealed class CountingRandomSource : ISecureRandomSource
        {
            public int Calls { get; private set; }

            public void GetBytes(Span<byte> buffer)
            {
                Calls++;

                for (int ii = 0; ii < buffer.Length; ii++)
                {
                    buffer[ii] = (byte)(ii + 1);
                }
            }
        }
    }
}
