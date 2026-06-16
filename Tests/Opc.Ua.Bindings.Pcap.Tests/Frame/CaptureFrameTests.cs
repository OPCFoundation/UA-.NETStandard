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
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Bindings.Pcap.Tests.Frame
{
    /// <summary>
    /// Equality and projection-property tests for <see cref="CaptureFrame"/>.
    /// </summary>
    [TestFixture]
    public sealed class CaptureFrameTests
    {
        [Test]
        public void ConstructorAssignsEveryProperty()
        {
            var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            byte[] data = [9, 8, 7, 6];

            var frame = new CaptureFrame(
                timestamp,
                CaptureFrameDirection.ClientToServer,
                "10.0.0.1:55000",
                "10.0.0.2:4840",
                data);

            Assert.That(frame.Timestamp, Is.EqualTo(timestamp));
            Assert.That(frame.Direction, Is.EqualTo(CaptureFrameDirection.ClientToServer));
            Assert.That(frame.ClientEndpoint, Is.EqualTo("10.0.0.1:55000"));
            Assert.That(frame.ServerEndpoint, Is.EqualTo("10.0.0.2:4840"));
            Assert.That(frame.Data.ToArray(), Is.EqualTo(data).AsCollection);
        }

        [Test]
        public void ConstructorNormalisesNullEndpointsToEmpty()
        {
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.Unknown,
                clientEndpoint: null!,
                serverEndpoint: null!,
                data: Array.Empty<byte>());

            Assert.That(frame.ClientEndpoint, Is.EqualTo(string.Empty));
            Assert.That(frame.ServerEndpoint, Is.EqualTo(string.Empty));
        }

        [Test]
        public void EqualsReturnsTrueForIdenticalValues()
        {
            var ts = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
            byte[] data = [1, 2, 3, 4, 5];
            var left = new CaptureFrame(ts, CaptureFrameDirection.ServerToClient, "a", "b", data);
            var right = new CaptureFrame(ts, CaptureFrameDirection.ServerToClient, "a", "b", data);
            bool equalsResult = left.Equals(right);
            bool equalityOperator = left == right;
            bool inequalityOperator = left != right;

            Assert.That(equalsResult, Is.True);
            Assert.That(left, Is.EqualTo(right));
            Assert.That(equalityOperator, Is.True);
            Assert.That(inequalityOperator, Is.False);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void EqualsCompareDataBytesNotReference()
        {
            DateTimeOffset ts = DateTimeOffset.UtcNow;
            var left = new CaptureFrame(
                ts,
                CaptureFrameDirection.ClientToServer,
                "a",
                "b",
                new byte[] { 5, 6, 7 });
            var right = new CaptureFrame(
                ts,
                CaptureFrameDirection.ClientToServer,
                "a",
                "b",
                new byte[] { 5, 6, 7 });

            Assert.That(left, Is.EqualTo(right));
        }

        [Test]
        public void EqualsReturnsFalseWhenDataBytesDiffer()
        {
            DateTimeOffset ts = DateTimeOffset.UtcNow;
            var left = new CaptureFrame(
                ts,
                CaptureFrameDirection.ClientToServer,
                "a",
                "b",
                new byte[] { 1 });
            var right = new CaptureFrame(
                ts,
                CaptureFrameDirection.ClientToServer,
                "a",
                "b",
                new byte[] { 2 });

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void EqualsReturnsFalseWhenDirectionDiffers()
        {
            DateTimeOffset ts = DateTimeOffset.UtcNow;
            byte[] data = [0];
            var left = new CaptureFrame(ts, CaptureFrameDirection.ClientToServer, "a", "b", data);
            var right = new CaptureFrame(ts, CaptureFrameDirection.ServerToClient, "a", "b", data);

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void EqualsReturnsFalseWhenTimestampDiffers()
        {
            byte[] data = [0];
            var left = new CaptureFrame(
                DateTimeOffset.UnixEpoch,
                CaptureFrameDirection.Unknown,
                "a",
                "b",
                data);
            var right = new CaptureFrame(
                DateTimeOffset.UnixEpoch.AddTicks(1),
                CaptureFrameDirection.Unknown,
                "a",
                "b",
                data);

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void EqualsReturnsFalseWhenEndpointsDiffer()
        {
            DateTimeOffset ts = DateTimeOffset.UtcNow;
            byte[] data = [0];
            var clientDiff = new CaptureFrame(ts, CaptureFrameDirection.Unknown, "a1", "b", data);
            var serverDiff = new CaptureFrame(ts, CaptureFrameDirection.Unknown, "a", "b1", data);
            var baseline = new CaptureFrame(ts, CaptureFrameDirection.Unknown, "a", "b", data);

            Assert.That(baseline, Is.Not.EqualTo(clientDiff));
            Assert.That(baseline, Is.Not.EqualTo(serverDiff));
        }

        [Test]
        public void EqualsObjectReturnsFalseForUnrelatedType()
        {
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.Unknown,
                string.Empty,
                string.Empty,
                Array.Empty<byte>());
            bool equalsString = frame.Equals("not a frame");
            bool equalsNull = frame.Equals(null);

            Assert.That(equalsString, Is.False);
            Assert.That(equalsNull, Is.False);
        }
    }
}
