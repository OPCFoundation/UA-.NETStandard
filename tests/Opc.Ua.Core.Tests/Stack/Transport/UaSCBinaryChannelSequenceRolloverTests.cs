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

#nullable enable

using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies receiver-side sequence-number acceptance across repeated rollovers.
    /// </summary>
    /// <remarks>
    /// OPC 10000-6 does not cap a SecureChannel at a single wrap, so a long-lived
    /// busy channel must keep accepting later legal rollovers.
    /// </remarks>
    [TestFixture]
    [Category("Transport")]
    [Parallelizable(ParallelScope.All)]
    public sealed class UaSCBinaryChannelSequenceRolloverTests
    {
        /// <summary>
        /// Verifies that a second legal rollover is accepted once the counter left the window.
        /// </summary>
        [Test]
        public void RepeatedLegalRolloversAreAccepted()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 1), Is.True);
                Assert.That(channel.Verify(0), Is.True, "the first wrap is legal.");

                // Leave the rollover window through the normal range so the next wrap is fresh.
                Assert.That(channel.Verify(TcpMessageLimits.MaxRolloverSequenceNumber), Is.True);
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 1), Is.True);

                Assert.That(
                    channel.Verify(0),
                    Is.True,
                    "a later wrap on a long-lived channel is legal and must not fault it.");
            });
        }

        /// <summary>
        /// Verifies that a replayed low number inside the rollover window is still rejected.
        /// </summary>
        [Test]
        public void SecondRolloverInsideTheSameWindowIsRejected()
        {
            using SequenceProbe channel = CreateChannel();

            Assert.Multiple(() =>
            {
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 1), Is.True);
                Assert.That(channel.Verify(0), Is.True);

                // A jump straight back to the top of the range never traverses the normal
                // range, so the one-wrap-per-window guard stays armed.
                Assert.That(channel.Verify(TcpMessageLimits.MinSequenceNumber + 2), Is.True);
                Assert.That(
                    channel.Verify(0),
                    Is.False,
                    "repeated wraps without leaving the window remain a replay and must be rejected.");
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

        private static SequenceProbe CreateChannel()
        {
            var telemetry = new Mock<ITelemetryContext>();
            var context = ServiceMessageContext.Create(telemetry.Object);
            var buffers = new BufferManager("sequence-rollover", 65536, telemetry.Object);
            return new SequenceProbe(buffers, new ChannelQuotas(context), telemetry.Object);
        }

        /// <summary>
        /// Exposes the protected receiver-side sequence check.
        /// </summary>
        private sealed class SequenceProbe : UaSCUaBinaryChannel
        {
            /// <summary>
            /// Creates a probe channel that performs no security processing.
            /// </summary>
            public SequenceProbe(BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry)
                : base(
                    "sequence-rollover",
                    buffers,
                    quotas,
                    (Certificate?)null,
                    null,
                    MessageSecurityMode.None,
                    SecurityPolicies.None,
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
        }
    }
}
