// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.Buffers;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies HTTP/3 control-stream frame ordering and GOAWAY state.
    /// </summary>
    [TestFixture]
    public sealed class Http3ControlStreamStateTests
    {
        /// <summary>
        /// Requires a valid SETTINGS frame before any other control-stream frame.
        /// </summary>
        [Test]
        public void ControlStream_NonSettingsFirstFrame_IsRejectedWithoutStateChange()
        {
            Http3ControlStreamState state = new(Http3EndpointRole.Client);

            Assert.That(
                state.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x00 })),
                Is.EqualTo(Http3ControlStreamFrameResult.FrameUnexpected));
            Assert.That(state.HasPeerSettings, Is.False);
            Assert.That(state.PeerGoAwayStreamId, Is.Null);
        }

        /// <summary>
        /// Accepts exactly one valid SETTINGS frame and rejects a duplicate.
        /// </summary>
        [Test]
        public void ControlStream_SettingsFirstAndUnique()
        {
            Http3ControlStreamState state = new(Http3EndpointRole.Client);
            ReadOnlySequence<byte> emptySettings = ReadOnlySequence<byte>.Empty;

            Assert.That(
                state.AcceptFrame(0x4, emptySettings),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            Assert.That(state.HasPeerSettings, Is.True);
            Assert.That(
                state.AcceptFrame(0x4, emptySettings),
                Is.EqualTo(Http3ControlStreamFrameResult.FrameUnexpected));
            Assert.That(state.HasPeerSettings, Is.True);
        }

        /// <summary>
        /// Rejects SETTINGS_ENABLE_CONNECT_PROTOCOL when sent by a client.
        /// </summary>
        [Test]
        public void ControlStream_ClientCannotAdvertiseExtendedConnect()
        {
            Http3ControlStreamState state = new(Http3EndpointRole.Server);
            byte[] clientSettings = [0x08, 0x01];

            Assert.That(
                state.AcceptFrame(0x4, new ReadOnlySequence<byte>(clientSettings)),
                Is.EqualTo(Http3ControlStreamFrameResult.MalformedSettings));
            Assert.That(state.HasPeerSettings, Is.False);
        }

        /// <summary>
        /// Rejects request-only frames on the control stream after SETTINGS.
        /// </summary>
        [TestCase(0x0UL)]
        [TestCase(0x1UL)]
        [TestCase(0x5UL)]
        public void ControlStream_RequestFrame_IsRejected(ulong frameType)
        {
            Http3ControlStreamState state = CreateInitializedState(Http3EndpointRole.Client);

            Assert.That(
                state.AcceptFrame(frameType, ReadOnlySequence<byte>.Empty),
                Is.EqualTo(Http3ControlStreamFrameResult.FrameUnexpected));
        }

        /// <summary>
        /// Ignores unknown extension frames without altering GOAWAY state.
        /// </summary>
        [Test]
        public void ControlStream_UnknownExtension_IsIgnored()
        {
            Http3ControlStreamState state = CreateInitializedState(Http3EndpointRole.Client);

            Assert.That(
                state.AcceptFrame(0x21, ReadOnlySequence<byte>.Empty),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            Assert.That(state.PeerGoAwayStreamId, Is.Null);
        }

        /// <summary>
        /// Requires server GOAWAY values to identify client-initiated bidirectional request streams.
        /// </summary>
        [Test]
        public void ControlStream_ServerGoAway_RequiresClientBidirectionalStreamId()
        {
            Http3ControlStreamState state = CreateInitializedState(Http3EndpointRole.Client);

            Assert.That(
                state.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x04 })),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            Assert.That(state.PeerGoAwayStreamId, Is.EqualTo(4));
            Assert.That(
                state.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x05 })),
                Is.EqualTo(Http3ControlStreamFrameResult.InvalidGoAway));
            Assert.That(
                state.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x01 })),
                Is.EqualTo(Http3ControlStreamFrameResult.InvalidGoAway));
            Assert.That(state.PeerGoAwayStreamId, Is.EqualTo(4));
        }

        /// <summary>
        /// Enforces non-increasing server GOAWAY identifiers and the client GOAWAY value of zero.
        /// </summary>
        [Test]
        public void ControlStream_GoAwayMonotonicityAndClientValue_AreEnforced()
        {
            Http3ControlStreamState clientSide = CreateInitializedState(Http3EndpointRole.Client);
            Assert.That(
                clientSide.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x08 })),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            Assert.That(
                clientSide.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x04 })),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            Assert.That(
                clientSide.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x08 })),
                Is.EqualTo(Http3ControlStreamFrameResult.InvalidGoAway));

            Http3ControlStreamState serverSide = CreateInitializedState(Http3EndpointRole.Server);
            Assert.That(
                serverSide.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x00 })),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            Assert.That(
                serverSide.AcceptFrame(0x7, new ReadOnlySequence<byte>(new byte[] { 0x04 })),
                Is.EqualTo(Http3ControlStreamFrameResult.InvalidGoAway));
        }

        /// <summary>
        /// Rejects empty, trailing, and truncated GOAWAY payloads without changing state.
        /// </summary>
        [TestCase(new byte[] { })]
        [TestCase(new byte[] { 0x00, 0x00 })]
        [TestCase(new byte[] { 0x40 })]
        public void ControlStream_MalformedGoAway_DoesNotChangeState(byte[] payload)
        {
            Http3ControlStreamState state = CreateInitializedState(Http3EndpointRole.Client);

            Assert.That(
                state.AcceptFrame(0x7, new ReadOnlySequence<byte>(payload)),
                Is.EqualTo(Http3ControlStreamFrameResult.InvalidGoAway));
            Assert.That(state.PeerGoAwayStreamId, Is.Null);
        }

        private static Http3ControlStreamState CreateInitializedState(Http3EndpointRole role)
        {
            Http3ControlStreamState state = new(role);
            Assert.That(
                state.AcceptFrame(0x4, ReadOnlySequence<byte>.Empty),
                Is.EqualTo(Http3ControlStreamFrameResult.Accepted));
            return state;
        }
    }
}
