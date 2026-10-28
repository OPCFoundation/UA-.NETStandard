// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies peer unidirectional stream identity, critical stream uniqueness, and bounded discard.
    /// </summary>
    [TestFixture]
    public sealed class Http3CriticalStreamRegistryTests
    {
        /// <summary>
        /// Registers one control, QPACK encoder, and QPACK decoder stream for each endpoint role.
        /// </summary>
        [TestCase(0, 3UL, 7UL, 11UL)]
        [TestCase(1, 2UL, 6UL, 10UL)]
        public void CriticalStreams_OneOfEach_CompletesRegistry(
            int localRoleValue,
            ulong controlStreamId,
            ulong encoderStreamId,
            ulong decoderStreamId)
        {
            Http3EndpointRole localRole = (Http3EndpointRole)localRoleValue;
            Http3CriticalStreamRegistry registry = new(localRole);

            Assert.That(
                registry.Register(controlStreamId, 0),
                Is.EqualTo(Http3CriticalStreamResult.CriticalStreamRegistered));
            Assert.That(
                registry.Register(encoderStreamId, 2),
                Is.EqualTo(Http3CriticalStreamResult.CriticalStreamRegistered));
            Assert.That(registry.HasAllCriticalStreams, Is.False);
            Assert.That(
                registry.Register(decoderStreamId, 3),
                Is.EqualTo(Http3CriticalStreamResult.CriticalStreamRegistered));
            Assert.That(registry.HasAllCriticalStreams, Is.True);
        }

        /// <summary>
        /// Rejects duplicate critical types without replacing the original registered stream.
        /// </summary>
        [Test]
        public void CriticalStreams_DuplicateType_IsRejected()
        {
            Http3CriticalStreamRegistry registry = new(Http3EndpointRole.Client);

            Assert.That(
                registry.Register(3, 0),
                Is.EqualTo(Http3CriticalStreamResult.CriticalStreamRegistered));
            Assert.That(
                registry.Register(7, 0),
                Is.EqualTo(Http3CriticalStreamResult.DuplicateCriticalStream));
            Assert.That(
                registry.Close(3),
                Is.EqualTo(Http3CriticalStreamResult.ClosedCriticalStream));
        }

        /// <summary>
        /// Fails when any registered critical stream closes.
        /// </summary>
        [TestCase(0UL, 3UL)]
        [TestCase(2UL, 7UL)]
        [TestCase(3UL, 11UL)]
        public void CriticalStreams_Closure_IsFatal(ulong streamType, ulong streamId)
        {
            Http3CriticalStreamRegistry registry = new(Http3EndpointRole.Client);

            Assert.That(
                registry.Register(streamId, streamType),
                Is.EqualTo(Http3CriticalStreamResult.CriticalStreamRegistered));
            Assert.That(
                registry.Close(streamId),
                Is.EqualTo(Http3CriticalStreamResult.ClosedCriticalStream));
        }

        /// <summary>
        /// Rejects stream IDs not initiated by the peer as unidirectional streams.
        /// </summary>
        [TestCase(0, 2UL)]
        [TestCase(1, 3UL)]
        public void PeerStream_InvalidInitiatorOrDirection_IsRejected(
            int localRoleValue,
            ulong streamId)
        {
            Http3EndpointRole localRole = (Http3EndpointRole)localRoleValue;
            Http3CriticalStreamRegistry registry = new(localRole);

            Assert.That(
                registry.Register(streamId, 0),
                Is.EqualTo(Http3CriticalStreamResult.InvalidStreamId));
            Assert.That(registry.HasControlStream, Is.False);
        }

        /// <summary>
        /// Rejects server push because the managed profile does not negotiate push.
        /// </summary>
        [Test]
        public void PeerStream_PushStream_IsRejected()
        {
            Http3CriticalStreamRegistry registry = new(Http3EndpointRole.Client);

            Assert.That(
                registry.Register(3, 1),
                Is.EqualTo(Http3CriticalStreamResult.UnsupportedPushStream));
        }

        /// <summary>
        /// Bounds unknown extension-stream discard without retaining per-stream state.
        /// </summary>
        [Test]
        public void PeerStream_UnknownExtensions_AreDiscardedWithinLimit()
        {
            Http3CriticalStreamRegistry registry = new(Http3EndpointRole.Client);

            for (ulong index = 0; index < 64; index++)
            {
                Assert.That(
                    registry.Register(3 + 4 * index, 0x20),
                    Is.EqualTo(Http3CriticalStreamResult.IgnoredExtensionStream),
                    $"index={index}");
            }

            Assert.That(
                registry.Register(3 + 4 * 64, 0x20),
                Is.EqualTo(Http3CriticalStreamResult.IgnoredStreamLimitExceeded));
            Assert.That(registry.HasAllCriticalStreams, Is.False);
        }

        /// <summary>
        /// Rejects an undefined endpoint role when constructing the registry.
        /// </summary>
        [Test]
        public void Registry_InvalidRole_Throws()
        {
            Assert.That(
                () => new Http3CriticalStreamRegistry((Http3EndpointRole)2),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
