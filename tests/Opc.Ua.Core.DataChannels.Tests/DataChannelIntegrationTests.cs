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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Drives the data channel Service Set end to end over a live
    /// <c>opc.tcp</c> connection: a real Client Session calls
    /// <c>OpenDataChannel</c>, <c>ModifyDataChannel</c> and
    /// <c>CloseDataChannel</c> on a real <see cref="StandardServer"/>, and
    /// payload crosses the same SecureChannel inline alongside the Service
    /// traffic.
    /// </summary>
    /// <remarks>
    /// The unit suite exercises the engine and the Service handler in
    /// isolation. This fixture is the only place the Server's Service
    /// overrides, the authorizer, the auditor and the inline transport are
    /// all driven by an actual request arriving off a socket, which is what
    /// the Part 6 errata §5 framing has to survive.
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("DataChannels")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class DataChannelIntegrationTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            m_source = new IntegrationSource(SourceNodeId);
            ReferenceServer.DataChannelSources.Register(m_source);
            ReferenceServer.DataChannelAuthorizer = new AllowConfiguredSourceAuthorizer(SourceNodeId);
            ReferenceServer.DataChannelCapabilities = new DataChannelServerCapabilities
            {
                MaxDataChannels = 4,
                MaxFrameSize = 8 * 1024,
                MaxCreditPerChannel = 256 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaTcpTransport]
            };

            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            ReferenceServer.DataChannelSources.Unregister(SourceNodeId);
            ReferenceServer.DataChannelAuthorizer = null!;
            return base.TearDownAsync();
        }

        /// <summary>
        /// The whole round trip: open a channel on a live Session, carry
        /// payload from the Server's source to the Client's sink on the same
        /// SecureChannel the Services ran on, then close it.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task OpenCarriesPayloadInlineAndClosesAsync(CancellationToken ct)
        {
            // The Client shall be ready to receive frames before it asks for a
            // channel: the Server may begin sending as soon as the response is
            // dispatched (§7.4), and a frame arriving at a Client that has not
            // enabled inline framing is an unknown MessageType that closes the
            // whole SecureChannel.
            DataChannelManager clientManager = EnableClientDataChannels();

            OpenDataChannelResponse opened = await Session
                .OpenDataChannelAsync(null, SourceNodeId, 0, 0, Parameters(), ct)
                .ConfigureAwait(false);

            Assert.That(opened.ChannelId, Is.Not.Zero);
            Assert.That(
                opened.RevisedParameters.Direction,
                Is.EqualTo(DataChannelDirection.SourceToSink));

            DataChannel sink = clientManager.Register(
                opened.ChannelId,
                SourceNodeId,
                DataChannelSettings.FromParameters(opened.RevisedParameters),
                isSource: false,
                opened.RevisedTransportChannelId);
            clientManager.MarkOpen(opened.ChannelId);

            DataChannel source = await m_source!.WaitForChannelAsync(ct).ConfigureAwait(false);

            byte[] payload = [0x0A, 0x0B, 0x0C, 0x0D];
            source.Write(
                payload,
                DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd);

            using DataChannelMessage? delivered = await ReadWithTimeoutAsync(sink, ct)
                .ConfigureAwait(false);

            Assert.That(delivered, Is.Not.Null, "The payload never crossed the SecureChannel.");
            Assert.That(delivered!.Payload.ToArray(), Is.EqualTo(payload));

            CloseDataChannelResponse closed = await Session
                .CloseDataChannelAsync(null, opened.ChannelId, StatusCodes.Good, false, ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(closed.ResponseHeader.ServiceResult), Is.True);
            Assert.That(
                await WaitForAsync(
                        () => source.State is DataChannelState.Closed or DataChannelState.Closing,
                        ct)
                    .ConfigureAwait(false),
                Is.True,
                "CloseDataChannel left the Server side channel open.");
        }

        /// <summary>
        /// Part 4 errata §5.2: a Modify revises the parameters the Server is
        /// willing to accept and the revision comes back on the response.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ModifyRevisesTheChannelOnALiveSessionAsync(CancellationToken ct)
        {
            DataChannelManager clientManager = EnableClientDataChannels();

            OpenDataChannelResponse opened = await Session
                .OpenDataChannelAsync(null, SourceNodeId, 0, 0, Parameters(), ct)
                .ConfigureAwait(false);

            clientManager.Register(
                opened.ChannelId,
                SourceNodeId,
                DataChannelSettings.FromParameters(opened.RevisedParameters),
                isSource: false,
                opened.RevisedTransportChannelId);
            clientManager.MarkOpen(opened.ChannelId);

            ModifyDataChannelResponse modified = await Session
                .ModifyDataChannelAsync(
                    null,
                    opened.ChannelId,
                    new DataChannelParametersDataType
                    {
                        Direction = opened.RevisedParameters.Direction,
                        DeliveryMode = opened.RevisedParameters.DeliveryMode,
                        ContentType = opened.RevisedParameters.ContentType,
                        MaxFrameSize = 1024,
                        InitialCredit = opened.RevisedParameters.InitialCredit,
                        Priority = 5
                    },
                    ct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(modified.ResponseHeader.ServiceResult), Is.True);
                Assert.That(modified.RevisedParameters.MaxFrameSize, Is.EqualTo(1024u));
                Assert.That(modified.RevisedParameters.Priority, Is.EqualTo(5));
            });

            await Session
                .CloseDataChannelAsync(null, opened.ChannelId, StatusCodes.Good, true, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Part 4 errata §7.2: a channel is scoped to the SecureChannel and
        /// the Session that authorized it, so a ChannelId that names nothing
        /// this Session owns is refused rather than acted on.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public void CloseOfAChannelThisSessionDoesNotOwnIsRefused()
        {
            var exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session
                    .CloseDataChannelAsync(
                        null,
                        UnownedChannelId,
                        StatusCodes.Good,
                        false,
                        CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDataChannelIdInvalid));
        }

        /// <summary>
        /// A source the Server does not host cannot be opened, and the
        /// refusal names the source rather than leaking whether one exists.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public void OpenOnAnUnknownSourceIsRefused()
        {
            var exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session
                    .OpenDataChannelAsync(
                        null,
                        new NodeId(987_654u),
                        0,
                        0,
                        Parameters(),
                        CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.AnyOf(
                    (uint)StatusCodes.BadDataChannelNotSupported,
                    (uint)StatusCodes.BadUserAccessDenied));
        }

        private DataChannelManager EnableClientDataChannels()
        {
            var channel = Session.TransportChannel as UaSCUaBinaryTransportChannel;
            Assert.That(
                channel?.SecureChannel,
                Is.Not.Null,
                "The client transport is not a UASC binary channel, so inline framing cannot be used.");

            return channel!.SecureChannel!.EnableDataChannels(
                isServer: false,
                Telemetry,
                maxDataChannels: 4,
                maxCreditPerChannel: 256 * 1024);
        }

        private static DataChannelParametersDataType Parameters()
        {
            return new DataChannelParametersDataType
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                ContentType = "application/octet-stream",
                MaxFrameSize = 4096,
                InitialCredit = 65_536,
                Priority = 1
            };
        }

        private static async Task<DataChannelMessage?> ReadWithTimeoutAsync(
            DataChannel channel,
            CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                return await channel.ReadAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> predicate, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                if (predicate())
                {
                    return true;
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// A source that hands the channel it is given to the test and
        /// otherwise does nothing, so the test owns what is written on it.
        /// </summary>
        private sealed class IntegrationSource(NodeId nodeId) : IDataChannelSource
        {
            public NodeId NodeId { get; } = nodeId;

            public DataChannelSourceCapabilities Capabilities { get; } = new()
            {
                Direction = DataChannelDirection.SourceToSink,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                ContentType = "application/octet-stream",
                MaxFrameSize = 8 * 1024,
                Priority = 1
            };

            public int ActiveChannelCount => m_active;

            public void OnChannelOpened(DataChannel channel)
            {
                Interlocked.Increment(ref m_active);
                m_opened.TrySetResult(channel);
            }

            public void OnChannelClosed(DataChannel channel, StatusCode reason)
            {
                Interlocked.Decrement(ref m_active);
            }

            public async Task<DataChannel> WaitForChannelAsync(CancellationToken ct)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                return await m_opened.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }

            private readonly TaskCompletionSource<DataChannel> m_opened =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_active;
        }

        /// <summary>
        /// Grants the one source this fixture publishes. The default
        /// authorizer resolves the source in the AddressSpace and applies its
        /// RolePermissions; a source kept outside the AddressSpace, as this
        /// one is, has no such metadata and would be denied.
        /// </summary>
        private sealed class AllowConfiguredSourceAuthorizer(NodeId sourceNodeId)
            : IDataChannelAuthorizer
        {
            public ValueTask<bool> IsAuthorizedAsync(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return new ValueTask<bool>(
                    !context.SessionId.IsNull && sourceNodeId == m_sourceNodeId);
            }

            private readonly NodeId m_sourceNodeId = sourceNodeId;
        }

        private static readonly NodeId SourceNodeId = new(424_242u);

        private const uint UnownedChannelId = 9_999;

        private IntegrationSource? m_source;
    }
}
