// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
