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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class MatchedReverseTransportTests
{
    [TestCase("urn:expected-server", "opc.tcp://SERVER.example.test:4840/Factory", true)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4840/Factory#fragment", false)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4840/Factory?extra=value", false)]
    [TestCase("urn:expected-server", "opc.tcp://operator@server.example.test:4840/Factory", false)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4840/factory", false)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4842/Factory", false)]
    [TestCase("urn:expected-server", "wss://server.example.test:4840/Factory", false)]
    [TestCase("urn:Expected-server", "opc.tcp://server.example.test:4840/Factory", false)]
    [TestCase("urn:expected-server", "/Factory", false)]
    public async Task PeerFilterRequiresBothExactIdentityAndAnUnambiguousEndpoint(
        string serverUri,
        string url,
        bool accepted)
    {
        var context = new ListenerContext();
        await using (context.ConfigureAwait(false))
        {
            int deliveries = 0;
            context.Listener.ConnectionWaiting += (_, args) =>
            {
                deliveries++;
                args.Accepted = true;
                return Task.CompletedTask;
            };
            var hello = new WaitingConnection(serverUri, new Uri(url, UriKind.RelativeOrAbsolute)) { Accepted = true };

            await context.DeliverAsync(hello).ConfigureAwait(false);

            Assert.That(hello.Accepted, Is.EqualTo(accepted));
            Assert.That(deliveries, Is.EqualTo(accepted ? 1 : 0));
        }
    }

    [Test]
    public async Task MatchingPeerWithoutAConsumerDoesNotRetainAnEarlierAcceptance()
    {
        var context = new ListenerContext();
        await using (context.ConfigureAwait(false))
        {
            var hello = new WaitingConnection("urn:expected-server", new Uri(Profile().EndpointUrl))
            {
                Accepted = true
            };

            await context.DeliverAsync(hello).ConfigureAwait(false);

            Assert.That(hello.Accepted, Is.False);
        }
    }

    [Test]
    public async Task HandlerFailureRejectsThePeerEvenIfTheHandlerAlreadyMarkedItAccepted()
    {
        var context = new ListenerContext();
        await using (context.ConfigureAwait(false))
        {
            var failure = new InvalidOperationException("Controlled consumer failure.");
            context.Listener.ConnectionWaiting += (_, args) =>
            {
                args.Accepted = true;
                return Task.FromException(failure);
            };
            var hello = new WaitingConnection("urn:expected-server", new Uri(Profile().EndpointUrl));

            await Assert.ThatAsync(() => context.DeliverAsync(hello), Throws.Exception.SameAs(failure))
                .ConfigureAwait(false);

            Assert.That(hello.Accepted, Is.False);
        }
    }

    [TestCase("address")]
    [TestCase("direction")]
    [TestCase("certificate")]
    [TestCase("validator")]
    public async Task WssOpenRejectsMissingTlsOrDifferentExplicitListenerBeforeTheBindingRuns(string missing)
    {
        ReverseConnectionProfile profile = Profile() with
        {
            ListenerUrl = "wss://localhost:4841/client",
            EndpointUrl = "wss://server.example.test:4840/Factory",
            TlsConfigurationId = "listener-tls"
        };
        var inner = new Mock<ITransportListener>(MockBehavior.Strict);
        inner.SetupAdd(value => value.ConnectionWaiting += It.IsAny<ConnectionWaitingHandlerAsync>());
        inner.SetupRemove(value => value.ConnectionWaiting -= It.IsAny<ConnectionWaitingHandlerAsync>());
        inner.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var listener = new MatchedReverseListener(inner.Object, profile);
        await using (listener.ConfigureAwait(false))
        {
            var settings = new TransportListenerSettings
            {
                ReverseConnectListener = missing != "direction",
                ServerCertificates = missing == "certificate" ? null : new Mock<ICertificateRegistry>().Object,
                CertificateValidator = missing == "validator" ? null : new Mock<ICertificateValidatorEx>().Object
            };
            var address = new Uri(missing == "address" ? "wss://localhost:4842/client" : profile.ListenerUrl);

            await Assert.ThatAsync(
                async () => await listener.OpenAsync(address, settings, null!).ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);

            inner.Verify(value => value.OpenAsync(It.IsAny<Uri>(), It.IsAny<TransportListenerSettings>(),
                It.IsAny<ITransportListenerCallback>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("opc.tcp")]
    [TestCase("wss")]
    [TestCase("opc.wss")]
    public async Task OpenForwardsTheExactConfiguredListenerTlsAndCancellationToTheRegisteredBinding(string scheme)
    {
        ReverseConnectionProfile profile = Profile() with
        {
            ListenerUrl = $"{scheme}://localhost:4841/client",
            EndpointUrl = $"{scheme}://server.example.test:4840/Factory",
            TlsConfigurationId = scheme == "opc.tcp" ? null : "listener-tls"
        };
        var inner = new Mock<ITransportListener>();
        var listener = new MatchedReverseListener(inner.Object, profile);
        await using (listener.ConfigureAwait(false))
        {
            var settings = new TransportListenerSettings
            {
                ReverseConnectListener = true,
                ServerCertificates = scheme == "opc.tcp" ? null : new Mock<ICertificateRegistry>().Object,
                CertificateValidator = scheme == "opc.tcp" ? null : new Mock<ICertificateValidatorEx>().Object
            };
            var address = new Uri(profile.ListenerUrl);
            using var cancellation = new CancellationTokenSource();
            inner.Setup(value => value.OpenAsync(address, settings, null!, cancellation.Token))
                .Returns(ValueTask.CompletedTask);

            await listener.OpenAsync(address, settings, null!, cancellation.Token).ConfigureAwait(false);

            inner.Verify(value => value.OpenAsync(address, settings, null!, cancellation.Token), Times.Once);
        }
    }

    [Test]
    public async Task CertificateRenewalForwardsTheNewValidatorAndRegistryWithoutRelaxingThePeerPin()
    {
        var context = new ListenerContext();
        await using (context.ConfigureAwait(false))
        {
            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Strict);
            var certificates = new Mock<ICertificateRegistry>(MockBehavior.Strict);
            context.Inner.Setup(value => value.CertificateUpdate(validator.Object, certificates.Object));
            int deliveries = 0;
            context.Listener.ConnectionWaiting += (_, args) =>
            {
                deliveries++;
                args.Accepted = true;
                return Task.CompletedTask;
            };

            context.Listener.CertificateUpdate(validator.Object, certificates.Object);
            var wrong = new WaitingConnection("urn:other-server", new Uri(Profile().EndpointUrl));
            await context.DeliverAsync(wrong).ConfigureAwait(false);
            var exact = new WaitingConnection("urn:expected-server", new Uri(Profile().EndpointUrl));
            await context.DeliverAsync(exact).ConfigureAwait(false);

            Assert.That(wrong.Accepted, Is.False);
            Assert.That(exact.Accepted, Is.True);
            Assert.That(deliveries, Is.EqualTo(1));
            context.Inner.Verify(value => value.CertificateUpdate(validator.Object, certificates.Object), Times.Once);
            validator.VerifyNoOtherCalls();
            certificates.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task AHandlerCompletingAfterDisposalCannotAcceptItsPeer()
    {
        var context = new ListenerContext();
        await using (context.ConfigureAwait(false))
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Listener.ConnectionWaiting += async (_, args) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                args.Accepted = true;
            };
            var hello = new WaitingConnection("urn:expected-server", new Uri(Profile().EndpointUrl));
            Task pending = context.DeliverAsync(hello);
            try
            {
                await entered.Task.WaitAsync(s_bound).ConfigureAwait(false);

                await context.Listener.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult();
            }
            await pending.WaitAsync(s_bound).ConfigureAwait(false);

            Assert.That(hello.Accepted, Is.False);
            context.Inner.Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task ConcurrentDisposalSharesCleanupRejectsLateEventsAndUnsubscribesExactlyOnce()
    {
        var context = new ListenerContext();
        await using (context.ConfigureAwait(false))
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Inner.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                entered.TrySetResult();
                return new ValueTask(release.Task);
            });
            context.Listener.ConnectionWaiting += (_, args) =>
                throw new InvalidOperationException("A disposed listener cannot deliver a peer.");
            Task first = context.Listener.DisposeAsync().AsTask();
            Task repeated = context.Listener.DisposeAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_bound).ConfigureAwait(false);
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(repeated, Is.SameAs(first));
                var hello = new WaitingConnection("urn:expected-server", new Uri(Profile().EndpointUrl))
                {
                    Accepted = true
                };

                await context.DeliverAsync(hello).ConfigureAwait(false);

                Assert.That(hello.Accepted, Is.False);
                Assert.That(() => context.Listener.CertificateUpdate(
                    new Mock<ICertificateValidatorEx>().Object, new Mock<ICertificateRegistry>().Object),
                    Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                release.TrySetResult();
            }
            await Task.WhenAll(first, repeated).WaitAsync(s_bound).ConfigureAwait(false);
            context.Inner.Verify(value => value.DisposeAsync(), Times.Once);
            context.Inner.VerifyRemove(
                value => value.ConnectionWaiting -= It.IsAny<ConnectionWaitingHandlerAsync>(), Times.Once);
        }
    }

    [Test]
    public async Task MatchedBindingsReuseTheHostFactoryWithoutAllowingLiveRegistryMutation()
    {
        var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
        var inner = new Mock<ITransportListener>();
        var channel = new Mock<ITransportChannel>(MockBehavior.Strict);
        registry.Setup(value => value.CreateListener("opc.tcp", telemetry.Object)).Returns(inner.Object);
        registry.Setup(value => value.CreateChannel("opc.tcp", telemetry.Object)).Returns(channel.Object);
        var bindings = new MatchedReverseTransportBindings(registry.Object, Profile());

        ITransportListener? listener = bindings.CreateListener("opc.tcp", telemetry.Object);
        Assert.That(listener, Is.TypeOf<MatchedReverseListener>());
        await using (listener!.ConfigureAwait(false))
        {
            Assert.That(bindings.CreateChannel("opc.tcp", telemetry.Object), Is.SameAs(channel.Object));
            Assert.That(() => bindings.RegisterListenerFactory(new Mock<ITransportListenerFactory>().Object),
                Throws.InvalidOperationException);
            Assert.That(() => bindings.RegisterChannelFactory(new Mock<ITransportChannelFactory>().Object),
                Throws.InvalidOperationException);
            Assert.That(() => bindings.RemoveListenerFactory("opc.tcp"), Throws.InvalidOperationException);
            Assert.That(() => bindings.RemoveChannelFactory("opc.tcp"), Throws.InvalidOperationException);
            registry.Verify(value => value.CreateListener("opc.tcp", telemetry.Object), Times.Once);
            registry.Verify(value => value.CreateChannel("opc.tcp", telemetry.Object), Times.Once);
            inner.VerifyAdd(
                value => value.ConnectionWaiting += It.IsAny<ConnectionWaitingHandlerAsync>(), Times.Once);
            registry.VerifyNoOtherCalls();
        }
        inner.Verify(value => value.DisposeAsync(), Times.Once);
    }

    private static ReverseConnectionProfile Profile()
    {
        return new ReverseConnectionProfile
        {
            ListenerUrl = "opc.tcp://localhost:4841/client",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory",
            ServerUri = "urn:expected-server"
        };
    }

    private sealed class ListenerContext : IAsyncDisposable
    {
        public ListenerContext()
        {
            Inner.SetupAdd(value => value.ConnectionWaiting += It.IsAny<ConnectionWaitingHandlerAsync>())
                .Callback<ConnectionWaitingHandlerAsync>(handler => m_receive = handler);
            Inner.SetupRemove(value => value.ConnectionWaiting -= It.IsAny<ConnectionWaitingHandlerAsync>());
            Inner.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            Listener = new MatchedReverseListener(Inner.Object, Profile());
        }

        public Mock<ITransportListener> Inner { get; } = new(MockBehavior.Strict);

        public MatchedReverseListener Listener { get; }

        public Task DeliverAsync(ConnectionWaitingEventArgs args)
        {
            return (m_receive ?? throw new InvalidOperationException("The listener filter was not registered."))(
                Inner.Object, args);
        }

        public ValueTask DisposeAsync()
        {
            return Listener.DisposeAsync();
        }

        private ConnectionWaitingHandlerAsync? m_receive;
    }

    private sealed class WaitingConnection : ConnectionWaitingEventArgs
    {
        public WaitingConnection(string serverUri, Uri endpointUrl)
            : base(serverUri, endpointUrl)
        {
        }
    }

    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);
}
