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

using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies receiver-side sequence-number acceptance: exactly the next number, valid
    /// wrap arounds, and gaps only on a reconnect.
    /// </summary>
    /// <remarks>
    /// OPC 10000-6 6.7.2.4 increments the SequenceNumber by exactly one per chunk and does not
    /// cap a SecureChannel at a single wrap; OPC 10000-2 5.1.14 closes the channel when a
    /// SequenceNumber is missed.
    /// </remarks>
    [TestFixture]
    [Category("Transport")]
    [Parallelizable(ParallelScope.All)]
    public sealed class UaSCBinaryChannelSequenceRolloverTests
    {
        /// <summary>
        /// Verifies that repeated legal legacy rollovers are accepted.
        /// </summary>
        [Test]
        public void RepeatedLegalRolloversAreAccepted()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 1), Is.True);
                Assert.That(channel.Verify(0), Is.True, "the first wrap is legal.");
                Assert.That(channel.Verify(1), Is.True);

                // a long-lived busy channel reaches the next wrap.
                channel.Reset(TcpMessageLimits.MinSequenceNumber + 5);
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 6), Is.True);
                Assert.That(
                    channel.Verify(3),
                    Is.True,
                    "a later wrap on a long-lived channel is legal and must not fault it.");
            });
        }

        /// <summary>
        /// Verifies that a replayed number after a wrap is rejected.
        /// </summary>
        [Test]
        public void ReplayAfterRolloverIsRejected()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 1), Is.True);
                Assert.That(channel.Verify(0), Is.True);
                Assert.That(channel.Verify(0), Is.False, "a repeated low number is a replay.");
                Assert.That(
                    channel.Verify(TcpMessageLimits.MinSequenceNumber + 2),
                    Is.False,
                    "jumping back to the top of the range is a replay.");
                Assert.That(channel.Verify(1), Is.True);
            });
        }

        /// <summary>
        /// Verifies that non-increasing numbers outside a rollover are always rejected.
        /// </summary>
        [Test]
        public void NonIncreasingSequenceNumbersAreRejected()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(10), Is.True);
                Assert.That(channel.Verify(10), Is.False, "a repeated number is a replay.");
                Assert.That(channel.Verify(9), Is.False, "a lower number outside the window is a replay.");
                Assert.That(channel.Verify(11), Is.True);
            });
        }

        /// <summary>
        /// OPC 10000-2 5.1.14: a missed SequenceNumber (a suppressed chunk) is rejected.
        /// </summary>
        [Test]
        public void SequenceNumberGapIsRejected()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(10), Is.True);
                Assert.That(channel.Verify(12), Is.False, "a skipped number means a chunk was suppressed.");
                Assert.That(channel.Verify(11), Is.True);

                // a legacy wrap may only start from the top of the range.
                Assert.That(channel.Verify(0), Is.False);
            });
        }

        /// <summary>
        /// OPC 10000-6 6.7.2.4: with the non-legacy rules the first number is 0 and the number after
        /// UInt32.MaxValue is 0.
        /// </summary>
        [Test]
        public void NonLegacyRolloverIsExactlyFromMaxValueToZero()
        {
            using SequenceProbe? channel = CreateChannel(SecurityPolicies.ECC_nistP256);
            if (channel == null)
            {
                Assert.Ignore("ECC_nistP256 is not supported on this platform.");
                return;
            }

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(1), Is.False, "the first number is 0.");
                Assert.That(channel.Verify(0), Is.True);

                channel.Reset(uint.MaxValue - 1);
                Assert.That(channel.Verify(0), Is.False, "the wrap only follows UInt32.MaxValue.");
                Assert.That(channel.Verify(uint.MaxValue), Is.True);
                Assert.That(channel.Verify(1), Is.False, "the first number after the wrap is 0.");
                Assert.That(channel.Verify(0), Is.True);
                Assert.That(channel.Verify(1), Is.True);
            });
        }

        /// <summary>
        /// A reconnect on a new socket may skip the numbers of chunks lost on the dropped connection,
        /// but may not go back.
        /// </summary>
        [Test]
        public void ReconnectMaySkipAheadButNotBack()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(10), Is.True);
                Assert.That(channel.VerifyReconnect(10), Is.False);
                Assert.That(channel.VerifyReconnect(9), Is.False);
                Assert.That(channel.VerifyReconnect(15), Is.True);
                Assert.That(channel.Verify(16), Is.True);
                Assert.That(channel.Verify(18), Is.False);
            });
        }

        /// <summary>
        /// After the counter wrapped, a reconnect may still skip ahead across the
        /// wrap but must not accept a number from before the wrap again.
        /// </summary>
        [Test]
        public void ReconnectAfterRolloverRejectsPreWrapNumbers()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 1), Is.True);
                Assert.That(channel.Verify(10), Is.True, "the legacy wrap is legal.");
                Assert.That(channel.VerifyReconnect(uint.MaxValue), Is.False, "a pre-wrap number is a replay.");
                Assert.That(
                    channel.VerifyReconnect(TcpMessageLimits.MinSequenceNumber + 2),
                    Is.False,
                    "a pre-wrap number is a replay.");
                Assert.That(channel.VerifyReconnect(20), Is.True);

                // a reconnect may itself skip across a wrap.
                channel.Reset(uint.MaxValue - 5);
                Assert.That(channel.VerifyReconnect(3), Is.True);
            });
        }

        private static SequenceProbe CreateChannel()
        {
            return CreateChannel(SecurityPolicies.None)!;
        }

        private static SequenceProbe? CreateChannel(string policyUri)
        {
            if (SecurityPolicies.Default.GetInfo(policyUri) == null)
            {
                return null;
            }

            var telemetry = new Mock<ITelemetryContext>();
            var context = ServiceMessageContext.Create(telemetry.Object);
            var buffers = new BufferManager("sequence-rollover", 65536, telemetry.Object);
            return new SequenceProbe(
                buffers,
                new ChannelQuotas(context),
                policyUri == SecurityPolicies.None ? MessageSecurityMode.None : MessageSecurityMode.Sign,
                policyUri,
                telemetry.Object);
        }

        /// <summary>
        /// Exposes the protected receiver-side sequence check.
        /// </summary>
        private sealed class SequenceProbe : UaSCUaBinaryChannel
        {
            /// <summary>
            /// Creates a probe channel that performs no security processing.
            /// </summary>
            public SequenceProbe(
                BufferManager buffers,
                ChannelQuotas quotas,
                MessageSecurityMode mode,
                string policyUri,
                ITelemetryContext telemetry)
                : base(
                    "sequence-rollover",
                    buffers,
                    quotas,
                    (Certificate?)null,
                    null,
                    mode,
                    policyUri,
                    telemetry)
            {
            }

            /// <summary>
            /// Runs the receiver-side sequence-number check.
            /// </summary>
            /// <param name="sequenceNumber">The received sequence number.</param>
            /// <returns>Whether the number was accepted.</returns>
            public bool Verify(uint sequenceNumber)
            {
                return VerifySequenceNumber(sequenceNumber, "test");
            }

            /// <summary>
            /// Runs the receiver-side sequence-number check for a reconnect.
            /// </summary>
            /// <param name="sequenceNumber">The received sequence number.</param>
            /// <returns>Whether the number was accepted.</returns>
            public bool VerifyReconnect(uint sequenceNumber)
            {
                return VerifySequenceNumberCore(sequenceNumber, "test", true);
            }

            /// <summary>
            /// Sets the last received sequence number.
            /// </summary>
            /// <param name="sequenceNumber">The last received sequence number.</param>
            public void Reset(uint sequenceNumber)
            {
                ResetSequenceNumber(sequenceNumber);
            }
        }
    }
}
