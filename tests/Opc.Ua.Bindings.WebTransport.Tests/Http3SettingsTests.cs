// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies bounded HTTP/3 SETTINGS parsing and capsule-profile negotiation values.
    /// </summary>
    [TestFixture]
    public sealed class Http3SettingsTests
    {
        /// <summary>
        /// Advertises Extended CONNECT while disabling QPACK dynamic state and H3 datagrams.
        /// </summary>
        [Test]
        public void LocalSettings_UseCapsuleProfileValues()
        {
            Span<byte> payload = stackalloc byte[6];

            Assert.That(Http3Settings.TryWriteLocalPayload(
                Http3EndpointRole.Server, payload, out int bytesWritten), Is.True);
            Assert.That(payload[..bytesWritten].ToArray(), Is.EqualTo(new byte[] { 1, 0, 7, 0, 8, 1 }));

            Assert.That(
                Http3Settings.TryParse(new ReadOnlySequence<byte>(payload[..bytesWritten].ToArray()), out var settings),
                Is.EqualTo(Http3SettingsReadStatus.Success));
            Assert.That(settings, Is.EqualTo(new Http3Settings(0, 0, true, false)));
        }

        /// <summary>
        /// Leaves a too-small destination untouched instead of emitting a partial SETTINGS payload.
        /// </summary>
        [Test]
        public void LocalSettings_InsufficientCapacity_DoesNotWrite()
        {
            Span<byte> payload = stackalloc byte[5];
            payload.Fill(0xaa);

            Assert.That(Http3Settings.TryWriteLocalPayload(
                Http3EndpointRole.Server, payload, out int bytesWritten), Is.False);
            Assert.That(bytesWritten, Is.Zero);
            Assert.That(payload.ToArray(), Is.EqualTo(new byte[] { 0xaa, 0xaa, 0xaa, 0xaa, 0xaa }));
        }

        /// <summary>
        /// Accepts unknown settings while retaining only recognized profile values.
        /// </summary>
        [Test]
        public void Settings_UnknownIdentifier_IsIgnored()
        {
            byte[] payload = [0x21, 0x2a, 0x08, 0x01, 0x33, 0x00];

            Assert.That(
                Http3Settings.TryParse(new ReadOnlySequence<byte>(payload), out var settings),
                Is.EqualTo(Http3SettingsReadStatus.Success));
            Assert.That(settings.PeerEnablesConnectProtocol, Is.True);
            Assert.That(settings.PeerAdvertisesH3Datagrams, Is.False);
        }

        /// <summary>
        /// Does not emit the server-only Extended CONNECT setting from a client endpoint.
        /// </summary>
        [Test]
        public void ClientSettings_OmitEnableConnectProtocol()
        {
            Span<byte> payload = stackalloc byte[4];

            Assert.That(Http3Settings.TryWriteLocalPayload(
                Http3EndpointRole.Client, payload, out int bytesWritten), Is.True);
            Assert.That(payload[..bytesWritten].ToArray(), Is.EqualTo(new byte[] { 1, 0, 7, 0 }));
        }

        /// <summary>
        /// Rejects truncated, duplicated, reserved, and invalid boolean settings.
        /// </summary>
        [TestCase(new byte[] { 0x01 })]
        [TestCase(new byte[] { 0x08, 0x01, 0x08, 0x00 })]
        [TestCase(new byte[] { 0x02, 0x00 })]
        [TestCase(new byte[] { 0x05, 0x00 })]
        [TestCase(new byte[] { 0x08, 0x02 })]
        [TestCase(new byte[] { 0x33, 0x02 })]
        public void Settings_InvalidPayload_IsRejected(byte[] payload)
        {
            Assert.That(
                Http3Settings.TryParse(new ReadOnlySequence<byte>(payload), out var settings),
                Is.EqualTo(Http3SettingsReadStatus.Malformed));
            Assert.That(settings, Is.Default);
        }

        /// <summary>
        /// Stops after the configured setting-count limit rather than scanning unbounded entries.
        /// </summary>
        [Test]
        public void Settings_TooManyEntries_IsRejected()
        {
            byte[] payload = new byte[65 * 3];
            for (int index = 0; index < 65; index++)
            {
                payload[index * 3] = 0x41;
                payload[index * 3 + 1] = (byte)index;
                payload[index * 3 + 2] = 0;
            }

            Assert.That(
                Http3Settings.TryParse(new ReadOnlySequence<byte>(payload), out _),
                Is.EqualTo(Http3SettingsReadStatus.TooManySettings));
        }
    }
}
