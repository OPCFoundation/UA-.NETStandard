/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: test code transfers disposable ownership to test methods and disposes in cleanup paths.
#pragma warning disable CA2000

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using IChannel = Opc.Ua.Client.Tests.Stack.Client.ClientChannelManagerManagedTests.IChannel;

namespace Opc.Ua.Client.Tests.Channels
{
    /// <summary>
    /// Tests the managed client channel cap enforced by
    /// <see cref="ClientChannelManager"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ChannelManagerMaxChannelsTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;

        [Test]
        public async Task GetAsyncRespectsMaxChannelsCap()
        {
            var options = new ChannelManagerOptions
            {
                MaxChannels = 3
            };
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut(options);
            var channels = new List<IManagedTransportChannel>();
            try
            {
                for (int ii = 0; ii < options.MaxChannels; ii++)
                {
                    ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert, ii);
                    channels.Add(await sut.GetAsync(new TestParticipant($"p{ii}", endpoint), default)
                        .ConfigureAwait(false));
                }

                ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sut.GetAsync(
                            new TestParticipant("over-cap", GetTestEndpoint(serverCert, options.MaxChannels)),
                            default)
                        .AsTask()
                        .ConfigureAwait(false));

                Assert.That(exception, Is.Not.Null);
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            }
            finally
            {
                DisposeChannels(channels);
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task GetAsyncReturnsExistingChannelAtCapBoundary()
        {
            var options = new ChannelManagerOptions
            {
                MaxChannels = 3
            };
            (ClientChannelManager sut, Certificate serverCert, Mock<IChannel> channelMock) = CreateMockedSut(options);
            var channels = new List<IManagedTransportChannel>();
            try
            {
                ConfiguredEndpoint firstEndpoint = GetTestEndpoint(serverCert, 0);
                IManagedTransportChannel firstChannel = await sut.GetAsync(
                        new TestParticipant("first", firstEndpoint),
                        default)
                    .ConfigureAwait(false);
                channels.Add(firstChannel);

                for (int ii = 1; ii < options.MaxChannels; ii++)
                {
                    ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert, ii);
                    channels.Add(await sut.GetAsync(new TestParticipant($"p{ii}", endpoint), default)
                        .ConfigureAwait(false));
                }

                IManagedTransportChannel cachedChannel = await sut.GetAsync(
                        new TestParticipant("cached", firstEndpoint),
                        default)
                    .ConfigureAwait(false);
                channels.Add(cachedChannel);

                Assert.That(cachedChannel.Key, Is.EqualTo(firstChannel.Key));
                channelMock.Verify(c => c.OpenAsync(
                        It.IsAny<Uri>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()),
                    Times.Exactly(options.MaxChannels));
            }
            finally
            {
                DisposeChannels(channels);
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public async Task MaxChannelsZeroDisablesCap()
        {
            var options = new ChannelManagerOptions
            {
                MaxChannels = 0
            };
            (ClientChannelManager sut, Certificate serverCert, _) = CreateMockedSut(options);
            var channels = new List<IManagedTransportChannel>();
            try
            {
                for (int ii = 0; ii < 10; ii++)
                {
                    ConfiguredEndpoint endpoint = GetTestEndpoint(serverCert, ii);
                    channels.Add(await sut.GetAsync(new TestParticipant($"p{ii}", endpoint), default)
                        .ConfigureAwait(false));
                }

                Assert.That(channels, Has.Count.EqualTo(10));
            }
            finally
            {
                DisposeChannels(channels);
                await sut.DisposeAsync().ConfigureAwait(false);
                serverCert.Dispose();
            }
        }

        [Test]
        public void MaxChannelsDefaultIs256()
        {
            Assert.That(new ChannelManagerOptions().MaxChannels, Is.EqualTo(256));
        }

        private static (ClientChannelManager Sut, Certificate ServerCert, Mock<IChannel> ChannelMock)
            CreateMockedSut(ChannelManagerOptions options)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Certificate serverCert = s_factory.CreateCertificate("CN=server").CreateForRSA();

            var channelMock = new Mock<IChannel>();
            channelMock.Setup(c => c.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            channelMock.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            channelMock.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.None);

            var bindings = new Mock<ITransportChannelBindings>();
            bindings.Setup(b => b.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                .Returns(channelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(
                configuration,
                telemetry,
                bindings.Object,
                reconnectPolicy: null,
                timeProvider: null,
                options: options);

            return (sut, serverCert, channelMock);
        }

        private static ConfiguredEndpoint GetTestEndpoint(Certificate serverCert, int index)
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration
                {
                    OperationTimeout = 6000
                }
            };
            endpoint.Description.EndpointUrl = $"opc.tcp://localhost:{4840 + index}";
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.Description.ServerCertificate = serverCert.RawData.ToByteString();
            return endpoint;
        }

        private static void DisposeChannels(IEnumerable<IManagedTransportChannel> channels)
        {
            foreach (IManagedTransportChannel channel in channels)
            {
                channel.Dispose();
            }
        }

        private sealed class TestParticipant : IReconnectParticipant
        {
            public TestParticipant(string id, ConfiguredEndpoint endpoint)
            {
                Id = id;
                Endpoint = endpoint;
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                _ = channel;
                _ = reconnectAttempt;
                _ = ct;

                return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
            }
        }
    }
}
