// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Validates Ethernet II framing (EtherType 0xB62C, optional 802.1Q
    /// tagging, 60-octet minimum padding) produced by
    /// <see cref="EthernetFrameCodec"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.3", Summary = "Ethernet frame encoding")]
    public sealed class EthernetFrameCodecTests
    {
        private static readonly byte[] s_dst = [0x01, 0x00, 0x5E, 0x00, 0x00, 0x01];
        private static readonly byte[] s_src = [0x02, 0x00, 0x00, 0x00, 0x00, 0x01];

        [Test]
        public void GetRequiredLengthPadsToMinimum()
        {
            Assert.Multiple(() =>
            {
                Assert.That(EthernetFrameCodec.GetRequiredLength(4, vlanTagged: false), Is.EqualTo(60));
                Assert.That(EthernetFrameCodec.GetRequiredLength(4, vlanTagged: true), Is.EqualTo(60));
                Assert.That(EthernetFrameCodec.GetRequiredLength(100, vlanTagged: false), Is.EqualTo(114));
                Assert.That(EthernetFrameCodec.GetRequiredLength(100, vlanTagged: true), Is.EqualTo(118));
            });
        }

        [Test]
        public void BuildAndParseUntaggedRoundTrip()
        {
            byte[] payload = MakePayload(50);
            byte[] buffer = new byte[EthernetFrameCodec.GetRequiredLength(payload.Length, false)];

            int written = EthernetFrameCodec.Build(buffer, s_dst, s_src, null, null, payload);
            Assert.That(written, Is.EqualTo(64));

            Assert.That(
                EthernetFrameCodec.TryParse(buffer.AsMemory(0, written), out EthernetFrame frame),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(frame.Payload.ToArray(), Is.EqualTo(payload));
                Assert.That(frame.VlanId, Is.Null);
                Assert.That(frame.Priority, Is.Null);
                Assert.That(frame.DestinationAddress.GetAddressBytes(), Is.EqualTo(s_dst));
                Assert.That(frame.SourceAddress.GetAddressBytes(), Is.EqualTo(s_src));
            });
        }

        [Test]
        public void BuildAndParseTaggedRoundTrip()
        {
            byte[] payload = MakePayload(50);
            byte[] buffer = new byte[EthernetFrameCodec.GetRequiredLength(payload.Length, true)];

            int written = EthernetFrameCodec.Build(buffer, s_dst, s_src, 5, 6, payload);

            Assert.That(
                EthernetFrameCodec.TryParse(buffer.AsMemory(0, written), out EthernetFrame frame),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(frame.VlanId, Is.EqualTo((ushort)5));
                Assert.That(frame.Priority, Is.EqualTo((byte)6));
                Assert.That(frame.Payload.ToArray(), Is.EqualTo(payload));
            });
        }

        [Test]
        public void BuildPadsSmallPayloadToMinimum()
        {
            byte[] payload = MakePayload(4);
            byte[] buffer = new byte[EthernetFrameCodec.GetRequiredLength(payload.Length, false)];

            int written = EthernetFrameCodec.Build(buffer, s_dst, s_src, null, null, payload);

            Assert.That(written, Is.EqualTo(EthernetFrameCodec.MinFrameLength));
        }

        [Test]
        public void TryParseRejectsForeignEtherType()
        {
            byte[] frame = new byte[60];
            s_dst.CopyTo(frame, 0);
            s_src.CopyTo(frame, 6);
            // IPv4 EtherType, not OPC UA.
            frame[12] = 0x08;
            frame[13] = 0x00;

            Assert.That(
                EthernetFrameCodec.TryParse(frame, out int offset, out _, out _),
                Is.False);
            Assert.That(offset, Is.Zero);
        }

        [Test]
        public void TryParseRejectsTooShortFrame()
        {
            Assert.That(
                EthernetFrameCodec.TryParse(new byte[10], out _, out _, out _),
                Is.False);
        }

        [Test]
        public void BuildRejectsWrongMacLength()
        {
            byte[] buffer = new byte[64];
            Assert.That(
                () => EthernetFrameCodec.Build(buffer, new byte[4], s_src, null, null, MakePayload(10)),
                Throws.ArgumentException);
        }

        [Test]
        public void BuildPriorityOnlyEmitsTagWithVlanZero()
        {
            byte[] payload = MakePayload(50);
            byte[] buffer = new byte[EthernetFrameCodec.GetRequiredLength(payload.Length, true)];

            int written = EthernetFrameCodec.Build(buffer, s_dst, s_src, null, 3, payload);

            Assert.That(
                EthernetFrameCodec.TryParse(buffer.AsMemory(0, written), out EthernetFrame frame),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(frame.VlanId, Is.Zero);
                Assert.That(frame.Priority, Is.EqualTo((byte)3));
            });
        }

        [Test]
        public void GetRequiredLengthRejectsOverflowPayload()
        {
            Assert.That(
                () => EthernetFrameCodec.GetRequiredLength(int.MaxValue, vlanTagged: true),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static byte[] MakePayload(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(i + 1);
            }
            return payload;
        }
    }
}
