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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Unit tests for <see cref="ReverseConnectHost"/> lifecycle and
    /// null-guard contracts.
    /// </summary>
    [TestFixture]
    [Category("ReverseConnectHost")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReverseConnectHostTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorWithTelemetryOnlySucceeds()
        {
            Assert.That(() => new ReverseConnectHost(m_telemetry), Throws.Nothing);
        }

        [Test]
        public void ConstructorWithRegistrySucceeds()
        {
            ITransportBindingRegistry registry =
                DefaultTransportBindingRegistry.WithDefaultTcp();

            Assert.That(
                () => new ReverseConnectHost(m_telemetry, registry),
                Throws.Nothing);
        }

        [Test]
        public void ConstructorWithNullRegistryFallsBackToDefaultTcpOnCreateListener()
        {
            // Passing null registry is allowed; the host creates one lazily.
            var host = new ReverseConnectHost(m_telemetry, transportBindings: null);

            // Before CreateListener is called the Url is unset.
            Assert.That(host.Url, Is.Null);
        }

        [Test]
        public void UrlIsNullBeforeCreateListener()
        {
            var host = new ReverseConnectHost(m_telemetry);
            Assert.That(host.Url, Is.Null);
        }

        [Test]
        public void CreateListenerThrowsOnNullUrl()
        {
            var host = new ReverseConnectHost(m_telemetry);

            Assert.That(
                () => host.CreateListener(
                    null!,
                    IgnoreConnectionWaitingAsync,
                    IgnoreConnectionStatusChanged),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("url"));
        }

        [Test]
        public void CreateListenerWithUnsupportedSchemeThrows()
        {
            // Registry that has no factories registered → scheme lookup returns null.
            var emptyRegistry = new DefaultTransportBindingRegistry();
            var host = new ReverseConnectHost(m_telemetry, emptyRegistry);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => host.CreateListener(
                    new Uri("opc.nonexistent://localhost:4840"),
                    IgnoreConnectionWaitingAsync,
                    IgnoreConnectionStatusChanged))!;

            Assert.That(ex.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadProtocolVersionUnsupported));
        }

        [Test]
        public async Task CreateListenerSetsUrlPropertyAsync()
        {
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();
            var host = new ReverseConnectHost(m_telemetry, registry);
            var url = new Uri("opc.tcp://localhost:4840");

            host.CreateListener(
                url,
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            Assert.That(host.Url, Is.EqualTo(url));

            // Clean up the listener created internally.
            await host.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public void OpenBeforeCreateListenerThrowsBadInvalidState()
        {
            var host = new ReverseConnectHost(m_telemetry);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await host.OpenAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void CloseWithoutCreateListenerIsNoOp()
        {
            var host = new ReverseConnectHost(m_telemetry);

            // Must not throw even though CreateListener was never called.
            Assert.That(async () => await host.CloseAsync().ConfigureAwait(false), Throws.Nothing);
        }

        [Test]
        public void CloseAfterCreateListenerIsIdempotent()
        {
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();
            var host = new ReverseConnectHost(m_telemetry, registry);

            host.CreateListener(
                new Uri("opc.tcp://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            // First close: wires down the listener.
            Assert.That(async () => await host.CloseAsync().ConfigureAwait(false), Throws.Nothing);

            // A second Close must not throw (m_listener is still non-null but the
            // event unsubscriptions and inner CloseAsync() are idempotent).
            Assert.That(async () => await host.CloseAsync().ConfigureAwait(false), Throws.Nothing);
        }

        [Test]
        public async Task CreateListenerWithTlsParametersOverloadSetsUrlAsync()
        {
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();
            var host = new ReverseConnectHost(m_telemetry, registry);
            var url = new Uri("opc.tcp://localhost:4840");

            // 5-argument overload with null TLS params (valid for plain TCP).
            host.CreateListener(
                url,
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged,
                serverCertificates: null,
                certificateValidator: null);

            Assert.That(host.Url, Is.EqualTo(url));

            await host.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CreateListenerWithNullRegistryUsesDefaultTcpAsync()
        {
            // When transportBindings is null the host lazily creates the default
            // TCP registry; the opc.tcp scheme must therefore be resolvable.
            var host = new ReverseConnectHost(m_telemetry, transportBindings: null);

            Assert.That(
                () => host.CreateListener(
                    new Uri("opc.tcp://localhost:4840"),
                    IgnoreConnectionWaitingAsync,
                    IgnoreConnectionStatusChanged),
                Throws.Nothing);

            await host.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public void CloseFailureDisposesListener()
        {
            var closeError = new InvalidOperationException("close failed");
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(closeError)));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await host.CloseAsync().ConfigureAwait(false))!;

            Assert.That(exception, Is.SameAs(closeError));
            listener.Verify(l => l.DisposeAsync(), Times.Once);
            Assert.That(async () => await host.CloseAsync().ConfigureAwait(false), Throws.Nothing);
        }

        [Test]
        public async Task CanceledCloseKeepsListenerRetryableAsync()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(cts.Token))
                .Returns(new ValueTask(Task.FromCanceled(cts.Token)));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            Assert.That(
                async () => await host.CloseAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            listener.Verify(l => l.DisposeAsync(), Times.Never);

            listener
                .Setup(l => l.CloseAsync(default))
                .Returns(default(ValueTask));
            await host.CloseAsync().ConfigureAwait(false);
            listener.Verify(l => l.CloseAsync(default), Times.Once);
        }

        [Test]
        public void DisposeAsyncWithoutCreateListenerIsNoOp()
        {
            var host = new ReverseConnectHost(m_telemetry);

            Assert.That(
                async () => await host.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task DisposeAsyncClosesThenDisposesListenerAsync()
        {
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            await host.DisposeAsync().ConfigureAwait(false);

            listener.Verify(l => l.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            listener.Verify(l => l.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            await host.DisposeAsync().ConfigureAwait(false);
            await host.DisposeAsync().ConfigureAwait(false);

            // A second DisposeAsync releases ownership only once, so the
            // underlying listener is disposed exactly once.
            listener.Verify(l => l.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task DisposeAsyncDisposesListenerEvenWhenCloseFailsAsync()
        {
            var closeError = new InvalidOperationException("close failed");
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(closeError)));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            // A close failure during disposal must be swallowed and the
            // listener disposed regardless.
            Assert.That(
                async () => await host.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
            listener.Verify(l => l.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task ConcurrentDisposeAsyncClosesAndDisposesListenerExactlyOnce()
        {
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            // Fan out many concurrent DisposeAsync calls that all start
            // together. Ownership of the listener is claimed atomically
            // (Interlocked.Exchange), so exactly one caller tears it down.
            using var start = new ManualResetEventSlim(false);
            var disposals = new Task[16];
            for (int i = 0; i < disposals.Length; i++)
            {
                disposals[i] = Task.Run(async () =>
                {
                    start.Wait();
                    await host.DisposeAsync().ConfigureAwait(false);
                });
            }
            start.Set();
            await Task.WhenAll(disposals).ConfigureAwait(false);

            listener.Verify(l => l.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            listener.Verify(l => l.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task GatedOpenSerializesWithDisposeAndTearsDownExactlyOnceAsync()
        {
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var openRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportListenerSettings>(),
                    It.IsAny<ITransportListenerCallback>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => openEntered.TrySetResult(true))
                .Returns(new ValueTask(openRelease.Task));
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            // An open blocks inside the listener while holding the gate.
            Task open = host.OpenAsync().AsTask();
            await openEntered.Task.ConfigureAwait(false);

            // Dispose must queue behind the in-flight open: it may not null or
            // dispose the listener while the open is still resuming.
            Task dispose = host.DisposeAsync().AsTask();
            Assert.That(
                dispose.IsCompleted,
                Is.False,
                "DisposeAsync must wait for the in-flight open");
            listener.Verify(l => l.DisposeAsync(), Times.Never);

            // Releasing the open lets it complete; the dispose then tears the
            // listener down exactly once.
            openRelease.SetResult(true);
            await open.ConfigureAwait(false);
            await dispose.ConfigureAwait(false);

            listener.Verify(l => l.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            listener.Verify(l => l.DisposeAsync(), Times.Once);

            // A further open after disposal is rejected without resurrecting the
            // listener.
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await host.OpenAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task TwoConcurrentDisposeDuringGatedOpenShareOneTeardownAsync()
        {
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var openRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportListenerSettings>(),
                    It.IsAny<ITransportListenerCallback>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => openEntered.TrySetResult(true))
                .Returns(new ValueTask(openRelease.Task));
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            // An open blocks inside the listener while holding the gate.
            Task open = host.OpenAsync().AsTask();
            await openEntered.Task.ConfigureAwait(false);

            // Two concurrent disposals must both queue behind the in-flight
            // open: they share one teardown task and neither may complete (nor
            // touch the listener) while the open is still resuming.
            Task dispose1 = host.DisposeAsync().AsTask();
            Task dispose2 = host.DisposeAsync().AsTask();
            Assert.That(
                dispose1.IsCompleted,
                Is.False,
                "the first DisposeAsync must wait for the in-flight open");
            Assert.That(
                dispose2.IsCompleted,
                Is.False,
                "the second DisposeAsync must wait for the shared teardown");
            listener.Verify(l => l.DisposeAsync(), Times.Never);

            // Releasing the open lets it complete; both disposals then observe
            // the same single teardown and complete together.
            openRelease.SetResult(true);
            await open.ConfigureAwait(false);
            await Task.WhenAll(dispose1, dispose2).ConfigureAwait(false);

            // Exactly one close and one dispose happen despite two callers.
            listener.Verify(l => l.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            listener.Verify(l => l.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task GatedOpenSerializesWithCloseWhichRunsAfterOpenCompletesAsync()
        {
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var openRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportListenerSettings>(),
                    It.IsAny<ITransportListenerCallback>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => openEntered.TrySetResult(true))
                .Returns(new ValueTask(openRelease.Task));
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);
            host.CreateListener(
                new Uri("opc.test://localhost:4840"),
                IgnoreConnectionWaitingAsync,
                IgnoreConnectionStatusChanged);

            Task open = host.OpenAsync().AsTask();
            await openEntered.Task.ConfigureAwait(false);

            // Close must queue behind the in-flight open rather than racing it,
            // so no concurrent double close can occur.
            Task close = host.CloseAsync().AsTask();
            Assert.That(
                close.IsCompleted,
                Is.False,
                "CloseAsync must wait for the in-flight open");
            listener.Verify(l => l.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);

            openRelease.SetResult(true);
            await open.ConfigureAwait(false);
            await close.ConfigureAwait(false);

            // A temporary close closes the listener exactly once and leaves it
            // undisposed (reopenable).
            listener.Verify(l => l.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            listener.Verify(l => l.DisposeAsync(), Times.Never);

            await host.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CreateListenerAfterDisposeThrowsAndCreatesNoListenerAsync()
        {
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(listener.Object);
            var host = new ReverseConnectHost(m_telemetry, registry.Object);

            await host.DisposeAsync().ConfigureAwait(false);

            // CreateListener must reject after disposal and never create a
            // listener that would then be leaked.
            Assert.Throws<ObjectDisposedException>(
                () => host.CreateListener(
                    new Uri("opc.test://localhost:4840"),
                    IgnoreConnectionWaitingAsync,
                    IgnoreConnectionStatusChanged));
            registry.Verify(
                r => r.CreateListener("opc.test", m_telemetry),
                Times.Never);
            Assert.That(host.HasListener, Is.False);
        }

        [Test]
        public async Task CreateListenerLosingRaceWithDisposeDisposesCreatedListenerAsync()
        {
            var createEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseCreate = new ManualResetEventSlim(false);
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            listener
                .Setup(l => l.DisposeAsync())
                .Returns(default(ValueTask));
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", m_telemetry))
                .Returns(() =>
                {
                    // Signal that the creation is in progress (the lifecycle gate
                    // is held) and block until the test has queued a DisposeAsync
                    // behind the gate.
                    createEntered.TrySetResult(true);
                    releaseCreate.Wait();
                    return listener.Object;
                });
            var host = new ReverseConnectHost(m_telemetry, registry.Object);

            Task create = Task.Run(
                () => host.CreateListener(
                    new Uri("opc.test://localhost:4840"),
                    IgnoreConnectionWaitingAsync,
                    IgnoreConnectionStatusChanged));
            await createEntered.Task.ConfigureAwait(false);

            // DisposeAsync claims disposal (sets the disposed flag) then queues
            // behind the in-flight CreateListener on the shared lifecycle gate.
            Task dispose = host.DisposeAsync().AsTask();
            Assert.That(
                dispose.IsCompleted,
                Is.False,
                "DisposeAsync must queue behind the in-flight CreateListener");

            // Let the creation finish: it observes the lost race and rejects,
            // and the queued DisposeAsync then disposes the created listener.
            releaseCreate.Set();

            Assert.That(
                async () => await create.ConfigureAwait(false),
                Throws.InstanceOf<ObjectDisposedException>());
            await dispose.ConfigureAwait(false);

            listener.Verify(l => l.DisposeAsync(), Times.Once);
            Assert.That(host.HasListener, Is.False);
        }

        private static Task IgnoreConnectionWaitingAsync(
            object sender,
            ConnectionWaitingEventArgs args)
        {
            return Task.CompletedTask;
        }

        private static void IgnoreConnectionStatusChanged(
            object? sender,
            ConnectionStatusEventArgs args)
        {
        }
    }
}
