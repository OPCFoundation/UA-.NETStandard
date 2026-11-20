/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;

namespace Opc.Ua.Pcap.Tests.Bindings
{
    /// <summary>
    /// Deterministic unit tests for the server-side capture binding
    /// (<see cref="PcapTransportListenerBinding"/> and its inner listener
    /// decorator) that verify the capture seam is injected into the
    /// <see cref="TransportListenerSettings"/> and that every other member is
    /// forwarded to the wrapped listener.
    /// </summary>
    [TestFixture]
    public sealed class PcapTransportListenerBindingTests
    {
        [Test]
        public void ConstructorRejectsNulls()
        {
            var registry = new ChannelCaptureRegistry();
            Assert.That(
                () => new PcapTransportListenerBinding(null!, registry),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new PcapTransportListenerBinding(new FakeListenerFactory(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void UriSchemeReflectsInnerFactory()
        {
            var binding = new PcapTransportListenerBinding(
                new FakeListenerFactory(),
                new ChannelCaptureRegistry());

            Assert.That(binding.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public void CreateReturnsDecoratorForwardingIdentity()
        {
            var inner = new FakeListenerFactory();
            var binding = new PcapTransportListenerBinding(inner, new ChannelCaptureRegistry());
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();

            ITransportListener listener = binding.Create(telemetry);

            Assert.That(listener, Is.Not.SameAs(inner.Listener));
            Assert.That(listener.ListenerId, Is.EqualTo(inner.Listener.ListenerId));
            Assert.That(listener.UriScheme, Is.EqualTo(inner.Listener.UriScheme));
        }

        [Test]
        public async Task OpenAsyncInstallsCaptureSeamAndDelegatesAsync()
        {
            var registry = new ChannelCaptureRegistry();
            var inner = new FakeListenerFactory();
            var binding = new PcapTransportListenerBinding(inner, registry);
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();
            ITransportListener listener = binding.Create(telemetry);

            var settings = new TransportListenerSettings();
            Assert.That(settings.AcceptedTransportDecorator, Is.Null);
            Assert.That(settings.OnAcceptedChannel, Is.Null);

            await listener.OpenAsync(
                new Uri("opc.tcp://localhost:0"),
                settings,
                Mock.Of<ITransportListenerCallback>()).ConfigureAwait(false);

            Assert.That(inner.Listener.OpenCount, Is.EqualTo(1));
            Assert.That(inner.Listener.OpenedWith, Is.SameAs(settings));
            Assert.That(settings.AcceptedTransportDecorator, Is.Not.Null);
            Assert.That(settings.OnAcceptedChannel, Is.Not.Null);

            IUaSCByteTransport wrapped = settings.AcceptedTransportDecorator!(new NoopTransport());
            try
            {
                Assert.That(wrapped, Is.InstanceOf<CapturingByteTransport>());
            }
            finally
            {
                (wrapped as IDisposable)?.Dispose();
            }
        }

        [Test]
        public async Task PassThroughMembersDelegateToInnerAsync()
        {
            var inner = new FakeListenerFactory();
            var binding = new PcapTransportListenerBinding(inner, new ChannelCaptureRegistry());
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();
            ITransportListener listener = binding.Create(telemetry);

            listener.UpdateChannelLastActiveTime("global-1");
            await listener.CloseAsync().ConfigureAwait(false);
            await listener.DisposeAsync().ConfigureAwait(false);

            Assert.That(inner.Listener.LastActiveId, Is.EqualTo("global-1"));
            Assert.That(inner.Listener.CloseCount, Is.EqualTo(1));
            Assert.That(inner.Listener.DisposeCount, Is.EqualTo(1));
        }

        private sealed class FakeListenerFactory : ITransportListenerFactory
        {
            public FakeListener Listener { get; } = new();

            public string UriScheme => Utils.UriSchemeOpcTcp;

            public ITransportListener Create(ITelemetryContext telemetry)
            {
                return Listener;
            }

            public ValueTask<List<EndpointDescription>> CreateServiceHostAsync(
                ServerBase serverBase,
                IDictionary<string, ServiceHost> hosts,
                ApplicationConfiguration configuration,
                ArrayOf<string> baseAddresses,
                ApplicationDescription serverDescription,
                ArrayOf<ServerSecurityPolicy> securityPolicies,
                ICertificateRegistry serverCertificates,
                ICertificateValidatorEx clientCertificateValidator,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }

#pragma warning disable CS0067 // events are part of the ITransportListener surface but unused by the tests
        private sealed class FakeListener : ITransportListener
        {
            public string ListenerId => "fake-listener";
            public string UriScheme => Utils.UriSchemeOpcTcp;
            public int OpenCount { get; private set; }
            public int CloseCount { get; private set; }
            public int DisposeCount { get; private set; }
            public string? LastActiveId { get; private set; }
            public TransportListenerSettings? OpenedWith { get; private set; }

            public event ConnectionWaitingHandlerAsync? ConnectionWaiting;
            public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

            public ValueTask OpenAsync(
                Uri baseAddress,
                TransportListenerSettings settings,
                ITransportListenerCallback callback,
                CancellationToken ct = default)
            {
                OpenCount++;
                OpenedWith = settings;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                CloseCount++;
                return default;
            }

            public void CertificateUpdate(
                ICertificateValidatorEx validator,
                ICertificateRegistry serverCertificates)
            {
            }

            public void CreateReverseConnection(Uri url, int timeout)
            {
            }

            public void UpdateChannelLastActiveTime(string globalChannelId)
            {
                LastActiveId = globalChannelId;
            }

            public ValueTask DisposeAsync()
            {
                DisposeCount++;
                return default;
            }
        }
#pragma warning restore CS0067

        private sealed class NoopTransport : IUaSCByteTransport, IDisposable
        {
            public string Implementation => "UA-NOOP";
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return new(new ArraySegment<byte>([]));
            }

            public void Close()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
