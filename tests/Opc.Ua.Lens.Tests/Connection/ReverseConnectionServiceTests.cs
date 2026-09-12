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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ReverseConnectionServiceTests
{
    [Test]
    public async Task RestoringReverseIntentNeitherConstructsNorStartsAListener()
    {
        ReverseConnectionProfile reverse = Profile();
        var token = new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" };
        var endpoint = new EndpointDescription(reverse.EndpointUrl)
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            Server = new ApplicationDescription { ApplicationUri = reverse.ServerUri },
            UserIdentityTokens = [token]
        };
        ConnectionProfile profile = ConnectionProfile.Create(endpoint, token, SubscriptionEngineKind.ChannelV2) with
        {
            ReverseConnection = reverse
        };
        string json = JsonSerializer.Serialize(profile, ConnectionProfileJsonContext.Default.ConnectionProfile);
        ConnectionProfile restored = JsonSerializer.Deserialize(
            json, ConnectionProfileJsonContext.Default.ConnectionProfile)!;
        restored.Validate();
        var factory = new Mock<IReverseConnectionRuntimeFactory>(MockBehavior.Strict);
        var service = new ReverseConnectionService(factory.Object);
        await using (service.ConfigureAwait(false))
        {
            Assert.That(restored.ReverseConnection, Is.EqualTo(reverse));
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            Assert.That(() => service.Acquire(restored.ReverseConnection!), Throws.InvalidOperationException);
            await Assert.ThatAsync(() => service.WaitAsync(restored.ReverseConnection!),
                Throws.InvalidOperationException).ConfigureAwait(false);
            factory.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task ExplicitStartIsSharedAndActiveSessionLeasePreventsListenerReplacementOrStop()
    {
        ReverseConnectionProfile profile = Profile();
        Mock<IReverseConnectionRuntime> runtime = Runtime();
        var factory = new Mock<IReverseConnectionRuntimeFactory>();
        factory.Setup(value => value.Create(profile)).Returns(runtime.Object);
        var service = new ReverseConnectionService(factory.Object);
        await using (service.ConfigureAwait(false))
        {
            await service.StartAsync(profile).ConfigureAwait(false);
            await service.StartAsync(profile).ConfigureAwait(false);
            using (ReverseConnectionLease lease = service.Acquire(profile))
            {
                Assert.That(lease.Runtime, Is.SameAs(runtime.Object));
                await Assert.ThatAsync(() => service.StopAsync(), Throws.InvalidOperationException)
                    .ConfigureAwait(false);
                await Assert.ThatAsync(() => service.StartAsync(profile with { ServerUri = "urn:other-server" }),
                    Throws.InvalidOperationException).ConfigureAwait(false);
                runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
                runtime.Verify(value => value.DisposeAsync(), Times.Never);
            }
            await service.StopAsync().ConfigureAwait(false);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            factory.Verify(value => value.Create(profile), Times.Once);
            runtime.Verify(value => value.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
            runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            runtime.Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task CancelingAWaitReleasesOnlyItsLeaseAndPreservesTheExplicitListener()
    {
        ReverseConnectionProfile profile = Profile();
        Mock<IReverseConnectionRuntime> runtime = Runtime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new TaskCompletionSource<ITransportWaitingConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Setup(value => value.WaitAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) =>
        {
            entered.TrySetResult();
            return peer.Task.WaitAsync(ct);
        });
        var factory = new Mock<IReverseConnectionRuntimeFactory>();
        factory.Setup(value => value.Create(profile)).Returns(runtime.Object);
        var service = new ReverseConnectionService(factory.Object);
        await using (service.ConfigureAwait(false))
        {
            await service.StartAsync(profile).ConfigureAwait(false);
            Task<ITransportWaitingConnection> pending = service.WaitAsync(profile);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Waiting));

            await service.CancelWaitAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
            using ReverseConnectionLease next = service.Acquire(profile);
            runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task StopDrainsThePendingWaitBeforeDisposingItsOwnedRuntime()
    {
        ReverseConnectionProfile profile = Profile();
        Mock<IReverseConnectionRuntime> runtime = Runtime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new TaskCompletionSource<ITransportWaitingConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Setup(value => value.WaitAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) =>
        {
            entered.TrySetResult();
            return peer.Task.WaitAsync(ct);
        });
        var factory = new Mock<IReverseConnectionRuntimeFactory>();
        factory.Setup(value => value.Create(profile)).Returns(runtime.Object);
        var service = new ReverseConnectionService(factory.Object);
        await using (service.ConfigureAwait(false))
        {
            await service.StartAsync(profile).ConfigureAwait(false);
            Task<ITransportWaitingConnection> pending = service.WaitAsync(profile);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await service.StopAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            runtime.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
        }
    }

    [Test]
    public async Task CanceledStartDisposesUninstalledRuntimeAndAllowsAnExplicitRetry()
    {
        ReverseConnectionProfile profile = Profile();
        Mock<IReverseConnectionRuntime> runtime = Runtime();
        using var cancellation = new CancellationTokenSource();
        runtime.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        });
        var factory = new Mock<IReverseConnectionRuntimeFactory>();
        factory.Setup(value => value.Create(profile)).Returns(runtime.Object);
        var service = new ReverseConnectionService(factory.Object);
        await using (service.ConfigureAwait(false))
        {
            await Assert.ThatAsync(() => service.StartAsync(profile, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            runtime.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));

            Mock<IReverseConnectionRuntime> replacement = Runtime();
            factory.Setup(value => value.Create(profile)).Returns(replacement.Object);
            await service.StartAsync(profile).ConfigureAwait(false);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
            replacement.Verify(value => value.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task MissingFactoryCapabilityDoesNotLeaveAStartingListener()
    {
        var factory = new Mock<IReverseConnectionRuntimeFactory>();
        factory.Setup(value => value.Create(It.IsAny<ReverseConnectionProfile>()))
            .Throws(new NotSupportedException("The listener binding is not configured."));
        var service = new ReverseConnectionService(factory.Object);
        await using (service.ConfigureAwait(false))
        {
            await Assert.ThatAsync(() => service.StartAsync(Profile()), Throws.TypeOf<NotSupportedException>())
                .ConfigureAwait(false);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
        }
    }

    [Test]
    public async Task DisposalCancelsStartupAndAwaitsUninstalledRuntimeCleanup()
    {
        ReverseConnectionProfile profile = Profile();
        Mock<IReverseConnectionRuntime> runtime = Runtime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) =>
        {
            entered.TrySetResult();
            return release.Task.WaitAsync(ct);
        });
        var factory = new Mock<IReverseConnectionRuntimeFactory>();
        factory.Setup(value => value.Create(profile)).Returns(runtime.Object);
        var service = new ReverseConnectionService(factory.Object);
        Task starting = service.StartAsync(profile);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        await service.DisposeAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => starting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        runtime.Verify(value => value.DisposeAsync(), Times.Once);
        Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
        await service.DisposeAsync().ConfigureAwait(false);
        runtime.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4840/Factory", true)]
    [TestCase("urn:other-server", "opc.tcp://server.example.test:4840/Factory", false)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4840/Other", false)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4840/factory", false)]
    [TestCase("urn:expected-server", "opc.tcp://server.example.test:4842/Factory", false)]
    public async Task OnlyExactServerUriAndEndpointAreForwardedBeforeTheManagerCanClaimAReverseHello(
        string serverUri,
        string endpointUrl,
        bool accepted)
    {
        var inner = new Mock<ITransportListener>();
        ConnectionWaitingHandlerAsync? receive = null;
        inner.SetupAdd(value => value.ConnectionWaiting += It.IsAny<ConnectionWaitingHandlerAsync>())
            .Callback<ConnectionWaitingHandlerAsync>(handler => receive = handler);
        var listener = new MatchedReverseListener(inner.Object, Profile());
        await using (listener.ConfigureAwait(false))
        {
            int deliveries = 0;
            listener.ConnectionWaiting += (_, args) =>
            {
                deliveries++;
                args.Accepted = true;
                return Task.CompletedTask;
            };
            var hello = new WaitingConnection(serverUri, new Uri(endpointUrl));
            Assert.That(receive, Is.Not.Null);
            await receive!(inner.Object, hello).ConfigureAwait(false);

            Assert.That(hello.Accepted, Is.EqualTo(accepted));
            Assert.That(deliveries, Is.EqualTo(accepted ? 1 : 0));
        }
        inner.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task ConfigurationProviderCannotSilentlyAddOrChangeListeners()
    {
        ReverseConnectionProfile profile = Profile();
        var source = new Mock<IReverseConnectConfigurationProvider>();
        source.Setup(value => value.ConfigureAsync(
            It.IsAny<ApplicationConfiguration>(), It.IsAny<ReverseConnectClientConfiguration>(),
            It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(new ReverseConnectClientConfiguration
            {
                ClientEndpoints =
            [
                new ReverseConnectClientEndpoint { EndpointUrl = profile.ListenerUrl },
                new ReverseConnectClientEndpoint { EndpointUrl = "opc.tcp://0.0.0.0:4842" }
            ],
                HoldTime = profile.HoldTimeSeconds * 1000,
                WaitTimeout = profile.WaitTimeoutSeconds * 1000
            }));
        var pinned = new PinnedReverseConfigurationProvider(profile, source.Object);

        await Assert.ThatAsync(async () => await pinned.ConfigureAsync(null, profile.CreateConfiguration())
            .ConfigureAwait(false), Throws.InvalidOperationException).ConfigureAwait(false);
    }

    [Test]
    public void WssReverseRequiresExplicitTlsAndARegisteredBinding()
    {
        ReverseConnectionProfile profile = Profile() with
        {
            EndpointUrl = "wss://server.example.test:443/Factory",
            ListenerUrl = "wss://localhost:4841/client"
        };
        Assert.That(() => profile.Validate(), Throws.InvalidOperationException);
        var transports = new ConnectionTransportCatalog(DefaultTransportBindingRegistry.WithDefaultTcp());
        Assert.That(() => transports.RequireReverse(profile with { TlsConfigurationId = "listener-tls" }),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public async Task ListenerTlsFactoryCannotBorrowTheToolsOrPrimaryCertificateManager()
    {
        Mock<ITelemetryContext> telemetry = Telemetry();
        var manager = new Mock<ICertificateManager>();
        manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var configuration = new ApplicationConfiguration(telemetry.Object)
        {
            CertificateManager = manager.Object,
            SecurityConfiguration = new SecurityConfiguration()
        };
        var catalog = new ConnectionConfigurationCatalog(reverseTlsSources:
        [
            new ConfiguredReverseTlsSource("listener-tls", "Configured TLS", _ => Task.FromResult(configuration))
        ]);
        Assert.That(catalog.TryClaimManager(configuration), Is.True);
        var bindings = new Mock<ITransportBindingRegistry>();
        bindings.Setup(value => value.HasChannelFactory("wss")).Returns(true);
        bindings.Setup(value => value.HasListenerFactory("wss")).Returns(true);
        var factory = new StackReverseConnectionRuntimeFactory(
            telemetry.Object, new ConnectionTransportCatalog(bindings.Object), catalog);
        var service = new ReverseConnectionService(factory);
        await using (service.ConfigureAwait(false))
        {
            await Assert.ThatAsync(() => service.StartAsync(Profile() with
            {
                EndpointUrl = "wss://server.example.test:443/Factory",
                ListenerUrl = "wss://localhost:4841/client",
                TlsConfigurationId = "listener-tls"
            }), Throws.InvalidOperationException).ConfigureAwait(false);
            manager.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);
            bindings.Verify(value => value.CreateListener(
                It.IsAny<string>(), It.IsAny<ITelemetryContext>()), Times.Never);
            Assert.That(service.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
        }
    }

    [Test]
    public void CapabilityRowsReflectRegistrationsInsteadOfAssumingPlatformSupport()
    {
        var transports = new ConnectionTransportCatalog(DefaultTransportBindingRegistry.WithDefaultTcp());
        foreach (ConnectionTransportCapability capability in transports.Capabilities)
        {
            Assert.That(capability.ForwardRegistered, Is.EqualTo(capability.Scheme == "opc.tcp"));
            Assert.That(capability.Prerequisites, Is.Not.Empty);
        }
    }

    private static ReverseConnectionProfile Profile()
    {
        return new ReverseConnectionProfile
        {
            ListenerUrl = "opc.tcp://localhost:4841",
            ServerUri = "urn:expected-server",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory"
        };
    }

    private static Mock<IReverseConnectionRuntime> Runtime()
    {
        var runtime = new Mock<IReverseConnectionRuntime>();
        runtime.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runtime.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runtime.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return runtime;
    }

    private static Mock<ITelemetryContext> Telemetry()
    {
        var telemetry = new Mock<ITelemetryContext>();
        telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return telemetry;
    }

    private sealed class WaitingConnection : ConnectionWaitingEventArgs
    {
        public WaitingConnection(string serverUri, Uri endpointUrl)
            : base(serverUri, endpointUrl)
        {
        }
    }
}
