// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.Pcap.Tests.Frame
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
