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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

// This fixture defines subclasses that override the obsolete legacy
// OnUpdateConfiguration hooks and calls the async lifecycle directly.
#pragma warning disable CS0618
#pragma warning disable CS0672

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Deterministic lifecycle coverage for the async
    /// <see cref="ReverseConnectManager"/> using fake transport listeners
    /// and providers.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ReverseConnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ReverseConnectManagerLifecycleTests
    {
        private const string Scheme = "opc.fake";

        private static ITelemetryContext CreateTelemetry()
        {
            return NUnitTelemetryContext.Create();
        }

        private static Uri Url(int port, string path = "/reverse")
        {
            return new Uri(
                Scheme + "://localhost:" +
                port.ToString(CultureInfo.InvariantCulture) + path);
        }

        private static ReverseConnectClientConfiguration ConfigFor(params Uri[] urls)
        {
            return new ReverseConnectClientConfiguration
            {
                ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                    urls.Select(u => new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = u.ToString()
                    }).ToArray())
            };
        }

        [Test]
        public async Task StartServiceAsyncRccConfigOpensListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            Uri url = Url(20001);

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            Assert.That(harness.Listeners, Has.Count.EqualTo(1));
            Assert.That(harness.Listeners[0].OpenCount, Is.EqualTo(1));
            Assert.That(harness.Listeners[0].OpenedUrl, Is.EqualTo(url));
        }

        [Test]
        public async Task StartServiceAsyncBindFailurePropagatesBadNoCommunication()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20002);
            harness.FailOpenFor(url, new ServiceResultException(StatusCodes.BadNoCommunication));
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.StartServiceAsync(ConfigFor(url)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(exception.Message, Does.Contain(url.ToString()));
            // The failed candidate listener is closed (rolled back).
            Assert.That(harness.Listeners[0].CloseCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void StartServiceAsyncInvalidEndpointThrowsBadTcpEndpointUrlInvalid()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            var configuration = new ReverseConnectClientConfiguration
            {
                ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                    new[]
                    {
                        new ReverseConnectClientEndpoint { EndpointUrl = "not a uri" }
                    })
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.StartServiceAsync(configuration))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
        }

        [Test]
        public async Task StartServiceAsyncCancellationDuringOpenCleansCandidate()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20003);
            using var cts = new CancellationTokenSource();
            harness.SetOpenGate(url, (ct) =>
            {
                cts.Cancel();
                return Task.Delay(Timeout.Infinite, ct);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Assert.That(
                async () => await manager.StartServiceAsync(ConfigFor(url), cts.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(harness.Listeners[0].CloseCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task ReloadRestoresPreviousListenersWhenNewFails()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20010);
            Uri replacement = Url(20011);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            Assert.That(harness.OpenedUrls(), Does.Contain(original));

            // The replacement endpoint fails to open — the reload must restore
            // the original listener and remain started.
            harness.FailOpenFor(replacement, new ServiceResultException(StatusCodes.BadNoCommunication));

            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration)
                .ConfigureAwait(false);

            // Original must be actively serving again: a listener bound to the
            // original URL is currently open (not merely ever-opened).
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True,
                "the restored original listener must be actively open");
            // The failed replacement listener must not be left open.
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.False);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
        }

        [Test]
        public async Task ReloadManualEndpointsSurvive()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri manual = Url(20020);
            Uri configured = Url(20021);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.AddEndpoint(manual);

            await manager.StartServiceAsync(ConfigFor(configured)).ConfigureAwait(false);
            Assert.That(harness.OpenedUrls(), Does.Contain(manual));

            Uri configured2 = Url(20022);
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, configured2),
                manager.CurrentWatcherGeneration)
                .ConfigureAwait(false);

            // Manual endpoint must remain actively served after the configured
            // reload (its host is reused by identity and re-opened).
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == manual && l.IsOpen),
                Is.True,
                "the manual listener must be actively open after reload");
            // The superseded configured endpoint must no longer be open.
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == configured && l.IsOpen),
                Is.False);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == configured2 && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task ConcurrentStartOnlyOneSucceeds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Task first = manager.StartServiceAsync(ConfigFor(Url(20030)));
            Task second = manager.StartServiceAsync(ConfigFor(Url(20031)));

            int failures = 0;
            foreach (Task t in new[] { first, second })
            {
                try
                {
                    await t.ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    failures++;
                }
            }

            Assert.That(failures, Is.EqualTo(1));
        }

        [Test]
        public async Task StartThenDisposeIsSafe()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(Url(20040))).ConfigureAwait(false);
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(harness.Listeners[0].CloseCount, Is.GreaterThanOrEqualTo(1));
            Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.StartServiceAsync(ConfigFor(Url(20041))));
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var manager = new ReverseConnectManager(telemetry);

            await manager.DisposeAsync().ConfigureAwait(false);
            Assert.That(
                async () => await manager.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task QueuedGateWaiterCancellationThrows()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri blocking = Url(20050);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.SetOpenGate(blocking, _ => release.Task);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // First start acquires the lifecycle gate and blocks inside open.
            Task first = manager.StartServiceAsync(ConfigFor(blocking));
            await harness.OpenObserved.ConfigureAwait(false);

            // A stop queued behind the gate is cancelled and must throw OCE.
            using var cts = new CancellationTokenSource();
            Task queuedStop = manager.StopServiceAsync(cts.Token);
            cts.Cancel();

            Assert.That(
                async () => await queuedStop.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            // Release the first start so the manager and its gate return to a
            // consistent state before disposal.
            release.SetResult(true);
            await first.ConfigureAwait(false);
        }

        [Test]
        public async Task LegacyOverrideOmittingBaseStillActivatesCandidate()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20060);
            await using var manager = new SuppressingManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Omitting base no longer suppresses: the (unmodified) candidate is
            // still activated so the configured listener opens.
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task LegacyOverrideReplacingWithoutBaseAppliesReplacement()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri injected = Url(20065);
            await using var manager = new ReplacingWithoutBaseManager(telemetry, injected)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(Url(20066))).ConfigureAwait(false);

            // The override mutated the candidate in place and omitted base; the
            // replacement endpoints must still be honoured.
            Assert.That(harness.OpenedUrls(), Does.Contain(injected));
            Assert.That(harness.OpenedUrls(), Does.Not.Contain(Url(20066)));
        }

        [Test]
        public async Task ProviderSuppressesByReturningEmptyConfiguration()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = new EmptyingProvider()
            };

            await manager.StartServiceAsync(ConfigFor(Url(20067))).ConfigureAwait(false);

            // Explicit suppression path: the provider dropped all endpoints so
            // no listener opens, yet the manager reaches the Started state.
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
        }

        [Test]
        public async Task LegacyOverrideMutatingCandidateIsApplied()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri injected = Url(20070);
            await using var manager = new MutatingManager(telemetry, injected)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(Url(20071))).ConfigureAwait(false);

            // The override replaced the endpoints with the injected URL.
            Assert.That(harness.OpenedUrls(), Does.Contain(injected));
            Assert.That(harness.OpenedUrls(), Does.Not.Contain(Url(20071)));
        }

        [Test]
        public void LegacyOverrideRejectingCandidatePropagates()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            using var manager = new RejectingManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.StartServiceAsync(ConfigFor(Url(20080))))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task ProviderRunsAfterLegacyAdaptation()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri injected = Url(20090);
            var provider = new RecordingProvider();
            await using var manager = new MutatingManager(telemetry, injected)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            await manager.StartServiceAsync(ConfigFor(Url(20091))).ConfigureAwait(false);

            // The provider must observe the legacy-mutated candidate (injected URL).
            Assert.That(provider.LastInput, Is.Not.Null);
            bool providerSawInjected = false;
            foreach (ReverseConnectClientEndpoint endpoint in provider.LastInput!.ClientEndpoints)
            {
                if (endpoint.EndpointUrl == injected.ToString())
                {
                    providerSawInjected = true;
                }
            }
            Assert.That(providerSawInjected, Is.True);
        }

        [Test]
        public async Task EnsureStartedAsyncIsIdempotent()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, Url(20100)));

            await manager.EnsureStartedAsync().ConfigureAwait(false);
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            int openedCount = harness.Listeners.Count(l => l.OpenCount > 0);
            Assert.That(openedCount, Is.EqualTo(1));
        }

        [Test]
        public void SyncWrapperStartServiceBridgesToAsyncLifecycle()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            manager.StartService(ConfigFor(Url(20110)));

            Assert.That(harness.Listeners[0].OpenCount, Is.EqualTo(1));
        }

        [Test]
        public async Task OverlappingConcurrentStartSecondRejectsWhilePreparing()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            Uri first = Url(20120);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            // The first start reserves the lifecycle and blocks inside the
            // provider (state == Preparing).
            Task firstStart = manager.StartServiceAsync(ConfigFor(first));
            await provider.Entered.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Preparing"));

            // A truly overlapping second start must reject immediately.
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.StartServiceAsync(ConfigFor(Url(20121))))!;
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            release.SetResult(true);
            await firstStart.ConfigureAwait(false);
            Assert.That(harness.Listeners.Any(l => l.OpenedUrl == first && l.IsOpen), Is.True);
        }

        [Test]
        public async Task StopDuringPrepareSupersedesPendingStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            Uri url = Url(20130);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            Task start = manager.StartServiceAsync(ConfigFor(url));
            await provider.Entered.ConfigureAwait(false);

            // Stop while the start is still preparing.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            release.SetResult(true);
            // A stop now cancels the in-flight explicit start's token, so a
            // non-cooperative provider's start surfaces the cancellation once it
            // returns (the post-provider recheck); a start that instead reaches
            // the gate is superseded with BadInvalidState. Either way it never
            // binds a listener.
            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<ServiceResultException>());

            // The pending start never bound a listener and the candidate was
            // cleaned up (no leaked open listener).
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task DisposeDuringPrepareRejectsPendingStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            Task start = manager.StartServiceAsync(ConfigFor(Url(20140)));
            await provider.Entered.ConfigureAwait(false);

            await manager.DisposeAsync().ConfigureAwait(false);

            release.SetResult(true);
            // A dispose now cancels the in-flight explicit start's token, so a
            // non-cooperative provider's start surfaces the cancellation once it
            // returns (the post-provider recheck); a start that instead reaches
            // the gate is rejected as disposed. Either way it never binds.
            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<ObjectDisposedException>()
                    .Or.InstanceOf<OperationCanceledException>());
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task ConcurrentDisposeAsyncShareSameTeardown()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(Url(20150))).ConfigureAwait(false);

            Task first = manager.DisposeAsync().AsTask();
            Task second = manager.DisposeAsync().AsTask();
            await Task.WhenAll(first, second).ConfigureAwait(false);

            // Teardown ran exactly once even though two callers disposed: the
            // single live listener was closed exactly once.
            Assert.That(harness.Listeners, Has.Count.EqualTo(1));
            Assert.That(harness.Listeners[0].CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedConcurrentLazyStartupPropagatesToAllCallers()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20160);
            harness.FailOpenFor(url, new ServiceResultException(StatusCodes.BadNoCommunication));
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            Task firstWait = manager.EnsureStartedAsync();
            Task secondWait = manager.EnsureStartedAsync();

            // Both first-use callers observe the same real failure (it is not
            // swallowed) because the shared startup task faulted.
            ServiceResultException firstError = Assert.ThrowsAsync<ServiceResultException>(
                () => firstWait)!;
            ServiceResultException secondError = Assert.ThrowsAsync<ServiceResultException>(
                () => secondWait)!;
            Assert.That(firstError.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(secondError.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
        }

        [Test]
        public async Task InitialConfigurationFactoryThrowFaultsAndAllowsRetry()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20168);
            int calls = 0;
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("factory boom");
                }
                return Task.FromResult(BuildAppConfig(telemetry, url));
            });

            // The factory throws after ReserveStartLocked reserved the owned
            // Preparing state, so the failure must be finalized to the
            // retryable Faulted state (not stranded in Preparing) and mapped to
            // a ServiceResultException.
            Assert.ThrowsAsync<ServiceResultException>(
                () => manager.EnsureStartedAsync());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Faulted"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);

            // A retry from Faulted re-runs the factory and starts the listener.
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task GatedStartFailureIsSharedByConcurrentEnsureStartedWithoutOverlappingRetry()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20172);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // The failure-finalization hook holds the failing start AFTER
            // FaultIfOwned has set Faulted but BEFORE the tracked task publishes
            // its exception, opening the exact window issue 1 targets.
            var faultGateEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFault = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            manager.StartFailureFinalizationHookForTest = async () =>
            {
                faultGateEntered.TrySetResult(true);
                await releaseFault.Task.ConfigureAwait(false);
            };

            int factoryCalls = 0;
            bool succeedNext = false;
            manager.ConfigureInitialStartup(async ct =>
            {
                int call = Interlocked.Increment(ref factoryCalls);
                await Task.Yield();
                if (!succeedNext)
                {
                    throw new InvalidOperationException(
                        "factory boom " + call.ToString(CultureInfo.InvariantCulture));
                }
                return BuildAppConfig(telemetry, url);
            });

            // First start: the factory throws, the state faults, and the task
            // parks inside the hook without completing.
            Task first = manager.EnsureStartedAsync();
            await faultGateEntered.Task.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Faulted"));

            // A concurrent EnsureStartedAsync must await the SAME failing task
            // rather than return prematurely or reserve an overlapping retry.
            Task concurrent = manager.EnsureStartedAsync();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(concurrent.IsCompleted, Is.False,
                "concurrent start must await the in-flight failing task");
            Assert.That(factoryCalls, Is.EqualTo(1),
                "no overlapping retry may run while the failing task is incomplete");

            // Release the failure; both awaiters observe the same failure.
            releaseFault.TrySetResult(true);

            Exception? firstError = null;
            Exception? concurrentError = null;
            try
            {
                await first.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                firstError = ex;
            }
            try
            {
                await concurrent.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                concurrentError = ex;
            }

            Assert.That(firstError, Is.InstanceOf<ServiceResultException>());
            Assert.That(concurrentError, Is.InstanceOf<ServiceResultException>());
            Assert.That(factoryCalls, Is.EqualTo(1),
                "the shared failure must not have triggered a second attempt");

            // A fresh retry is only allowed AFTER the failing task completed.
            manager.StartFailureFinalizationHookForTest = null;
            succeedNext = true;
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task DeferredStartupReentryAfterParentCompletesIsNotFalselyRejected()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20173);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Exception? immediateError = null;
            var parentStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task? deferred = null;
            manager.ConfigureInitialStartup(async ct =>
            {
                // An immediate synchronous re-entry on the owning startup flow
                // must still fail fast (scope Active == true).
                try
                {
                    await Task.Yield();
                    await manager.EnsureStartedAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    immediateError = ex;
                }

                // A child task that captures the startup scope but only runs
                // AFTER the originating startup flow has returned must observe
                // the scope as inactive and NOT be falsely rejected.
                deferred = Task.Run(async () =>
                {
                    await parentStarted.Task.ConfigureAwait(false);
                    await manager.EnsureStartedAsync().ConfigureAwait(false);
                }, CancellationToken.None);

                return BuildAppConfig(telemetry, url);
            });

            await manager.EnsureStartedAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            // Immediate reentry failed fast during startup.
            Assert.That(immediateError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)immediateError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));

            // Release the deferred reentry now that the parent flow finished; it
            // must complete successfully rather than throw a false re-entry.
            parentStarted.TrySetResult(true);
            Assert.That(deferred, Is.Not.Null);
            Assert.That(
                async () => await deferred!.ConfigureAwait(false),
                Throws.Nothing);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
        }

        [Test]
        public async Task InitialConfigurationFactoryCancellationFaultsAndAllowsRetry()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20169);
            int calls = 0;
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new OperationCanceledException();
                }
                return Task.FromResult(BuildAppConfig(telemetry, url));
            });

            // A factory cancellation after ReserveStartLocked must finalize the
            // owned Preparing state to Faulted so a later start can retry; the
            // OperationCanceledException is surfaced unmapped.
            Assert.ThrowsAsync<OperationCanceledException>(
                () => manager.EnsureStartedAsync());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Faulted"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);

            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task ExplicitStartIsAwaitedByConcurrentEnsureStarted()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            Uri url = Url(20260);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            // An explicit start reserves the lifecycle and blocks in the
            // provider (state == Preparing).
            Task explicitStart = manager.StartServiceAsync(ConfigFor(url));
            await provider.Entered.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Preparing"));

            // A concurrent EnsureStartedAsync must await the in-flight explicit
            // start rather than returning while only Preparing.
            Task ensure = manager.EnsureStartedAsync();
            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(
                ensure.IsCompleted,
                Is.False,
                "EnsureStartedAsync must await the in-flight explicit start");

            release.SetResult(true);
            await explicitStart.ConfigureAwait(false);
            await ensure.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task ExplicitStartFailurePropagatesToConcurrentEnsureStarted()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20261);
            harness.FailOpenFor(url, new ServiceResultException(StatusCodes.BadNoCommunication));
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            Task explicitStart = manager.StartServiceAsync(ConfigFor(url));
            await provider.Entered.ConfigureAwait(false);

            // A concurrent EnsureStartedAsync tracks the same explicit start.
            Task ensure = manager.EnsureStartedAsync();

            release.SetResult(true);

            // The explicit start fails to bind and both callers observe the
            // same real failure (it is not swallowed).
            ServiceResultException fromStart = Assert.ThrowsAsync<ServiceResultException>(
                () => explicitStart)!;
            ServiceResultException fromEnsure = Assert.ThrowsAsync<ServiceResultException>(
                () => ensure)!;
            Assert.That(fromStart.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(fromEnsure.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
        }

        [Test]
        public async Task StopDefersStartTokenDisposalSoProviderCanRegister()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new TokenRegisteringProvider(release.Task);
            Uri url = Url(20270);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The lazy start runs the provider under the manager-owned token.
            Task lazyStart = manager.EnsureStartedAsync();
            await provider.Entered.ConfigureAwait(false);

            // Stop cancels the manager-owned start token. Its disposal must be
            // deferred until the start task completes so the provider (still
            // running) can register on the cancelled - but not disposed - token.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            release.SetResult(true);
            Exception? error = Assert.CatchAsync(
                async () => await lazyStart.ConfigureAwait(false));

            Assert.That(
                provider.RegistrationError,
                Is.Null,
                "the provider must not observe a disposed start token");
            Assert.That(error, Is.Not.InstanceOf<ObjectDisposedException>());
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task DisposeDefersStartTokenDisposalSoProviderCanRegister()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new TokenRegisteringProvider(release.Task);
            Uri url = Url(20271);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            Task lazyStart = manager.EnsureStartedAsync();
            await provider.Entered.ConfigureAwait(false);

            // Dispose cancels the manager-owned start token but must drain the
            // start (which is still running the provider) before disposing the
            // token source, so the provider never registers on a disposed token.
            Task dispose = manager.DisposeAsync().AsTask();
            release.SetResult(true);
            await dispose.ConfigureAwait(false);

            Exception? error = Assert.CatchAsync(
                async () => await lazyStart.ConfigureAwait(false));

            Assert.That(
                provider.RegistrationError,
                Is.Null,
                "the provider must not observe a disposed start token");
            Assert.That(error, Is.Not.InstanceOf<ObjectDisposedException>());
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public void WaitForConnectionAsyncNullEndpointThrowsWithoutStarting()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new RecordingProvider();
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, Url(20280)));

            // The null endpoint is rejected before any lazy startup side
            // effect: the configuration provider is never invoked and no
            // listener is bound.
            Assert.ThrowsAsync<ArgumentNullException>(
                () => manager.WaitForConnectionAsync(null!, null));

            Assert.That(provider.LastInput, Is.Null);
            Assert.That(harness.Listeners.Any(l => l.OpenCount > 0), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));
        }

        [Test]
        public async Task CandidateApplicationConfigurationIsolatedUntilActivation()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20170);
            Uri replacement = Url(20171);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await using (manager.ConfigureAwait(false))
            {
                ApplicationConfiguration configA = BuildAppConfig(telemetry, original);
                await manager.StartServiceAsync(configA).ConfigureAwait(false);
                Assert.That(
                    manager.ActiveApplicationConfigurationForTest,
                    Is.SameAs(configA));

                var probe = new ActiveConfigProbingProvider(manager);
                manager.ConfigurationProvider = probe;

                ApplicationConfiguration configB = BuildAppConfig(telemetry, replacement);
                await manager.ReloadConfigurationAsync(
                    configB,
                    manager.CurrentWatcherGeneration).ConfigureAwait(false);

                // While the candidate (configB) was being prepared the active
                // application configuration must still have been configA.
                Assert.That(probe.ObservedDuringPrepare, Is.SameAs(configA));
                // After a successful activation it is promoted to configB.
                Assert.That(
                    manager.ActiveApplicationConfigurationForTest,
                    Is.SameAs(configB));
            }
        }

        [Test]
        public async Task ConfiguredManualUriCollisionReusesManualNoLeak()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri shared = Url(20180);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.AddEndpoint(shared);

            // The configured set repeats the manual endpoint URL.
            await manager.StartServiceAsync(ConfigFor(shared)).ConfigureAwait(false);

            // Exactly one host/listener exists for the shared URL (the manual
            // one is reused; no duplicate configured host is created or leaked).
            Assert.That(harness.Listeners, Has.Count.EqualTo(1));
            Assert.That(harness.Listeners[0].OpenedUrl, Is.EqualTo(shared));
            Assert.That(harness.Listeners[0].IsOpen, Is.True);
        }

        [Test]
        public async Task PreparationFailureOnRunningServiceDoesNotFaultLiveListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri live = Url(20190);
            var provider = new ToggleThrowingProvider();
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, live)).ConfigureAwait(false);
            Assert.That(harness.Listeners.Any(l => l.OpenedUrl == live && l.IsOpen), Is.True);

            // A reload whose preparation fails must leave the running service
            // intact — the live listener stays open and the state stays Started.
            provider.Throw = true;
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, Url(20191)),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            Assert.That(harness.Listeners.Any(l => l.OpenedUrl == live && l.IsOpen), Is.True);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
        }

        [Test]
        public async Task ConfigurationChangeDuringPrepareSchedulesReload()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            var reloadObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            string path = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "rcm_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".cfg");
            System.IO.File.WriteAllText(path, "seed");
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));

            var manager = new ReloadObservingManager(telemetry, reloadObserved)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            try
            {
                ApplicationConfiguration appConfig = BuildAppConfig(telemetry, Url(20200));
                typeof(ApplicationConfiguration)
                    .GetProperty(nameof(ApplicationConfiguration.SourceFilePath))!
                    .SetValue(appConfig, path);

                Task start = manager.StartServiceAsync(appConfig);
                await provider.Entered.ConfigureAwait(false);

                // Simulate the configuration file changing between preparation
                // (write time already captured) and commit.
                System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
                release.SetResult(true);
                await start.ConfigureAwait(false);

                // The change is detected at commit and a reload is scheduled
                // through the watcher-changed seam.
                bool reloaded = await reloadObserved.Task
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(reloaded, Is.True);
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public async Task StopBeforeLazyProviderBeginsSupersedesLazyStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            Uri url = Url(20210);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The lazy first-use start reserves the lifecycle version
            // synchronously, then defers provider preparation.
            Task lazyStart = manager.EnsureStartedAsync();

            // Stop supersedes the reserved start before it can bind, even if
            // the provider has not begun.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            // Release a possibly-entered provider; the superseded start must
            // never open a listener.
            release.TrySetResult(true);
            Exception? error = Assert.CatchAsync(
                async () => await lazyStart.ConfigureAwait(false));
            Assert.That(
                error,
                Is.InstanceOf<ServiceResultException>()
                    .Or.InstanceOf<OperationCanceledException>());
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task HostedStartCancellationAbortsUnderlyingStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new CancellableGateProvider();
            Uri url = Url(20220);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));
            var hosted = new ReverseConnectManagerHostedService(manager);

            using var cts = new CancellationTokenSource();
            Task startAsync = hosted.StartAsync(cts.Token);
            await provider.Entered.ConfigureAwait(false);

            // Cancelling the host startup token aborts the underlying start,
            // not merely this awaiter.
            cts.Cancel();

            Assert.That(
                async () => await startAsync.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            // The provider observed cancellation: no listener was ever bound
            // and the start was aborted rather than committed, so no later
            // bind occurs.
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));
        }

        [Test]
        public async Task IndividualLazyWaiterCancellationDoesNotAbortSharedStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            Uri url = Url(20230);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            using var cancelFirst = new CancellationTokenSource();
            Task firstWaiter = manager.EnsureStartedAsync(cancelFirst.Token);
            Task secondWaiter = manager.EnsureStartedAsync();
            await provider.Entered.ConfigureAwait(false);

            // Cancelling the first waiter cancels only its wait; the shared
            // start continues so the second waiter still observes success.
            cancelFirst.Cancel();
            Assert.That(
                async () => await firstWaiter.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            release.SetResult(true);
            await secondWaiter.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task WatcherConstructionFailureRestoresPreviousActiveListener()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri liveUrl = Url(20240);
            Uri newUrl = Url(20241);
            string fileA = NewConfigFile();
            string fileB = NewConfigFile();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var reloadProvider = new GatedProvider(release.Task);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                // Initial start creates a watcher on fileA and opens liveUrl.
                await manager.StartServiceAsync(
                    BuildAppConfigWithFile(telemetry, fileA, liveUrl)).ConfigureAwait(false);
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == liveUrl && l.IsOpen),
                    Is.True);
                int watcherGeneration = manager.CurrentWatcherGeneration;

                // Reload targets fileB/newUrl. Gate preparation so the source
                // file can be removed after validation but before the watcher
                // is constructed at commit.
                manager.ConfigurationProvider = reloadProvider;
                Task reload = manager.ReloadConfigurationAsync(
                    BuildAppConfigWithFile(telemetry, fileB, newUrl),
                    watcherGeneration);
                await reloadProvider.Entered.ConfigureAwait(false);

                System.IO.File.Delete(fileB);
                release.SetResult(true);
                await reload.ConfigureAwait(false);

                // Watcher construction failed inside the transactional scope:
                // the candidate listener is closed and the previous listener,
                // state and (untouched) watcher are restored.
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == liveUrl && l.IsOpen),
                    Is.True);
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == newUrl && l.IsOpen),
                    Is.False);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    manager.CurrentWatcherGeneration,
                    Is.EqualTo(watcherGeneration));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
                System.IO.File.Delete(fileA);
                if (System.IO.File.Exists(fileB))
                {
                    System.IO.File.Delete(fileB);
                }
            }
        }

        [Test]
        public async Task LegacyDisposeBoolOverrideInvokedOnceByDisposeAsync()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var manager = new LegacyDisposingManager(telemetry);

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(manager.DisposeBoolCount, Is.EqualTo(1));
            Assert.That(manager.LastDisposing, Is.True);
        }

        [Test]
        public void LegacyDisposeBoolOverrideInvokedOnceByDispose()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var manager = new LegacyDisposingManager(telemetry);

            manager.Dispose();

            Assert.That(manager.DisposeBoolCount, Is.EqualTo(1));
            Assert.That(manager.LastDisposing, Is.True);
        }

        [Test]
        public async Task StopDuringListenerOpenCancelsOpenAndPreventsCommit()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20300);
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.SetOpenGate(url, ct =>
            {
                openEntered.TrySetResult(true);
                // Block until the manager-owned operation token is cancelled.
                return Task.Delay(Timeout.Infinite, ct);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // Start acquires the lifecycle gate and blocks inside listener open.
            Task start = manager.StartServiceAsync(ConfigFor(url));
            await openEntered.Task.ConfigureAwait(false);

            // Stop must cancel the blocked open BEFORE waiting on the gate, so
            // it can acquire the gate and complete; the superseded start must
            // never commit.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<ServiceResultException>());
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task DisposeDuringListenerOpenCancelsOpenAndPreventsCommit()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20305);
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.SetOpenGate(url, ct =>
            {
                openEntered.TrySetResult(true);
                return Task.Delay(Timeout.Infinite, ct);
            });
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Task start = manager.StartServiceAsync(ConfigFor(url));
            await openEntered.Task.ConfigureAwait(false);

            // Dispose must cancel the blocked open BEFORE waiting on the gate,
            // complete teardown, and the superseded start must never commit.
            await manager.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));

            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<ServiceResultException>()
                    .Or.InstanceOf<ObjectDisposedException>());
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task HostedStartWithAlreadyCancelledTokenNeverBinds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new RecordingProvider();
            Uri url = Url(20310);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));
            var hosted = new ReverseConnectManagerHostedService(manager);

            using var cts = new CancellationTokenSource();
            // The host startup token is cancelled BEFORE StartAsync creates the
            // shared start; the latched cancellation must still abort the start.
            cts.Cancel();

            Assert.That(
                async () => await hosted.StartAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(
                provider.LastInput,
                Is.Null,
                "the provider must never run when the hosted start is pre-cancelled");
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));
        }

        [Test]
        public void SyncRegisterWaitingConnectionStartsConfiguredManager()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20320);
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            // DI-lazy scenario: an initial startup is configured but deferred.
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The obsolete synchronous registration must start the configured
            // manager (opening the fake listener) before registering.
            int hashCode = manager.RegisterWaitingConnection(
                url,
                null,
                (sender, e) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once);

            Assert.That(hashCode, Is.Not.Zero);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True,
                "the configured listener must be opened by the sync registration");
        }

        [Test]
        public async Task SyncRegisterWaitingConnectionStopAfterStartRejectsWithoutInertRegistration()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20321);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            var reachedWindow = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            manager.BeforeRegisterWaitForTest = async () =>
            {
                reachedWindow.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };

            Task<int> register = Task.Run(() => manager.RegisterWaitingConnection(
                url,
                null,
                (sender, e) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once));
            await reachedWindow.Task.ConfigureAwait(false);

            await manager.StopServiceAsync().ConfigureAwait(false);
            release.SetResult(true);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => register)!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "no inert synchronous registration must survive a committed stop");
        }

        [Test]
        public void SyncRegisterWaitingConnectionOnUnconfiguredManagerIsRegistrationOnly()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // No initial startup configured: registration must not start the
            // manager (direct/manual managers remain registration-only).
            int hashCode = manager.RegisterWaitingConnection(
                Url(20325),
                null,
                (sender, e) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once);

            Assert.That(hashCode, Is.Not.Zero);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("New"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task FailedReloadOfPreviouslyStartedEmptyConfigStaysStarted()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri failing = Url(20330);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // Start with an empty configuration: Started with zero listeners.
            await manager.StartServiceAsync(ConfigFor()).ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);

            // A reload that fails to open its new listener must restore the
            // previous (empty) Started state, not fault, because the previous
            // lifecycle state is snapshotted independently of the descriptor
            // count.
            harness.FailOpenFor(
                failing,
                new ServiceResultException(StatusCodes.BadNoCommunication));
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, failing),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == failing && l.IsOpen),
                Is.False);
        }

        [Test]
        public async Task CanceledStartWithNonCooperativeProviderCanBeRetried()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new CancellationIgnoringProvider(release.Task);
            Uri url = Url(20340);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            using var cts = new CancellationTokenSource();
            Task start = manager.StartServiceAsync(ConfigFor(url), cts.Token);

            // The provider ignores cancellation and returns normally after the
            // token has been cancelled.
            await provider.Entered.ConfigureAwait(false);
            cts.Cancel();
            release.SetResult(true);

            // The start observes cancellation immediately after the (non-
            // cooperative) provider returns, so it must not strand the manager
            // in the Preparing state.
            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Preparing"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);

            // The cancelled start left a retryable state, so a fresh start
            // succeeds and opens the configured listener.
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task PreCanceledStopDoesNotAbortSharedStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);
            Uri url = Url(20345);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // A shared lazy start reserves the lifecycle version and begins
            // provider preparation.
            Task sharedStart = manager.EnsureStartedAsync();
            await provider.Entered.ConfigureAwait(false);

            // A pre-cancelled stop must throw WITHOUT aborting the pending
            // shared start (it must not cancel the start's token and then
            // abandon the stop).
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.That(
                async () => await manager.StopServiceAsync(canceled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            // Release the provider; the untouched shared start continues and
            // succeeds.
            release.SetResult(true);
            await sharedStart.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task ReloadCancellationDuringReplacementRestoresPreviousListener()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20400);
            Uri replacement = Url(20401);
            using var cts = new CancellationTokenSource();

            // The replacement listener opens successfully but cancels the caller
            // token as a side effect, so the open does NOT itself observe the
            // token: the commit-time recheck is the first place the cancellation
            // is seen. A commit-time cancellation must roll back through the same
            // transactional restore path as an open failure.
            harness.SetOpenGate(replacement, _ =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True);

            // The reload is cancelled at commit; ReloadConfigurationAsync
            // swallows the cancellation after the transactional restore runs.
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration,
                cts.Token).ConfigureAwait(false);

            // The previously active original listener must be reopened and the
            // manager must be Started again; the cancelled replacement must not
            // be left open.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True,
                "the previously active listener must be reopened after a commit-time cancellation");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.False);
        }

        [Test]
        public async Task ManualEndpointCanceledOpenIsCleanedAndRetryable()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri manual = Url(20410);
            using var cts = new CancellationTokenSource();
            int armed = 1;

            // The manual endpoint's open is cancelled once (cooperatively). A
            // manual host is reused by identity and is NOT candidate-owned, so
            // it must still be cleaned (closed) non-cancellably on the cancelled
            // open rather than left partially initialized.
            harness.SetOpenGate(manual, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    cts.Cancel();
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });

            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                manager.AddEndpoint(manual);

                Assert.That(
                    async () => await manager
                        .StartServiceAsync(ConfigFor(), cts.Token)
                        .ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());

                FakeReverseListener manualListener =
                    harness.Listeners.Single(l => l.OpenedUrl == manual);
                Assert.That(
                    manualListener.CloseCount,
                    Is.GreaterThanOrEqualTo(1),
                    "the attempted manual host must be closed non-cancellably");
                Assert.That(manualListener.IsOpen, Is.False);

                // Retry safety: a fresh start reuses and reopens the manual host.
                await manager.StartServiceAsync(ConfigFor()).ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == manual && l.IsOpen),
                    Is.True);
            }
            finally
            {
                // Dispose safety: closes the reused manual host without throwing.
                await manager.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task CancelableStopQueuedBehindCooperativeStartDoesNotStrandIt()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20420);
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var startRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // A cooperative listener open: it blocks until released and honors
            // the manager-owned operation token, so an abort would cancel it.
            harness.SetOpenGate(url, async ct =>
            {
                openEntered.TrySetResult(true);
                await Task.WhenAny(startRelease.Task, Task.Delay(Timeout.Infinite, ct))
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // The start acquires the gate and blocks inside the cooperative open.
            Task start = manager.StartServiceAsync(ConfigFor(url));
            await openEntered.Task.ConfigureAwait(false);

            // A cancellable stop queued behind the gate is cancelled while
            // queued. It must throw OCE WITHOUT aborting the in-flight start.
            using var cts = new CancellationTokenSource();
            Task queuedStop = manager.StopServiceAsync(cts.Token);
            cts.Cancel();
            Assert.That(
                async () => await queuedStop.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            // The start was not stranded: releasing it lets it complete
            // successfully and reach Started (its operation token was never
            // cancelled by the abandoned stop).
            startRelease.SetResult(true);
            await start.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
            Assert.That(
                harness.Listeners.Single(l => l.OpenedUrl == url).OpenCount,
                Is.EqualTo(1));
        }

        [Test]
        public async Task StopCloseCancellationRestoresStartedThenStopSucceeds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20430);
            using var cts = new CancellationTokenSource();
            int armed = 1;

            // The first listener close is slow and cancelled (the caller token
            // is cancelled while the close is in flight); subsequent closes
            // complete immediately.
            harness.SetCloseGate(url, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    cts.Cancel();
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);
            Assert.That(listener.IsOpen, Is.True);

            // The cancelled close must reconstruct a coherent, retryable Started
            // state and surface the cancellation rather than silently discarding
            // the token and leaving the listener half-closed.
            Assert.That(
                async () => await manager.StopServiceAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(listener.IsOpen, Is.True);

            // A subsequent (non-cancellable) stop completes cleanly.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task RegistrationAndWaitAfterDisposalThrowWithoutMutating()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            Uri url = Url(20350);

            await manager.DisposeAsync().ConfigureAwait(false);

            // The first post-disposal async registration is rejected.
            Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.RegisterWaitingConnectionAsync(
                    url,
                    null,
                    (sender, e) => { },
                    ReverseConnectManager.ReverseConnectStrategy.Once));

            // A repeated post-disposal async registration is also rejected.
            Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.RegisterWaitingConnectionAsync(
                    url,
                    null,
                    (sender, e) => { },
                    ReverseConnectManager.ReverseConnectStrategy.Once));

            // The obsolete synchronous overload is rejected too.
            Assert.Throws<ObjectDisposedException>(
                () => manager.RegisterWaitingConnection(
                    url,
                    null,
                    (sender, e) => { },
                    ReverseConnectManager.ReverseConnectStrategy.Once));

            // WaitForConnectionAsync must also reject after disposal.
            Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.WaitForConnectionAsync(url, null));
        }

        [Test]
        public async Task ConcurrentRegistrationDuringDisposalNeverCorrupts()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            Uri url = Url(20355);

            var gate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = new List<Task>();
            for (int i = 0; i < 32; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await gate.Task.ConfigureAwait(false);
                    try
                    {
                        await manager.RegisterWaitingConnectionAsync(
                            url,
                            null,
                            (sender, e) => { },
                            ReverseConnectManager.ReverseConnectStrategy.Once)
                            .ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The only acceptable failure once disposal has begun;
                        // any other exception indicates a corrupted teardown.
                    }
                }));
            }
            Task dispose = Task.Run(async () =>
            {
                await gate.Task.ConfigureAwait(false);
                await manager.DisposeAsync().ConfigureAwait(false);
            });

            gate.SetResult(true);
            await Task.WhenAll(tasks.Append(dispose)).ConfigureAwait(false);

            // After disposal completes every registration is rejected.
            Assert.ThrowsAsync<ObjectDisposedException>(
                () => manager.RegisterWaitingConnectionAsync(
                    url,
                    null,
                    (sender, e) => { },
                    ReverseConnectManager.ReverseConnectStrategy.Once));
        }

        [Test]
        public async Task StopDuringReloadOpenDoesNotReopenPreviousListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20440);
            Uri replacement = Url(20441);
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // The replacement open blocks and honors the manager-owned operation
            // token, so a Stop can abort it.
            harness.SetOpenGate(replacement, ct =>
            {
                openEntered.TrySetResult(true);
                return Task.Delay(Timeout.Infinite, ct);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True);

            // The reload closes the original (previous) listener, then blocks
            // opening the replacement.
            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);
            await openEntered.Task.ConfigureAwait(false);

            // A shutdown supersedes the in-flight reload while its candidate open
            // is blocked. The previous listener was already closed, so the
            // superseded activation must NOT restore/reopen it: the shutdown owns
            // the lifecycle and proceeds to Stopped.
            await manager.StopServiceAsync().ConfigureAwait(false);
            await reload.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            // Exactly one listener was ever bound to the original URL: a restore
            // would have created and opened a second one.
            Assert.That(
                harness.Listeners.Count(l => l.OpenedUrl == original),
                Is.EqualTo(1),
                "the previous listener must not be reopened after a shutdown supersession");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.False);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.False);
        }

        [Test]
        public async Task DisposeDuringReloadOpenDoesNotReopenPreviousListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20445);
            Uri replacement = Url(20446);
            var openEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            harness.SetOpenGate(replacement, ct =>
            {
                openEntered.TrySetResult(true);
                return Task.Delay(Timeout.Infinite, ct);
            });
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);

            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);
            await openEntered.Task.ConfigureAwait(false);

            // Dispose supersedes the in-flight reload; the previously closed
            // listener must not be non-cancellably reopened during teardown.
            await manager.DisposeAsync().ConfigureAwait(false);
            await reload.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(
                harness.Listeners.Count(l => l.OpenedUrl == original),
                Is.EqualTo(1),
                "the previous listener must not be reopened after a dispose supersession");
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task LazyWaitDuringReloadStoppingWindowDoesNotSupersede()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20450);
            Uri replacement = Url(20451);
            var closeEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var closeRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // The reload's close of the previous (original) listener blocks, so
            // the manager sits in the Stopping window a reload passes through
            // while a lazy Wait races it.
            harness.SetCloseGate(original, async _ =>
            {
                closeEntered.TrySetResult(true);
                await closeRelease.Task.ConfigureAwait(false);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, original));

            // Lazy first-use start populates the shared start task and reaches
            // Started with the original listener.
            await manager.EnsureStartedAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);
            await closeEntered.Task.ConfigureAwait(false);

            // A lazy Wait racing the in-flight reload (state Stopping) must never
            // reserve a new start and supersede it. Instead of returning premature
            // success it tracks the reload's completion, so it stays pending while
            // the reload is blocked and completes without throwing once the reload
            // restores a coherent Started state.
            Task lazyWait = manager.EnsureStartedAsync();
            Assert.That(
                lazyWait.IsCompleted,
                Is.False,
                "the lazy wait must track the in-flight reload rather than " +
                "returning premature success");

            // Release the reload: it must commit the replacement, proving the
            // lazy Wait never superseded it (a supersession would have rolled the
            // reload back and restored/started the original instead). Awaiting
            // both directly (rather than a blocking Throws.Nothing assertion)
            // surfaces any fault while staying non-blocking.
            closeRelease.SetResult(true);
            await reload.ConfigureAwait(false);
            await lazyWait.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.True,
                "the reload must commit the replacement listener, not be superseded");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.False);
        }

        [Test]
        public async Task CanceledStopKeepsRegistrationThenSuccessfulStopClears()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20460);
            using var cts = new CancellationTokenSource();
            int armed = 1;

            // The first listener close is slow and cancelled (the caller token is
            // cancelled while the close is in flight); later closes complete.
            harness.SetCloseGate(url, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    cts.Cancel();
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Register a waiting connection while the manager is Started.
            int hashCode = await manager.RegisterWaitingConnectionAsync(
                url,
                null,
                (sender, e) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once)
                .ConfigureAwait(false);
            Assert.That(manager.WaitingConnectionCountForTest, Is.EqualTo(1));

            // The cancelled stop must roll back to Started WITHOUT clearing the
            // registration: the close never committed, so waiters must survive.
            Assert.That(
                async () => await manager.StopServiceAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.EqualTo(1),
                "a rolled-back stop must leave the registration intact");

            // The registration still matches and can be found/removed.
            manager.UnregisterWaitingConnection(hashCode);
            Assert.That(manager.WaitingConnectionCountForTest, Is.Zero);

            // Re-register, then a successful (non-cancellable) stop clears it.
            await manager.RegisterWaitingConnectionAsync(
                url,
                null,
                (sender, e) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once)
                .ConfigureAwait(false);
            Assert.That(manager.WaitingConnectionCountForTest, Is.EqualTo(1));

            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "a committed stop must clear the registrations");
        }

        [Test]
        public async Task ProviderReentrantEnsureStartedFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20500);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            var provider = new ReentrantStartProvider(manager);
            manager.ConfigurationProvider = provider;
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The shared startup runs the provider during preparation. The
            // provider re-enters EnsureStartedAsync on the same in-flight
            // startup: it must fail fast (BadInvalidState) instead of awaiting
            // this start's own shared task (which would deadlock).
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(provider.ReentrantAttempts, Is.EqualTo(1));
            Assert.That(provider.ReentrantError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)provider.ReentrantError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            // The real startup still completed and bound the listener.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task ProviderReentrantAsyncRegistrationFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20505);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            var provider = new ReentrantStartProvider(manager, indirectRegistration: true);
            manager.ConfigurationProvider = provider;
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The provider re-enters via RegisterWaitingConnectionAsync (which
            // internally calls EnsureStartedAsync) across an await boundary. The
            // AsyncLocal owner marker flows across the yield, so the indirect
            // re-entrant start must also fail fast.
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(provider.ReentrantError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)provider.ReentrantError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task LegacyHookReentrantEnsureStartedFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20510);
            await using var manager = new ReentrantLegacyHookManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The legacy OnUpdateConfiguration hook runs during preparation and
            // re-enters EnsureStartedAsync on the same in-flight shared startup;
            // it must fail fast rather than block on this start's own task.
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(manager.ReentrantError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)manager.ReentrantError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task NestedUnrelatedManagerStartsWhileOuterStartupInFlight()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var outerHarness = new FakeListenerHarness(Scheme);
            var innerHarness = new FakeListenerHarness(Scheme);
            Uri outerUrl = Url(20515);
            Uri innerUrl = Url(20516);

            await using var inner = new ReverseConnectManager(telemetry)
            {
                TransportBindings = innerHarness.Registry
            };
            inner.ConfigureInitialStartup(BuildAppConfig(telemetry, innerUrl));

            await using var outer = new ReverseConnectManager(telemetry)
            {
                TransportBindings = outerHarness.Registry
            };
            var provider = new NestedManagerStartingProvider(inner);
            outer.ConfigurationProvider = provider;
            outer.ConfigureInitialStartup(BuildAppConfig(telemetry, outerUrl));

            // The outer manager's provider starts a DIFFERENT manager instance
            // during the outer's preparation. The owner marker is per-instance,
            // so the inner manager's EnsureStartedAsync is NOT blocked by the
            // outer's in-flight startup.
            await outer.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(provider.NestedError, Is.Null);
            Assert.That(provider.NestedStarted, Is.True);
            Assert.That(outer.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(inner.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                outerHarness.Listeners.Any(l => l.OpenedUrl == outerUrl && l.IsOpen),
                Is.True);
            Assert.That(
                innerHarness.Listeners.Any(l => l.OpenedUrl == innerUrl && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task InitialConfigurationFactoryReentrantEnsureStartedFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20560);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Exception? reentrantError = null;
            int factoryCalls = 0;
            manager.ConfigureInitialStartup(async ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                try
                {
                    // The owner marker is set around the asynchronous initial
                    // ApplicationConfiguration factory invocation. Cross an
                    // await boundary so the AsyncLocal marker must flow through
                    // the yield, then re-enter the startup: it must fail fast
                    // (BadInvalidState) instead of self-awaiting this start's
                    // own shared task (which would deadlock).
                    await Task.Yield();
                    await manager.EnsureStartedAsync(ct).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Record the fail-fast result and let the factory complete
                    // so the manager still reaches Started.
                    reentrantError = e;
                }
                return BuildAppConfig(telemetry, url);
            });

            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(reentrantError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)reentrantError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            // The real startup still completed and bound the listener.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task InitialConfigurationFactoryReentrantRegistrationFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20561);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Exception? reentrantError = null;
            manager.ConfigureInitialStartup(async ct =>
            {
                try
                {
                    // Re-enter indirectly via RegisterWaitingConnectionAsync
                    // (which internally calls EnsureStartedAsync). The owner
                    // marker set around the factory invocation must make the
                    // re-entrant registration fail fast rather than deadlock.
                    await Task.Yield();
                    await manager.RegisterWaitingConnectionAsync(
                        new Uri(Scheme + "://localhost:1/reentrant"),
                        null,
                        (sender, e) => { },
                        ReverseConnectManager.ReverseConnectStrategy.Once,
                        ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    reentrantError = e;
                }
                return BuildAppConfig(telemetry, url);
            });

            await manager.EnsureStartedAsync().ConfigureAwait(false);

            Assert.That(reentrantError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)reentrantError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task ProviderReentrantDisposeAsyncFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20570);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            var provider = new ReentrantDisposeProvider(manager);
            manager.ConfigurationProvider = provider;
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, url));

            // The shared startup runs the provider during preparation. The
            // provider re-enters DisposeAsync on the same in-flight startup
            // flow: it must fail fast (InvalidOperationException) instead of
            // awaiting the shared teardown, which drains this very start task and
            // would deadlock. The scheduled dispose still completes once the
            // start unwinds.
            Exception? startError = null;
            try
            {
                await manager.EnsureStartedAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                startError = e;
            }

            // Await the shared disposal from a non-reentrant flow to prove the
            // teardown completed without deadlocking.
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                provider.ReentrantError,
                Is.InstanceOf<InvalidOperationException>());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            // The start was superseded by the in-flight dispose and never bound.
            Assert.That(startError, Is.Not.Null);
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task InitialConfigurationFactoryReentrantDisposeAsyncFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20571);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Exception? reentrantError = null;
            manager.ConfigureInitialStartup(async ct =>
            {
                try
                {
                    // The owner marker is set around the asynchronous initial
                    // ApplicationConfiguration factory invocation. Cross an await
                    // boundary so the AsyncLocal marker must flow through the
                    // yield, then re-enter DisposeAsync on the same start flow: it
                    // must fail fast rather than deadlock on the shared teardown.
                    await Task.Yield();
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    reentrantError = e;
                }
                return BuildAppConfig(telemetry, url);
            });

            Exception? startError = null;
            try
            {
                await manager.EnsureStartedAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                startError = e;
            }

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                reentrantError,
                Is.InstanceOf<InvalidOperationException>());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(startError, Is.Not.Null);
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task CanceledStopRollbackDuringDisposeSkipsReopenAndDisposes()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20575);
            var closing = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new CancellationTokenSource();
            int armed = 1;

            // The first listener close blocks until the caller token is
            // cancelled; subsequent closes complete immediately.
            harness.SetCloseGate(url, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closing.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);
            Assert.That(listener.IsOpen, Is.True);

            // A cancellable stop acquires the gate and blocks inside the close.
            Task stop = manager.StopServiceAsync(cts.Token);
            await closing.Task.ConfigureAwait(false);

            // A concurrent Dispose sets the shutdown latch and marks the manager
            // Disposing synchronously (without the gate the stop holds), then
            // queues its teardown behind the gate.
            Task dispose = manager.DisposeAsync().AsTask();

            // Cancel the stop's close: it rolls back, but must observe the
            // pending disposal and skip the reopen/Started write, leaving the
            // disposal to finalize the lifecycle.
            cts.Cancel();

            Assert.That(
                async () => await stop.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            await dispose.ConfigureAwait(false);

            // The rolled-back stop never resurrected the manager as Started.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task DisposalBetweenCanceledStopRestoreCheckAndOpenSkipsReopen()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20585);
            var closing = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new CancellationTokenSource();
            int armed = 1;

            // The first listener close blocks until the caller token is
            // cancelled; subsequent closes complete immediately.
            harness.SetCloseGate(url, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closing.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);
            Assert.That(listener.IsOpen, Is.True);

            // The rollback's initial shutdown check passes (no shutdown yet); the
            // reopen hook then starts a Dispose in the window between that check
            // and the pre-open recheck, so the recheck must abort the reopen.
            Task? dispose = null;
            manager.RestoreReopenHookForTest = () =>
            {
                dispose ??= manager.DisposeAsync().AsTask();
                return Task.CompletedTask;
            };

            // A cancellable stop acquires the gate and blocks inside the close.
            Task stop = manager.StopServiceAsync(cts.Token);
            await closing.Task.ConfigureAwait(false);

            // Cancel the stop's close: it rolls back and enters the restore path,
            // whose hook starts the disposal between the initial check and open.
            cts.Cancel();

            Assert.That(
                async () => await stop.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(dispose, Is.Not.Null, "the restore reopen hook must have run");
            await dispose!.ConfigureAwait(false);

            // No listener was reopened and the disposal finalized the lifecycle.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task DisposalBetweenReloadRestoreCheckAndOpenSkipsReopenAndDisposes()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20590);
            Uri replacement = Url(20591);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True);

            // The replacement fails to open, so the reload rolls back through
            // RestoreAfterFailureAsync (the previous service was Started).
            harness.FailOpenFor(
                replacement,
                new ServiceResultException(StatusCodes.BadNoCommunication));

            // Start a Dispose in the window between the restore's shutdown check
            // and its listener reopen. The dispose latches synchronously, so the
            // pre-open recheck must abort the rollback without reopening original.
            Task? dispose = null;
            manager.RestoreReopenHookForTest = () =>
            {
                dispose ??= manager.DisposeAsync().AsTask();
                return Task.CompletedTask;
            };

            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration)
                .ConfigureAwait(false);

            Assert.That(dispose, Is.Not.Null, "the restore reopen hook must have run");
            await dispose!.ConfigureAwait(false);

            // The rolled-back reload never reopened the original listener, and the
            // disposal finalized the lifecycle.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task StopLatchBetweenReloadRestoreCheckAndOpenSkipsReopenAndStops()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20915);
            Uri replacement = Url(20916);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            FakeReverseListener originalListener = harness.Listeners.Single(
                l => l.OpenedUrl == original);
            Assert.That(originalListener.IsOpen, Is.True);
            int originalOpenCountBeforeRollback = originalListener.OpenCount;

            // The replacement fails to open, so the reload rolls back through
            // RestoreAfterFailureAsync (the previous service was Started).
            harness.FailOpenFor(
                replacement,
                new ServiceResultException(StatusCodes.BadNoCommunication));

            // A non-cancellable Stop is started in the window between the
            // rollback's transaction publication and its listener reopen. The stop
            // sets the shutdown latch and aborts the rollback transaction
            // synchronously, so the rollback's pre-open recheck must abort WITHOUT
            // reopening any listener (its OpenAsync is never re-entered) and let
            // the queued stop finalize the manager as Stopped.
            Task? stop = null;
            manager.RestoreReopenHookForTest = () =>
            {
                stop ??= manager.StopServiceAsync();
                return Task.CompletedTask;
            };

            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration)
                .ConfigureAwait(false);

            Assert.That(stop, Is.Not.Null, "the restore reopen hook must have run");
            await stop!.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                harness.Listeners.Any(l => l.IsOpen),
                Is.False,
                "a queued non-cancellable stop must not leave any listener open");
            Assert.That(
                originalListener.OpenCount,
                Is.EqualTo(originalOpenCountBeforeRollback),
                "the rolled-back reload must not reopen the previous listener");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.False,
                "the failed replacement listener must never become active");
        }

        [Test]
        public async Task DisposeDrainsHeldConnectionCallbackBeforeListenerDisposal()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20595);
            var closeGateEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseClose = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Gate the listener close (driven by the terminal host dispose) so the
            // test can prove the held callback has already drained before the
            // listener is torn down.
            harness.SetCloseGate(url, async _ =>
            {
                closeGateEntered.TrySetResult(true);
                await releaseClose.Task.ConfigureAwait(false);
            });
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);
            Assert.That(listener.IsOpen, Is.True);

            // Drive a held callback: no registration matches the event, so the
            // callback parks on its hold delay (bound to the manager registration
            // token) with the transport unaccepted.
            var e = new FakeConnectionWaitingEventArgs("urn:test:reverse:unmatched", url);
            Task callback = manager.InvokeConnectionWaitingForTest(e);
            Assert.That(callback.IsCompleted, Is.False, "the unmatched callback must hold");
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.EqualTo(1));

            // Dispose must cancel the held callback and wait for it to finish
            // before the terminal listener dispose drives the gated close.
            Task dispose = manager.DisposeAsync().AsTask();

            await closeGateEntered.Task.ConfigureAwait(false);
            Assert.That(
                callback.IsCompleted,
                Is.True,
                "the held callback must drain before the listener is torn down");
            Assert.That(
                e.Accepted,
                Is.False,
                "the drained callback must leave the transport unaccepted");

            releaseClose.TrySetResult(true);
            await dispose.ConfigureAwait(false);
            await callback.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(listener.IsOpen, Is.False);
            Assert.That(listener.DisposeCount, Is.GreaterThan(0));
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);
        }

        [Test]
        public async Task StopDrainsHeldConnectionCallbackBeforeListenerClose()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20600);
            var closeGateEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseClose = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            harness.SetCloseGate(url, async _ =>
            {
                closeGateEntered.TrySetResult(true);
                await releaseClose.Task.ConfigureAwait(false);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);
            Assert.That(listener.IsOpen, Is.True);

            // Park a held callback with an unaccepted transport.
            var e = new FakeConnectionWaitingEventArgs("urn:test:reverse:unmatched", url);
            Task callback = manager.InvokeConnectionWaitingForTest(e);
            Assert.That(callback.IsCompleted, Is.False);
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.EqualTo(1));

            // A committed (non-cancellable) stop must drain the held callback
            // before the terminal listener close tears the channels down.
            Task stop = manager.StopServiceAsync();

            await closeGateEntered.Task.ConfigureAwait(false);
            Assert.That(
                callback.IsCompleted,
                Is.True,
                "the held callback must drain before the listener close");
            Assert.That(e.Accepted, Is.False);

            releaseClose.TrySetResult(true);
            await stop.ConfigureAwait(false);
            await callback.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task ReloadDrainsHeldConnectionCallbackBeforeReplacingListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20900);
            Uri replacement = Url(20901);
            var closeGateEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseClose = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int armed = 1;

            // Block the original listener's replacement close so the test can prove
            // the held callback has already drained - and new candidate callbacks
            // are rejected - before the previous listener is torn down. Later
            // closes complete so teardown is not itself blocked.
            harness.SetCloseGate(original, async _ =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closeGateEntered.TrySetResult(true);
                    await releaseClose.Task.ConfigureAwait(false);
                }
            });

            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            FakeReverseListener originalListener = harness.Listeners.Single(
                l => l.OpenedUrl == original);
            Assert.That(originalListener.IsOpen, Is.True);

            // Park a held callback on the running manager: no registration matches,
            // so it holds its (unaccepted) transport parked on the hold delay.
            var held = new FakeConnectionWaitingEventArgs("urn:test:reverse:unmatched", original);
            Task heldCallback = manager.InvokeConnectionWaitingForTest(held);
            Assert.That(heldCallback.IsCompleted, Is.False, "the unmatched callback must hold");
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.EqualTo(1));

            // Reload replaces the original listener with the replacement. The
            // activation must drain the held callback before closing the original.
            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);

            await closeGateEntered.Task.ConfigureAwait(false);
            Assert.That(
                heldCallback.IsCompleted,
                Is.True,
                "the held callback must drain before the previous listener close");
            Assert.That(
                held.Accepted,
                Is.False,
                "the drained callback must leave the transport unaccepted");
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);

            // A candidate callback raised during the uncommitted activation window
            // must be rejected (unaccepted, never tracked) so it can never outlive
            // the reload and leak a transport if the activation rolls back.
            var candidate = new FakeConnectionWaitingEventArgs(
                "urn:test:reverse:candidate",
                replacement);
            await manager.InvokeConnectionWaitingForTest(candidate).ConfigureAwait(false);
            Assert.That(
                candidate.Accepted,
                Is.False,
                "a candidate callback must be rejected during the uncommitted activation");
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);

            releaseClose.TrySetResult(true);
            await reload.ConfigureAwait(false);
            await heldCallback.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.True,
                "the reload's replacement listener must be active after commit");
            Assert.That(
                originalListener.IsOpen,
                Is.False,
                "the previous listener is closed and discarded on a successful reload");
            Assert.That(
                originalListener.DisposeCount,
                Is.GreaterThan(0),
                "the drained transport must be resolved before the old listener is disposed");
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);
        }

        [Test]
        public async Task FailedReloadDrainsHeldConnectionCallbackBeforeRestoringPrevious()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20905);
            Uri replacement = Url(20906);
            var closeGateEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseClose = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int armed = 1;

            harness.SetCloseGate(original, async _ =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closeGateEntered.TrySetResult(true);
                    await releaseClose.Task.ConfigureAwait(false);
                }
            });

            // The replacement fails to open, so the reload rolls back through
            // RestoreAfterFailureAsync (the previous service was Started).
            harness.FailOpenFor(
                replacement,
                new ServiceResultException(StatusCodes.BadNoCommunication));

            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            FakeReverseListener originalListener = harness.Listeners.Single(
                l => l.OpenedUrl == original);
            Assert.That(originalListener.IsOpen, Is.True);

            var held = new FakeConnectionWaitingEventArgs("urn:test:reverse:unmatched", original);
            Task heldCallback = manager.InvokeConnectionWaitingForTest(held);
            Assert.That(heldCallback.IsCompleted, Is.False);
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.EqualTo(1));

            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);

            await closeGateEntered.Task.ConfigureAwait(false);
            Assert.That(
                heldCallback.IsCompleted,
                Is.True,
                "the held callback must drain before the previous listener close");
            Assert.That(held.Accepted, Is.False);
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);

            releaseClose.TrySetResult(true);
            // ReloadConfigurationAsync logs and swallows the failed reload.
            await reload.ConfigureAwait(false);
            await heldCallback.ConfigureAwait(false);

            // The rollback restored the previous listener; the replacement never
            // bound and no candidate callback leaked.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True,
                "the previous listener must be restored after a failed reload");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.False,
                "the failed replacement listener must not remain active");
            Assert.That(
                manager.ActiveConnectionCallbackCountForTest,
                Is.Zero,
                "no candidate callback may leak after a rolled-back reload");
        }

        [Test]
        public async Task ConnectionCallbackReentrantDisposeFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20760);
            const string serverUri = "urn:test:reverse:reentrant";
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Register a user callback that re-enters the synchronous Dispose()
            // from within the connection-waiting callback. The teardown drains
            // in-flight callbacks, so a synchronous dispose here that blocked on
            // that drain would deadlock: it must fail fast instead.
            Exception? reentrantError = null;
            await manager.RegisterWaitingConnectionAsync(
                url,
                serverUri,
                (s, ev) =>
                {
                    try
                    {
                        manager.Dispose();
                    }
                    catch (Exception ex)
                    {
                        reentrantError = ex;
                    }
                },
                ReverseConnectManager.ReverseConnectStrategy.Always).ConfigureAwait(false);

            var e = new FakeConnectionWaitingEventArgs(serverUri, url);
            await manager.InvokeConnectionWaitingForTest(e).ConfigureAwait(false);

            Assert.That(reentrantError, Is.InstanceOf<InvalidOperationException>());
            // The matched callback still resolved transport ownership.
            Assert.That(e.Accepted, Is.True);
            // The reentrant call never tore the manager down, so it stays
            // coherent (no leaked callback) and can be disposed externally.
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            await manager.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task ConnectionCallbackReentrantDisposeAsyncFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20765);
            const string serverUri = "urn:test:reverse:reentrant";
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Re-enter DisposeAsync from within the callback and await it. It
            // must fail fast (a faulted task) before the teardown that drains
            // this very callback is ever scheduled, so awaiting it can never
            // deadlock.
            Task? reentrantDispose = null;
            await manager.RegisterWaitingConnectionAsync(
                url,
                serverUri,
                (s, ev) => reentrantDispose = manager.DisposeAsync().AsTask(),
                ReverseConnectManager.ReverseConnectStrategy.Always).ConfigureAwait(false);

            var e = new FakeConnectionWaitingEventArgs(serverUri, url);
            await manager.InvokeConnectionWaitingForTest(e).ConfigureAwait(false);

            Assert.That(reentrantDispose, Is.Not.Null);
            Assert.That(
                async () => await reentrantDispose!.ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(e.Accepted, Is.True);
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            await manager.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task ConnectionCallbackReentrantStopServiceFailsFastWithoutDeadlock()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20770);
            const string serverUri = "urn:test:reverse:reentrant";
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Re-enter StopServiceAsync from within the callback and synchronously
            // wait on it. The stop drains in-flight callbacks, so it would wait
            // forever for the very callback that called it: it must fail fast
            // with BadInvalidState instead.
            Exception? reentrantError = null;
            await manager.RegisterWaitingConnectionAsync(
                url,
                serverUri,
                (s, ev) =>
                {
                    try
                    {
                        manager.StopServiceAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        reentrantError = ex;
                    }
                },
                ReverseConnectManager.ReverseConnectStrategy.Always).ConfigureAwait(false);

            var e = new FakeConnectionWaitingEventArgs(serverUri, url);
            await manager.InvokeConnectionWaitingForTest(e).ConfigureAwait(false);

            Assert.That(reentrantError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)reentrantError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(e.Accepted, Is.True);
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);
            // The manager was never stopped by the reentrant call, so an
            // external stop from a different flow still drains callbacks and
            // completes cleanly.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task DeferredCallbackReentryAfterCallbackCompletesIsNotFalselyRejected()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20775);
            const string serverUri = "urn:test:reverse:deferred";
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            Exception? immediateError = null;
            var releaseDeferred = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task? deferredStop = null;
            await manager.RegisterWaitingConnectionAsync(
                url,
                serverUri,
                (s, ev) =>
                {
                    // An immediate synchronous re-entry on the callback flow
                    // must still fail fast (scope Active == true).
                    try
                    {
                        manager.StopServiceAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        immediateError = ex;
                    }

                    // A child task capturing the callback scope but running only
                    // AFTER the callback flow returns must observe the scope as
                    // inactive and NOT be falsely rejected as a callback re-entry.
                    deferredStop = Task.Run(async () =>
                    {
                        await releaseDeferred.Task.ConfigureAwait(false);
                        await manager.StopServiceAsync().ConfigureAwait(false);
                    });
                },
                ReverseConnectManager.ReverseConnectStrategy.Always)
                .ConfigureAwait(false);

            var e = new FakeConnectionWaitingEventArgs(serverUri, url);
            await manager.InvokeConnectionWaitingForTest(e).ConfigureAwait(false);

            // Immediate reentry failed fast; the manager remained coherent.
            Assert.That(immediateError, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)immediateError!).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(e.Accepted, Is.True);
            Assert.That(manager.ActiveConnectionCallbackCountForTest, Is.Zero);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            // The callback flow has completed, so its scope is now inactive: the
            // deferred stop must run to completion instead of failing fast.
            releaseDeferred.TrySetResult(true);
            Assert.That(deferredStop, Is.Not.Null);
            await deferredStop!.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task CanceledStopRollbackRequeuesInFlightReloadAndAppliesChange()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri liveUrl = Url(20580);
            Uri newUrl = Url(20581);
            var releaseReload = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var reloadProvider = new GatedProvider(releaseReload.Task);
            using var cts = new CancellationTokenSource();
            int armed = 1;
            var closing = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.SetCloseGate(liveUrl, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closing.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                // Start with liveUrl. No provider yet, so the initial start runs
                // to completion immediately.
                await manager.StartServiceAsync(ConfigFor(liveUrl)).ConfigureAwait(false);
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == liveUrl && l.IsOpen),
                    Is.True);
                int watcherGeneration = manager.CurrentWatcherGeneration;

                // Enqueue a reload to newUrl. Gate its preparation so it stays
                // in-flight (dequeued, holding no lifecycle gate) while the stop
                // runs and bumps the lifecycle version.
                manager.ConfigurationProvider = reloadProvider;
                Task reload = manager.ReloadConfigurationAsync(
                    BuildAppConfig(telemetry, newUrl),
                    watcherGeneration);
                await reloadProvider.Entered.ConfigureAwait(false);

                // A cancellable stop acquires the gate, bumps the lifecycle
                // version and blocks inside the close; cancelling it rolls back to
                // Started but would otherwise strand the in-flight reload's
                // superseded version.
                Task stop = manager.StopServiceAsync(cts.Token);
                await closing.Task.ConfigureAwait(false);
                cts.Cancel();
                Assert.That(
                    async () => await stop.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

                // Release the in-flight reload: its original attempt is superseded
                // and dropped, but the rolled-back stop re-queued the candidate,
                // so the change is eventually applied.
                releaseReload.SetResult(true);
                await reload.ConfigureAwait(false);

                await WaitForAsync(
                    () => harness.Listeners.Any(l => l.OpenedUrl == newUrl && l.IsOpen),
                    "the re-queued reload must eventually open the changed endpoint");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == liveUrl && l.IsOpen),
                    Is.False,
                    "the stale endpoint must be closed once the reload commits");
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task HostedStartWithAlreadyCancelledTokenNeverRunsConfigurationFactory()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20562);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            int factoryCalls = 0;
            manager.ConfigureInitialStartup(ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult(BuildAppConfig(telemetry, url));
            });
            var hosted = new ReverseConnectManagerHostedService(manager);

            using var cts = new CancellationTokenSource();
            // The host startup token is cancelled BEFORE StartAsync creates the
            // shared start; the latched cancellation must abort the start right
            // after the yield and BEFORE the configuration factory runs, so an
            // already-cancelled startup never runs configuration validation or
            // certificate creation.
            cts.Cancel();

            Assert.That(
                async () => await hosted.StartAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(
                factoryCalls,
                Is.Zero,
                "the configuration factory must never run when the hosted start " +
                "is pre-cancelled");
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));
        }

        [Test]
        public async Task StopInsideActivationGateWindowAbortsBeforeOpen()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20520);
            var gateAcquired = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // Pause the activation immediately after gate acquisition and before
            // the ActiveTransaction is published.
            manager.GateAcquiredForTest = async () =>
            {
                gateAcquired.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };

            Task start = manager.StartServiceAsync(ConfigFor(url));
            await gateAcquired.Task.ConfigureAwait(false);

            // A non-cancellable stop sets the shutdown latch synchronously before
            // it yields on the gate wait, then queues behind the activation.
            Task stop = manager.StopServiceAsync();

            // Resume the activation. It must observe the latch and abort WITHOUT
            // entering the listener open.
            release.TrySetResult(true);
            await stop.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                harness.OpenObserved.IsCompleted,
                Is.False,
                "listener OpenAsync must never be entered when a shutdown is latched");
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);

            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()
                    .Or.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task DisposeInsideActivationGateWindowAbortsBeforeOpen()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20525);
            var gateAcquired = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            manager.GateAcquiredForTest = async () =>
            {
                gateAcquired.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };

            Task start = manager.StartServiceAsync(ConfigFor(url));
            await gateAcquired.Task.ConfigureAwait(false);

            // Dispose sets the shutdown latch synchronously (under the state
            // lock) before its teardown waits on the gate.
            Task dispose = manager.DisposeAsync().AsTask();

            release.TrySetResult(true);
            await dispose.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(
                harness.OpenObserved.IsCompleted,
                Is.False,
                "listener OpenAsync must never be entered when a disposal is latched");
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);

            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()
                    .Or.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public async Task WaitForConnectionDuringStopRejectsWithoutRegisteringInertWaiter()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20600);
            var closeEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var closeRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // The stop's close of the running listener blocks, so the manager
            // sits in the stop-in-progress window while a lazy Wait races it.
            harness.SetCloseGate(url, async _ =>
            {
                closeEntered.TrySetResult(true);
                await closeRelease.Task.ConfigureAwait(false);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            Task stop = manager.StopServiceAsync();
            await closeEntered.Task.ConfigureAwait(false);

            // A Wait racing the in-flight stop must reject deterministically
            // rather than returning success and registering an inert waiter
            // against a listener that is being closed.
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.WaitForConnectionAsync(url, null))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "no inert waiting-connection registration must survive a stop");

            closeRelease.SetResult(true);
            await stop.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task RegisterWaitingConnectionAsyncDuringStopRejectsWithoutRegistering()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20605);
            var closeEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var closeRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            harness.SetCloseGate(url, async _ =>
            {
                closeEntered.TrySetResult(true);
                await closeRelease.Task.ConfigureAwait(false);
            });
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            Task stop = manager.StopServiceAsync();
            await closeEntered.Task.ConfigureAwait(false);

            // An async registration racing the in-flight stop must reject
            // deterministically and add no registration.
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.RegisterWaitingConnectionAsync(
                    url,
                    null,
                    (sender, e) => { },
                    ReverseConnectManager.ReverseConnectStrategy.Once))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "no inert waiting-connection registration must survive a stop");

            closeRelease.SetResult(true);
            await stop.ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task RegisterWaitingConnectionAsyncStopAfterStartRejectsWithoutInertRegistration()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20607);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            var reachedWindow = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Pause the registration in the exact window between the internal
            // EnsureStartedAsync (already Started here) and the registration
            // insertion.
            manager.BeforeRegisterWaitForTest = async () =>
            {
                reachedWindow.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };

            Task<int> register = manager.RegisterWaitingConnectionAsync(
                url,
                null,
                (sender, e) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once);
            await reachedWindow.Task.ConfigureAwait(false);

            // A full stop commits while the registration is paused in the
            // window: it transitions the manager to Stopped and clears the
            // registrations under m_registrationsLock.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            // Resuming the registration must observe the committed stop and
            // reject deterministically rather than inserting an inert waiter.
            release.SetResult(true);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => register)!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "no inert waiting-connection registration must survive a stop " +
                "that commits between EnsureStartedAsync and insertion");
        }

        [Test]
        public async Task StopThenLazyRestartReopensLatestReloadedEndpoints()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri endpointA = Url(20610);
            Uri endpointB = Url(20611);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // DI-lazy scenario: the initial startup is configured with endpoint A.
            manager.ConfigureInitialStartup(BuildAppConfig(telemetry, endpointA));

            await manager.EnsureStartedAsync().ConfigureAwait(false);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointA && l.IsOpen),
                Is.True);

            // A successful reload swaps A for B and promotes B to the lazy
            // restart seed.
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, endpointB),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointB && l.IsOpen),
                Is.True);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointA && l.IsOpen),
                Is.False);

            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            // The lazy restart must reopen the latest reloaded endpoint (B),
            // not the original startup endpoint (A).
            await manager.EnsureStartedAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointB && l.IsOpen),
                Is.True,
                "the lazy restart must reopen the latest reloaded endpoint B");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointA && l.IsOpen),
                Is.False,
                "the lazy restart must not reopen the original startup endpoint A");
        }

        [Test]
        public async Task CandidateOpenFailureDisposesCandidateListenerOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20620);
            harness.FailOpenFor(url, new ServiceResultException(StatusCodes.BadNoCommunication));
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            Assert.ThrowsAsync<ServiceResultException>(
                () => manager.StartServiceAsync(ConfigFor(url)));

            Assert.That(harness.Listeners, Has.Count.EqualTo(1));
            Assert.That(
                harness.Listeners[0].DisposeCount,
                Is.EqualTo(1),
                "the failed candidate listener must be disposed exactly once");
            Assert.That(harness.Listeners[0].IsOpen, Is.False);
        }

        [Test]
        public async Task SuccessfulReloadDisposesReplacedListenerOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20630);
            Uri replacement = Url(20631);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            FakeReverseListener originalListener =
                harness.Listeners.Single(l => l.OpenedUrl == original);

            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            // The replaced snapshot listener is disposed exactly once and the
            // replacement is actively serving.
            Assert.That(
                originalListener.DisposeCount,
                Is.EqualTo(1),
                "the replaced snapshot listener must be disposed exactly once");
            Assert.That(originalListener.IsOpen, Is.False);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task CommittedStopDisposesConfiguredListenerOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20640);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);

            await manager.StopServiceAsync().ConfigureAwait(false);

            Assert.That(
                listener.DisposeCount,
                Is.EqualTo(1),
                "a committed stop must dispose its configured listener exactly once");
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task FailedReloadDisposesFailedCandidateAndRestoresPrevious()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20650);
            Uri replacement = Url(20651);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);

            // The replacement fails to open: the reload rolls back and restores
            // the previous service (with a freshly created listener).
            harness.FailOpenFor(replacement, new ServiceResultException(StatusCodes.BadNoCommunication));
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            FakeReverseListener failed =
                harness.Listeners.Single(l => l.OpenedUrl == replacement);
            Assert.That(
                failed.DisposeCount,
                Is.EqualTo(1),
                "the rolled-back candidate listener must be disposed exactly once");
            Assert.That(failed.IsOpen, Is.False);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.True,
                "the previous listener must be restored and actively open");
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
        }

        [Test]
        public async Task ManagerDisposeDisposesConfiguredListenerOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20655);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            FakeReverseListener listener = harness.Listeners.Single(l => l.OpenedUrl == url);

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                listener.DisposeCount,
                Is.EqualTo(1),
                "manager disposal must dispose its listener exactly once");
            Assert.That(listener.IsOpen, Is.False);
        }

        [Test]
        public async Task ManualHostSurvivesReloadAndIsDisposedOnceAtManagerDispose()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri manual = Url(20660);
            Uri configured1 = Url(20661);
            Uri configured2 = Url(20662);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.AddEndpoint(manual);

            await manager.StartServiceAsync(ConfigFor(configured1)).ConfigureAwait(false);
            FakeReverseListener manualListener =
                harness.Listeners.Single(l => l.OpenedUrl == manual);

            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, configured2),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            // The reused manual endpoint host is only closed/reopened across the
            // reload - never disposed and never recreated.
            Assert.That(
                manualListener.DisposeCount,
                Is.Zero,
                "a reused manual endpoint host must not be disposed across a reload");
            Assert.That(
                harness.Listeners.Count(l => l.OpenedUrl == manual),
                Is.EqualTo(1),
                "the manual host must be reused, not recreated");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == manual && l.IsOpen),
                Is.True);

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                manualListener.DisposeCount,
                Is.EqualTo(1),
                "the reused manual host is disposed exactly once at manager disposal");
        }

        [Test]
        public async Task ReloadReplacementCloseBlockedThenStopSupersedesWithoutRestore()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20670);
            Uri replacement = Url(20671);
            var closeEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int armed = 1;

            // The reload's replacement close of the original listener blocks
            // until the manager-owned operation token (aborted by a
            // non-cancellable Stop) cancels it. Later closes complete so
            // teardown/disposal is not itself blocked.
            harness.SetCloseGate(original, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closeEntered.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });

            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);

            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);
            await closeEntered.Task.ConfigureAwait(false);

            // A non-cancellable stop aborts the in-flight reload's transaction,
            // unblocking its replacement close, then acquires the gate and
            // finalizes the manager. Before the fix the replacement close ran
            // under CancellationToken.None and could never be unblocked here.
            Task stop = manager.StopServiceAsync();

            await reload.ConfigureAwait(false);
            await stop.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                harness.Listeners.Any(l => l.IsOpen),
                Is.False,
                "a shutdown supersession must not leave any listener open");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.OpenCount > 0),
                Is.False,
                "a shutdown supersession must not open the replacement listener");
        }

        [Test]
        public async Task ReloadReplacementCloseBlockedThenDisposeSupersedesWithoutRestore()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20675);
            Uri replacement = Url(20676);
            var closeEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int armed = 1;

            harness.SetCloseGate(original, ct =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    closeEntered.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, ct);
                }
                return Task.CompletedTask;
            });

            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);

            Task reload = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration);
            await closeEntered.Task.ConfigureAwait(false);

            // Disposal aborts the in-flight reload's transaction, unblocking its
            // replacement close, then tears the manager down.
            Task dispose = manager.DisposeAsync().AsTask();

            await reload.ConfigureAwait(false);
            await dispose.ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == replacement && l.OpenCount > 0),
                Is.False,
                "a disposal supersession must not open the replacement listener");
        }

        [Test]
        public async Task OverlappingReloadsApplyNewestCandidateWhenEarlierProviderDelayed()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20690);
            Uri endpointA = Url(20691);
            Uri endpointB = Url(20692);
            var releaseA = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(releaseA.Task);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            int generation = manager.CurrentWatcherGeneration;

            // The provider gates the FIRST reload (A) inside preparation so its
            // provider is delayed while the second reload (B) is submitted.
            manager.ConfigurationProvider = provider;
            Task reloadA = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, endpointA),
                generation);
            await provider.Entered.ConfigureAwait(false);

            // Reload B is submitted while A is still blocked in its provider. It
            // queues as the newest candidate; the latest-wins loop applies it
            // after A rather than letting A's later commit drop it.
            Task reloadB = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, endpointB),
                generation);

            // Release A; A commits first (older), then B commits (newest).
            releaseA.SetResult(true);
            await Task.WhenAll(reloadA, reloadB).ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointB && l.IsOpen),
                Is.True,
                "the newest reload candidate (B) must be the final active endpoint");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointA && l.IsOpen),
                Is.False,
                "the older reload candidate (A) must not remain active");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == original && l.IsOpen),
                Is.False);
        }

        [Test]
        public async Task QueuedReloadBehindBlockedReloadIsRejectedByStopAndNeverReopens()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20710);
            Uri endpointA = Url(20711);
            Uri endpointB = Url(20712);
            var releaseA = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(releaseA.Task);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);
            int generation = manager.CurrentWatcherGeneration;

            // Reload A blocks inside its provider preparation (it does not yet
            // hold the lifecycle gate). It captured the running lifecycle
            // version and teardown epoch when it was queued.
            manager.ConfigurationProvider = provider;
            Task reloadA = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, endpointA),
                generation);
            await provider.Entered.ConfigureAwait(false);

            // Reload B queues behind the blocked A in the same teardown epoch.
            Task reloadB = manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, endpointB),
                generation);

            // A non-cancellable Stop commits while A is still blocked in its
            // provider: the gate is free, so the stop advances the teardown
            // epoch, tears the manager down and rejects the queued B
            // deterministically (BadInvalidState) without ever applying it.
            await manager.StopServiceAsync().ConfigureAwait(false);

            // The queued B was completed by the committed stop without applying,
            // so its awaiter never hangs.
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(
                () => reloadB)!;
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            // Release A: its activation observes the superseding stop and never
            // opens a listener, then completes its awaiter without faulting.
            releaseA.SetResult(true);
            await reloadA.ConfigureAwait(false);

            Assert.That(reloadB.IsCompleted, Is.True);
            Assert.That(reloadA.IsCompleted, Is.True);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                harness.Listeners.Any(l => l.IsOpen),
                Is.False,
                "no listener may remain open after the stop completes");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointB && l.OpenCount > 0),
                Is.False,
                "the queued reload (B) must never open its listener after the stop");
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == endpointA && l.OpenCount > 0),
                Is.False,
                "the blocked reload (A) must never open its listener after the stop");
        }

        [Test]
        public async Task FileBackedLazyRestartReloadsConfigurationFromSourceFile()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri initialUrl = Url(20695);
            Uri changedUrl = Url(20696);
            string path = NewConfigFile();
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                int loadCount = 0;
                // The injectable loader seam stands in for
                // ApplicationConfiguration.LoadAsync: it simulates the source
                // file changing while stopped by returning the changed endpoint.
                manager.ConfigurationFileLoaderForTest = (p, appType, cfgType, ct) =>
                {
                    Interlocked.Increment(ref loadCount);
                    return Task.FromResult(BuildAppConfig(telemetry, changedUrl));
                };

                manager.ConfigureInitialStartup(
                    BuildAppConfigWithFile(telemetry, path, initialUrl));

                // First start must use the provided configuration, not reload.
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == initialUrl && l.IsOpen),
                    Is.True);
                Assert.That(
                    loadCount,
                    Is.Zero,
                    "the first start must not reload from the source file");

                // Stop converts the file-backed seed into a reloading factory.
                await manager.StopServiceAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

                // The lazy restart must reload from SourceFilePath (through the
                // loader seam) and apply the change made while stopped instead of
                // reusing the cached configuration.
                await manager.EnsureStartedAsync().ConfigureAwait(false);

                Assert.That(
                    loadCount,
                    Is.EqualTo(1),
                    "the restart must reload the configuration from the source file");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == changedUrl && l.IsOpen),
                    Is.True,
                    "the restart must apply the changed configuration from the source file");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == initialUrl && l.IsOpen),
                    Is.False,
                    "the stale cached endpoint must not be reopened on restart");
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public async Task WatcherTriggeredReloadReappliesReverseConnectOptionOverlay()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri fileUrl = Url(20720);
            Uri changedFileUrl = Url(20721);
            Uri optionUrl = Url(20722);
            string path = NewConfigFile();
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));

            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GatedProvider(release.Task);

            // The DI overlay decorator appends the option-only reverse-connect
            // endpoint (which lives only in memory, not in the file) to whatever
            // endpoints the freshly loaded file configuration carries.
            ApplicationConfiguration Decorate(ApplicationConfiguration loaded)
            {
                ReverseConnectClientEndpoint[] endpoints =
                    loaded.ClientConfiguration!.ReverseConnect!.ClientEndpoints.ToArray() ?? [];
                Uri[] urls = endpoints
                    .Select(e => new Uri(e.EndpointUrl!))
                    .Append(optionUrl)
                    .ToArray();
                return string.IsNullOrEmpty(loaded.SourceFilePath)
                    ? BuildAppConfig(telemetry, urls)
                    : BuildAppConfigWithFile(telemetry, loaded.SourceFilePath!, urls);
            }

            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            try
            {
                // The watcher-triggered reload reloads the file through the seam,
                // which returns the changed file endpoint only (no overlay).
                manager.ConfigurationFileLoaderForTest = (p, appType, cfgType, ct) =>
                    Task.FromResult(BuildAppConfigWithFile(telemetry, p, changedFileUrl));

                // Configure the lazy startup with the fully decorated initial
                // configuration (file endpoint plus the option overlay) and
                // retain the decorator so a reload reapplies the overlay.
                manager.ConfigureInitialStartup(
                    Decorate(BuildAppConfigWithFile(telemetry, path, fileUrl)),
                    Decorate);

                Task start = manager.EnsureStartedAsync();
                await provider.Entered.ConfigureAwait(false);

                // Bump the write time between preparation and commit so the
                // commit detects the change and drives the watcher-changed seam.
                System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
                release.SetResult(true);
                await start.ConfigureAwait(false);

                // The watcher-triggered reload must reload from the file (the
                // changed endpoint) AND reapply the option overlay, so the
                // option-only endpoint remains open after the file change.
                await WaitForAsync(
                    () => harness.Listeners.Any(
                            l => l.OpenedUrl == changedFileUrl && l.IsOpen) &&
                        harness.Listeners.Any(
                            l => l.OpenedUrl == optionUrl && l.IsOpen) &&
                        !harness.Listeners.Any(l => l.OpenedUrl == fileUrl && l.IsOpen),
                    "the reload must open the changed file endpoint and keep the " +
                    "option overlay endpoint while replacing the original endpoint")
                    .ConfigureAwait(false);

                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == optionUrl && l.IsOpen),
                    Is.True,
                    "the DI reverse-connect option overlay endpoint must survive a " +
                    "file-triggered reload");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == changedFileUrl && l.IsOpen),
                    Is.True,
                    "the reload must apply the changed file endpoint");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == fileUrl && l.IsOpen),
                    Is.False,
                    "the stale original file endpoint must be replaced by the reload");
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public async Task FactoryLoadedStartFailureKeepsFactoryActiveForRetry()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri badUrl = Url(20730);
            Uri goodUrl = Url(20731);

            // The first (bad) configuration fails to open so the factory-loaded
            // start fails during activation.
            harness.FailOpenFor(
                badUrl,
                new ServiceResultException(StatusCodes.BadNoCommunication));

            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            int factoryCalls = 0;
            manager.ConfigureInitialStartup(ct =>
            {
                int n = Interlocked.Increment(ref factoryCalls);
                // First invocation yields the bad configuration A; a retry (the
                // factory being re-invoked) yields the good configuration B.
                return Task.FromResult(n == 1
                    ? BuildAppConfig(telemetry, badUrl)
                    : BuildAppConfig(telemetry, goodUrl));
            });
            try
            {
                // The first start invokes the factory (A) and fails to activate.
                Assert.That(
                    async () => await manager.EnsureStartedAsync().ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>());
                Assert.That(factoryCalls, Is.EqualTo(1));
                Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));

                // Because the failed factory result was never cached, the factory
                // stays active: a retry re-invokes it (B) and activates.
                await manager.EnsureStartedAsync().ConfigureAwait(false);

                Assert.That(
                    factoryCalls,
                    Is.EqualTo(2),
                    "the retry must re-invoke the factory rather than reuse the " +
                    "failed configuration");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == goodUrl && l.IsOpen),
                    Is.True,
                    "the retry must open the good configuration returned on the " +
                    "second factory invocation");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == badUrl && l.IsOpen),
                    Is.False,
                    "the failed bad configuration must not remain open");
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MemoryBackedRestartReappliesOverlaySoProviderMutationDoesNotPersist()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20740);
            const int optionHold = 12345;
            const int optionWait = 6789;

            // A rebuilding overlay decorator, exactly like the production DI
            // reverse-connect overlay: it replaces the reverse-connect section
            // with a fresh ReverseConnectClientConfiguration (fresh endpoint
            // objects and the configured hold/wait timeouts) on every call, so
            // reapplying it is idempotent and never accumulates or inherits a
            // prior mutation.
            ApplicationConfiguration Decorate(ApplicationConfiguration cfg)
            {
                cfg.ClientConfiguration ??= new ClientConfiguration();
                cfg.ClientConfiguration.ReverseConnect = new ReverseConnectClientConfiguration
                {
                    ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                        new[]
                        {
                            new ReverseConnectClientEndpoint { EndpointUrl = url.ToString() }
                        }),
                    HoldTime = optionHold,
                    WaitTimeout = optionWait
                };
                return cfg;
            }

            var provider = new MutatingRecordingProvider(url, optionHold);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            try
            {
                manager.ConfigureInitialStartup(
                    Decorate(BuildAppConfig(telemetry, url)),
                    Decorate);

                await manager.EnsureStartedAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                    Is.True);

                await manager.StopServiceAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

                // The memory-backed lazy restart must reapply the overlay
                // decorator before starting so the provider's in-place mutation
                // of the reverse-connect section during the first start does not
                // persist: the second invocation observes clean option endpoints
                // and timeouts, not the damaged cache.
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                    Is.True,
                    "the restart must reopen the option endpoint after reapplying " +
                    "the overlay");

                Assert.That(
                    provider.ObservedEndpointCounts,
                    Has.Count.EqualTo(2),
                    "both starts must reach the provider");
                Assert.That(
                    provider.ObservedEndpointCounts,
                    Is.All.EqualTo(1),
                    "every start must hand the provider the clean option endpoint " +
                    "rather than the endpoints a prior start mutated away");
                Assert.That(
                    provider.ObservedHoldTimes,
                    Is.EqualTo(new[] { optionHold, optionHold }),
                    "every start must hand the provider the clean option hold time");
                Assert.That(
                    provider.ObservedWaitTimeouts,
                    Is.EqualTo(new[] { optionWait, optionWait }),
                    "every start must hand the provider the clean option wait timeout");
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MemoryBackedFailedRetryReappliesOverlaySoProviderMutationDoesNotPersist()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20741);
            const int optionHold = 4321;
            const int optionWait = 9876;

            ApplicationConfiguration Decorate(ApplicationConfiguration cfg)
            {
                cfg.ClientConfiguration ??= new ClientConfiguration();
                cfg.ClientConfiguration.ReverseConnect = new ReverseConnectClientConfiguration
                {
                    ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                        new[]
                        {
                            new ReverseConnectClientEndpoint { EndpointUrl = url.ToString() }
                        }),
                    HoldTime = optionHold,
                    WaitTimeout = optionWait
                };
                return cfg;
            }

            // The provider damages the shared candidate in place and throws on
            // the first invocation so the start fails during preparation, then
            // succeeds on the retry.
            var provider = new MutatingRecordingProvider(url, optionHold)
            {
                ThrowOnFirstCall = true
            };
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };
            try
            {
                manager.ConfigureInitialStartup(
                    Decorate(BuildAppConfig(telemetry, url)),
                    Decorate);

                Assert.That(
                    async () => await manager.EnsureStartedAsync().ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>());
                Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));

                // The failed-start retry must reapply the overlay decorator so the
                // provider's in-place mutation during the failed attempt does not
                // persist into the retry's input.
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                    Is.True);

                Assert.That(
                    provider.ObservedEndpointCounts,
                    Has.Count.EqualTo(2),
                    "the failed attempt and retry must both reach the provider");
                Assert.That(
                    provider.ObservedEndpointCounts,
                    Is.All.EqualTo(1),
                    "the failed attempt and the retry must both observe the clean " +
                    "option endpoint");
                Assert.That(
                    provider.ObservedHoldTimes,
                    Is.EqualTo(new[] { optionHold, optionHold }));
                Assert.That(
                    provider.ObservedWaitTimeouts,
                    Is.EqualTo(new[] { optionWait, optionWait }));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StopCancelsAtDeadlineWhileCallbackBlockedLeavesStartedAndListenersOpen()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20742);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

                // A blocked external callback holds a transport and ignores the
                // cancelled hold token, so the stop's callback drain cannot
                // complete until the callback is released.
                var gate = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Task callback = manager.InvokeBlockingConnectionCallbackForTest(gate.Task);
                await WaitForAsync(
                    () => manager.ActiveConnectionCallbackCountForTest == 1,
                    "the blocking callback must be tracked before the stop");

                // A cancellable Stop carrying a deadline must cancel at the
                // deadline (parked on the drain) rather than hang forever on the
                // blocked callback.
                using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                Assert.That(
                    async () => await manager.StopServiceAsync(stopCts.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>(),
                    "the stop must cancel at its deadline while the callback is held");

                // The drain was cancelled before any listener was closed, so the
                // manager restores a coherent Started state with its listeners
                // still open and the callback still coherently tracked.
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                    Is.True,
                    "the listeners must remain open after a drain-cancelled stop");
                Assert.That(
                    manager.ActiveConnectionCallbackCountForTest,
                    Is.EqualTo(1),
                    "the held callback must remain coherently tracked");

                // Releasing the callback lets it resolve cleanly (the drain signal
                // was ended, so its exit never faults or double-signals).
                gate.SetResult(true);
                await callback.ConfigureAwait(false);
                await WaitForAsync(
                    () => manager.ActiveConnectionCallbackCountForTest == 0,
                    "the released callback must drain to zero");

                // A subsequent non-cancellable stop now completes cleanly.
                await manager.StopServiceAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
                Assert.That(
                    harness.Listeners.Any(l => l.IsOpen),
                    Is.False,
                    "the non-cancellable stop must close the listeners");
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task HostedServiceStopHonorsDeadlineWhileCallbackBlocked()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20743);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
                var hosted = new ReverseConnectManagerHostedService(manager);

                var gate = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Task callback = manager.InvokeBlockingConnectionCallbackForTest(gate.Task);
                await WaitForAsync(
                    () => manager.ActiveConnectionCallbackCountForTest == 1,
                    "the blocking callback must be tracked before the host stop");

                // The Generic Host shutdown token must flow through StopAsync and
                // cancel the drain at the host's deadline.
                using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                Assert.That(
                    async () => await hosted.StopAsync(stopCts.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>(),
                    "the hosted stop must honor the Generic Host shutdown deadline");
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == url && l.IsOpen),
                    Is.True);

                gate.SetResult(true);
                await callback.ConfigureAwait(false);

                // A non-cancellable host stop (no deadline) then completes.
                await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StopCancelsInFlightExplicitStartProvider()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new CancellationObservingProvider();
            Uri url = Url(20697);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            // An explicit start blocks in the provider (awaiting the
            // manager-owned start token).
            Task start = manager.StartServiceAsync(ConfigFor(url));
            await provider.Entered.ConfigureAwait(false);
            Assert.That(start.IsCompleted, Is.False);

            // Stop must cancel the explicit start's token so the provider
            // observes cancellation and the start exits (rather than leaving a
            // provider-blocked start that only supersedes at the gate).
            await manager.StopServiceAsync().ConfigureAwait(false);

            bool observedCancellation = await provider.Canceled
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(
                observedCancellation,
                Is.True,
                "the stop must cancel the explicit start's provider");
            // The start exits (waits for exit) rather than binding a listener.
            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<ServiceResultException>());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task DisposeCancelsInFlightExplicitStartProvider()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new CancellationObservingProvider();
            Uri url = Url(20699);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            Task start = manager.StartServiceAsync(ConfigFor(url));
            await provider.Entered.ConfigureAwait(false);
            Assert.That(start.IsCompleted, Is.False);

            // Dispose must cancel the explicit start's token so the provider
            // observes cancellation and the start exits.
            await manager.DisposeAsync().ConfigureAwait(false);

            bool observedCancellation = await provider.Canceled
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(
                observedCancellation,
                Is.True,
                "the dispose must cancel the explicit start's provider");
            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<ServiceResultException>()
                    .Or.InstanceOf<ObjectDisposedException>());
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Disposed"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
        }

        [Test]
        public async Task IndefiniteWaitForConnectionCompletedPromptlyByStop()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20680);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // A waiter with an indefinite (never-firing) token would otherwise
            // wait until its listener produced a connection; a committed stop
            // must fault it promptly with BadInvalidState.
            using var indefinite = new CancellationTokenSource();
            Task<ITransportWaitingConnection> wait =
                manager.WaitForConnectionAsync(url, null, indefinite.Token);
            await WaitForAsync(
                () => manager.WaitingConnectionCountForTest == 1,
                "the waiter must register before the stop").ConfigureAwait(false);

            await manager.StopServiceAsync().ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await wait.ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(manager.WaitingConnectionCountForTest, Is.Zero);
        }

        [Test]
        public async Task IndefiniteWaitForConnectionCompletedPromptlyByDispose()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20685);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            using var indefinite = new CancellationTokenSource();
            Task<ITransportWaitingConnection> wait =
                manager.WaitForConnectionAsync(url, null, indefinite.Token);
            await WaitForAsync(
                () => manager.WaitingConnectionCountForTest == 1,
                "the waiter must register before the dispose").ConfigureAwait(false);

            await manager.DisposeAsync().ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await wait.ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task WaitForConnectionAfterCommittedStopRejectsWithoutRegistering()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20688);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

            // A wait against a committed-stop manager (no lazy restart seed) is
            // rejected and inserts no registration - the state verification and
            // insertion are atomic under the registrations lock.
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => manager.WaitForConnectionAsync(url, null))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "no registration must be inserted against a committed-stop manager");
        }

        [Test]
        public async Task RegistrationRacingCommittedStopNeverLeavesInertWaiter()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            Uri url = Url(20690);
            for (int i = 0; i < 40; i++)
            {
                var harness = new FakeListenerHarness(Scheme);
                await using var manager = new ReverseConnectManager(telemetry)
                {
                    TransportBindings = harness.Registry
                };
                await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

                using var waitCts = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(500));
                Task waiter = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await manager
                                .WaitForConnectionAsync(url, null, waitCts.Token)
                                .ConfigureAwait(false);
                        }
                        catch (ServiceResultException)
                        {
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    });
                Task stop = Task.Run(() => manager.StopServiceAsync());

                await Task.WhenAll(waiter, stop).ConfigureAwait(false);

                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
                Assert.That(
                    manager.WaitingConnectionCountForTest,
                    Is.Zero,
                    "a committed stop must never leave an inert waiting registration");
            }
        }

        [Test]
        public async Task BlockedReloadThenStopAllowsImmediateLazyRestartAndDiscardsStaleReload()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri initialUrl = Url(20720);
            Uri reloadUrl = Url(20721);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            // A noncooperative provider ignores the manager-owned reload token and
            // stays blocked until released, so the reload cannot exit even after
            // the stop cancels it - proving a stale reload never blocks a restart.
            var reloadProvider = new CancellationIgnoringProvider(release.Task);
            var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            try
            {
                // Seed a lazy restart source and start on the initial endpoint.
                manager.ConfigureInitialStartup(BuildAppConfig(telemetry, initialUrl));
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == initialUrl && l.IsOpen),
                    Is.True);
                int watcherGeneration = manager.CurrentWatcherGeneration;

                // Enqueue a reload that blocks inside the provider (outside the
                // lifecycle gate), so it stays in-flight while the stop commits.
                manager.ConfigurationProvider = reloadProvider;
                Task reload = manager.ReloadConfigurationAsync(
                    BuildAppConfig(telemetry, reloadUrl),
                    watcherGeneration);
                await reloadProvider.Entered.ConfigureAwait(false);

                // A committed stop advances the teardown epoch and detaches the
                // in-flight reload; it must complete promptly even though the
                // reload provider is still blocked.
                await manager.StopServiceAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
                Assert.That(reload.IsCompleted, Is.False,
                    "the stale reload is still blocked in its noncooperative provider");

                // An immediate lazy restart must NOT await the detached stale
                // reload task: it reserves a fresh start and reopens the initial
                // endpoint without waiting for the blocked provider. Clear the
                // (still-blocked) provider so the restart uses the pass-through
                // one; the stale reload already captured its own provider call.
                manager.ConfigurationProvider = null;
                await manager.EnsureStartedAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == initialUrl && l.IsOpen),
                    Is.True,
                    "the lazy restart must reopen the initial endpoint");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == reloadUrl && l.IsOpen),
                    Is.False,
                    "the stale reload endpoint must never open");

                // Release the stale provider: its completion is discarded (the
                // captured lifecycle version/epoch is superseded), so it neither
                // reopens the reload endpoint nor restarts the drain.
                release.SetResult(true);
                await reload.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == reloadUrl && l.IsOpen),
                    Is.False,
                    "the released stale reload must have no effect");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == initialUrl && l.IsOpen),
                    Is.True,
                    "the restarted service must remain on the initial endpoint");
            }
            finally
            {
                release.TrySetResult(true);
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MatchingWaitClaimWinsRaceAndAcceptsTransport()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20730);
            const string serverUri = "urn:test:reverse:server";
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Winner order A: the matcher claims the waiter first (no shutdown
            // faulted it), so it accepts the transport and hands it to the waiter.
            TaskCompletionSource<ITransportWaitingConnection> tcs =
                manager.RegisterWaitForTest(url, serverUri);
            var e = new FakeConnectionWaitingEventArgs(serverUri, url);

            bool matched = manager.MatchWaitingConnectionForTest(e);

            Assert.That(matched, Is.True);
            Assert.That(e.Accepted, Is.True, "the winning claim must accept the transport");
            Assert.That(tcs.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(tcs.Task.Result, Is.SameAs(e), "the waiter must receive the claimed transport");
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "the claimed registration must be removed");
        }

        [Test]
        public async Task MatchingWaitClaimLosesToStopAndLeavesNoAcceptedOrphan()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri url = Url(20735);
            const string serverUri = "urn:test:reverse:server";
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            await manager.StartServiceAsync(ConfigFor(url)).ConfigureAwait(false);

            // Winner order B: a committed Stop/Dispose faulted the waiter first.
            TaskCompletionSource<ITransportWaitingConnection> tcs =
                manager.RegisterWaitForTest(url, serverUri);
            var shutdownError = new ServiceResultException(
                StatusCodes.BadInvalidState,
                "The reverse connect manager stopped serving waiting connections.");
            Assert.That(tcs.TrySetException(shutdownError), Is.True);

            // The matcher's claim under the registrations lock loses the race, so
            // it must NOT accept the transport: the listener reclaims/closes it.
            var e = new FakeConnectionWaitingEventArgs(serverUri, url);
            bool matched = manager.MatchWaitingConnectionForTest(e);

            Assert.That(matched, Is.False, "a lost claim must not report a match");
            Assert.That(
                e.Accepted,
                Is.False,
                "a lost claim must leave the transport unaccepted so the listener closes it");
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await tcs.Task.ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                manager.WaitingConnectionCountForTest,
                Is.Zero,
                "the dead registration must be removed by the matcher");
        }

        [Test]
        public async Task HostedCancellationAbortsExplicitStartJoinedByEnsureStarted()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new CancellableGateProvider();
            Uri url = Url(20700);
            Uri restartUrl = Url(20701);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            // A user starts the manager explicitly; the start blocks in the
            // (cancellable) provider gate before any listener binds.
            Task explicitStart = manager.StartServiceAsync(ConfigFor(url));
            await provider.Entered.ConfigureAwait(false);

            // A hosted StartAsync joins the in-flight explicit start via
            // EnsureStartedAsync and registers hosted cancellation on its token.
            var hosted = new ReverseConnectManagerHostedService(manager);
            using var hostCts = new CancellationTokenSource();
            Task hostedStart = hosted.StartAsync(hostCts.Token);

            // Cancelling the host token must abort the underlying explicit start
            // (through the manager-owned active-start token), not merely the
            // joining awaiter, so no listener ever binds.
            hostCts.Cancel();

            Assert.That(
                async () => await explicitStart.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(
                async () => await hostedStart.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(
                harness.OpenObserved.IsCompleted,
                Is.False,
                "no listener must bind when the joined explicit start is cancelled");
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(manager.CurrentStateForTest, Is.Not.EqualTo("Started"));

            // A later restart must not be spuriously cancelled: joining and
            // cancelling the explicit start must not leave a stale hosted-cancel
            // latch behind.
            manager.ConfigurationProvider = null;
            await manager.StartServiceAsync(ConfigFor(restartUrl)).ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == restartUrl && l.IsOpen),
                Is.True,
                "the later restart must bind its listener, proving it was not " +
                "spuriously cancelled");
        }

        [Test]
        public async Task CandidateConstructionFailureDisposesEarlierOwnedHostsOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri valid = Url(20710);
            var unsupported = new Uri("opc.unsupported://localhost:20711/reverse");
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // The second configured endpoint's listener creation fails
            // (unsupported scheme) after the first host was already created;
            // the discarded first host must be disposed, not merely closed.
            Assert.ThrowsAsync<ServiceResultException>(
                () => manager.StartServiceAsync(ConfigFor(valid, unsupported)));

            Assert.That(harness.Listeners, Has.Count.EqualTo(1));
            Assert.That(
                harness.Listeners[0].DisposeCount,
                Is.EqualTo(1),
                "an earlier owned candidate host must be disposed when a later " +
                "candidate construction fails");
            Assert.That(harness.Listeners[0].IsOpen, Is.False);
        }

        [Test]
        public async Task ReloadOpenFailureAndRestoreOpenFailureDisposesEveryDiscardedHostOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri original = Url(20720);
            Uri replacement = Url(20721);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(BuildAppConfig(telemetry, original))
                .ConfigureAwait(false);

            // The replacement fails to open AND the restore reopen of the
            // (freshly recreated) previous listener also fails, so the reload
            // faults with no listeners. Every discarded configured host - the
            // replaced original, the failed replacement candidate, and the
            // failed restore host - must be disposed exactly once.
            var error = new ServiceResultException(StatusCodes.BadNoCommunication);
            harness.FailOpenFor(replacement, error);
            harness.FailOpenFor(original, error);
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Faulted"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(
                harness.Listeners.All(l => l.DisposeCount == 1),
                Is.True,
                "every discarded configured host must be disposed exactly once");
        }

        [Test]
        public async Task DelayedCancelPendingStartAfterCanceledStartDoesNotPoisonFreshStart()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            var provider = new CancellableGateProvider();
            Uri url = Url(20730);
            Uri restartUrl = Url(20731);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry,
                ConfigurationProvider = provider
            };

            // An explicit start blocks in the cancellable provider gate before
            // any listener binds and is then cancelled through its own token.
            using var cts = new CancellationTokenSource();
            Task explicitStart = manager.StartServiceAsync(ConfigFor(url), cts.Token);
            await provider.Entered.ConfigureAwait(false);
            cts.Cancel();
            Assert.That(
                async () => await explicitStart.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            // A late/stale hosted cancellation fires AFTER the canceled start
            // completed and while no hosted startup scope is active. It must not
            // latch (there is no pending hosted startup publication), so a later
            // start is never spuriously cancelled.
            manager.CancelPendingStart();

            manager.ConfigurationProvider = null;
            await manager.StartServiceAsync(ConfigFor(restartUrl)).ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == restartUrl && l.IsOpen),
                Is.True,
                "the fresh start must bind its listener, proving the delayed " +
                "cancellation did not poison it");
        }

        [Test]
        public async Task CanceledStopRestoreReopenFailureDisposesConfiguredHostsOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri first = Url(20740);
            Uri second = Url(20741);
            using var cts = new CancellationTokenSource();
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            await manager.StartServiceAsync(ConfigFor(first, second)).ConfigureAwait(false);
            FakeReverseListener firstListener =
                harness.Listeners.Single(l => l.OpenedUrl == first);
            FakeReverseListener secondListener =
                harness.Listeners.Single(l => l.OpenedUrl == second);

            // The first host closes successfully but its restore reopen fails;
            // cancelling the caller token while the first close completes makes
            // the stop's remaining close throw OperationCanceledException so the
            // stop rolls back through the restore path.
            harness.FailOpenFor(first, new ServiceResultException(StatusCodes.BadNoCommunication));
            harness.SetCloseGate(first, _ =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });

            Assert.That(
                async () => await manager.StopServiceAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            // The restore reopen failed, so the manager faults with no listeners.
            // Both discarded configured hosts must be disposed exactly once
            // rather than leaked when the references are cleared.
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Faulted"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(
                firstListener.DisposeCount,
                Is.EqualTo(1),
                "a discarded configured host must be disposed once on restore failure");
            Assert.That(
                secondListener.DisposeCount,
                Is.EqualTo(1),
                "a discarded configured host must be disposed once on restore failure");
        }

        [Test]
        public async Task FailedReloadRestoreRecreateFactoryThrowDisposesEarlierRecreatedHostOnce()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri first = Url(20750);
            Uri second = Url(20751);
            Uri replacement = Url(20752);
            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // Start with two configured endpoints (creates listeners #1 and #2).
            await manager.StartServiceAsync(BuildAppConfig(telemetry, first, second))
                .ConfigureAwait(false);

            // Reload to a single replacement (creates listener #3) that fails to
            // open, forcing the restore of the previous two descriptors. During
            // the restore the first previous host is recreated (listener #4) and
            // the second recreation (create #5) throws. The transactional
            // reconstruction must dispose the already-recreated host before
            // rethrowing so no partial reconstruction leaks.
            harness.FailOpenFor(replacement, new ServiceResultException(StatusCodes.BadNoCommunication));
            harness.ThrowOnCreateNumber(5);
            await manager.ReloadConfigurationAsync(
                BuildAppConfig(telemetry, replacement),
                manager.CurrentWatcherGeneration).ConfigureAwait(false);

            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Faulted"));
            Assert.That(harness.Listeners.Any(l => l.IsOpen), Is.False);
            Assert.That(
                harness.Listeners.All(l => l.DisposeCount == 1),
                Is.True,
                "every created host - including the partially recreated restore " +
                "host - must be disposed exactly once");
        }

        [Test]
        public async Task CloseFailingManualEndpointStopThenRestartOpensFreshListener()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(Scheme);
            Uri manual = Url(20760);
            int armed = 1;

            // The manual host's first close fails destructively (a non-cancellation
            // failure), which makes ReverseConnectHost.CloseAsync dispose and null
            // its listener, leaving the persistent manual host unusable.
            harness.SetCloseGate(manual, _ =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    return Task.FromException(
                        new InvalidOperationException("destructive manual close"));
                }
                return Task.CompletedTask;
            });

            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };
            manager.AddEndpoint(manual);

            await manager.StartServiceAsync(ConfigFor()).ConfigureAwait(false);
            FakeReverseListener original = harness.Listeners.Single(l => l.OpenedUrl == manual);
            Assert.That(original.IsOpen, Is.True);

            // Stop: the destructive close destroys the manual host's listener.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                original.DisposeCount,
                Is.GreaterThanOrEqualTo(1),
                "the destructive close must dispose the manual listener");

            // Restart: the manager must recreate the unusable manual host from
            // the endpoint and open a FRESH listener rather than reusing the
            // destroyed one (which OpenAsync would reject).
            await manager.StartServiceAsync(ConfigFor()).ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));
            Assert.That(
                harness.Listeners.Any(l =>
                    l.OpenedUrl == manual && l.IsOpen && !ReferenceEquals(l, original)),
                Is.True,
                "a freshly created manual listener must open after the destructive close");
        }

        [Test]
        public async Task RecreatedManualWssHostReusesOriginalAddEndpointConfigurationAsync()
        {
            const string wssScheme = Utils.UriSchemeOpcWss;
            ITelemetryContext telemetry = CreateTelemetry();
            var harness = new FakeListenerHarness(wssScheme);
            var manual = new Uri(wssScheme + "://localhost:20800/reverse");

            // Config A supplies the TLS context (server certificate registry +
            // validator) to AddEndpoint. Config B is a DIFFERENT application
            // configuration used to restart the manager after the manual host is
            // destroyed - it must never be substituted for config A on the
            // recreated manual WSS host.
            ICertificateManager registryA = Mock.Of<ICertificateManager>();
            ICertificateManager registryB = Mock.Of<ICertificateManager>();
            ApplicationConfiguration configA = BuildAppConfigWithCertificateManager(
                telemetry,
                registryA);
            ApplicationConfiguration configB = BuildAppConfigWithCertificateManager(
                telemetry,
                registryB);

            // The manual host's first close fails destructively (a
            // non-cancellation failure), which makes ReverseConnectHost.CloseAsync
            // dispose and null its listener, leaving the persistent manual host
            // unusable so a restart must recreate it.
            int armed = 1;
            harness.SetCloseGate(manual, _ =>
            {
                if (Interlocked.Exchange(ref armed, 0) == 1)
                {
                    return Task.FromException(
                        new InvalidOperationException("destructive manual close"));
                }
                return Task.CompletedTask;
            });

            await using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = harness.Registry
            };

            // Add a manual opc.wss endpoint with config A: its CertificateManager
            // is plumbed into the listener as the server certificate registry and
            // validator because the endpoint terminates TLS.
            manager.AddEndpoint(manual, configA);

            await manager.StartServiceAsync(ConfigFor()).ConfigureAwait(false);
            FakeReverseListener original = harness.Listeners.Single(l => l.OpenedUrl == manual);
            Assert.That(original.IsOpen, Is.True);
            Assert.That(original.OpenedServerCertificates, Is.SameAs(registryA));
            Assert.That(original.OpenedCertificateValidator, Is.SameAs(registryA));

            // Stop: the destructive close destroys the manual host's listener.
            await manager.StopServiceAsync().ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));
            Assert.That(
                original.DisposeCount,
                Is.GreaterThanOrEqualTo(1),
                "the destructive close must dispose the manual listener");

            // Restart with a DIFFERENT application configuration (config B). The
            // recreated manual WSS host must still use config A's TLS context -
            // the one originally supplied to AddEndpoint - not config B's.
            await manager.StartServiceAsync(configB).ConfigureAwait(false);
            Assert.That(manager.CurrentStateForTest, Is.EqualTo("Started"));

            FakeReverseListener recreated = harness.Listeners.Single(l =>
                l.OpenedUrl == manual && l.IsOpen && !ReferenceEquals(l, original));
            Assert.That(
                recreated.OpenedServerCertificates,
                Is.SameAs(registryA),
                "the recreated manual host must reuse config A's certificate registry");
            Assert.That(
                recreated.OpenedCertificateValidator,
                Is.SameAs(registryA),
                "the recreated manual host must reuse config A's certificate validator");
            Assert.That(
                recreated.OpenedServerCertificates,
                Is.Not.SameAs(registryB),
                "the restart configuration must not override the original TLS context");
        }

        private static ApplicationConfiguration BuildAppConfigWithCertificateManager(
            ITelemetryContext telemetry,
            ICertificateManager certificateManager)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "Test",
                CertificateManager = certificateManager
            };
        }

        private static async Task WaitForAsync(Func<bool> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail(message);
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private static string NewConfigFile()
        {
            string path = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "rcm_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".cfg");
            System.IO.File.WriteAllText(path, "seed");
            return path;
        }

        private static ApplicationConfiguration BuildAppConfigWithFile(
            ITelemetryContext telemetry,
            string sourceFilePath,
            params Uri[] urls)
        {
            ApplicationConfiguration config = BuildAppConfig(telemetry, urls);
            typeof(ApplicationConfiguration)
                .GetProperty(nameof(ApplicationConfiguration.SourceFilePath))!
                .SetValue(config, sourceFilePath);
            return config;
        }

        private static ApplicationConfiguration BuildAppConfig(
            ITelemetryContext telemetry,
            params Uri[] urls)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "Test",
                ClientConfiguration = new ClientConfiguration
                {
                    ReverseConnect = ConfigFor(urls)
                }
            };
        }

        private sealed class ReentrantStartProvider : IReverseConnectConfigurationProvider
        {
            private readonly ReverseConnectManager m_manager;
            private readonly bool m_indirectRegistration;

            public ReentrantStartProvider(
                ReverseConnectManager manager,
                bool indirectRegistration = false)
            {
                m_manager = manager;
                m_indirectRegistration = indirectRegistration;
            }

            public int ReentrantAttempts { get; private set; }

            public Exception? ReentrantError { get; private set; }

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                ReentrantAttempts++;
                try
                {
                    if (m_indirectRegistration)
                    {
                        // Cross an await boundary so the AsyncLocal owner marker
                        // must flow through the yield, then re-enter indirectly
                        // via the async registration entry point.
                        await Task.Yield();
                        await m_manager.RegisterWaitingConnectionAsync(
                            new Uri(Scheme + "://localhost:1/reentrant"),
                            null,
                            (sender, e) => { },
                            ReverseConnectManager.ReverseConnectStrategy.Once,
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await m_manager.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    // Record the fail-fast result and let the real startup
                    // proceed so the manager still reaches Started.
                    ReentrantError = e;
                }
                return configuration;
            }
        }

        private sealed class ReentrantDisposeProvider : IReverseConnectConfigurationProvider
        {
            private readonly ReverseConnectManager m_manager;

            public ReentrantDisposeProvider(ReverseConnectManager manager)
            {
                m_manager = manager;
            }

            public Exception? ReentrantError { get; private set; }

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                try
                {
                    // Cross an await boundary so the AsyncLocal startup-owner
                    // marker must flow through the yield, then re-enter
                    // DisposeAsync on the same in-flight startup flow. It must
                    // fail fast rather than deadlock on the shared teardown that
                    // drains this very start.
                    await Task.Yield();
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ReentrantError = e;
                }
                return configuration;
            }
        }

        private sealed class NestedManagerStartingProvider : IReverseConnectConfigurationProvider
        {
            private readonly ReverseConnectManager m_nested;

            public NestedManagerStartingProvider(ReverseConnectManager nested)
            {
                m_nested = nested;
            }

            public bool NestedStarted { get; private set; }

            public Exception? NestedError { get; private set; }

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                try
                {
                    // Nested unrelated manager: deliberately not tied to the
                    // outer manager's start token.
                    await m_nested.EnsureStartedAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    NestedStarted = true;
                }
                catch (Exception e)
                {
                    NestedError = e;
                }
                return configuration;
            }
        }

        private sealed class ReentrantLegacyHookManager : ReverseConnectManager
        {
            public ReentrantLegacyHookManager(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public Exception? ReentrantError { get; private set; }

            protected override void OnUpdateConfiguration(
                ReverseConnectClientConfiguration configuration)
            {
                try
                {
                    // Re-enter the startup from within the legacy adaptation
                    // hook. The fail-fast guard makes EnsureStartedAsync throw
                    // synchronously (a completed, faulted task), so observing it
                    // here never blocks the in-flight preparation.
                    EnsureStartedAsync().GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    ReentrantError = e;
                }
                base.OnUpdateConfiguration(configuration);
            }
        }

        private sealed class CancellationIgnoringProvider : IReverseConnectConfigurationProvider
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Task<bool> m_release;

            public CancellationIgnoringProvider(Task<bool> release)
            {
                m_release = release;
            }

            public Task<bool> Entered => m_entered.Task;

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(true);
                // Deliberately ignore the cancellation token: await an
                // unrelated release and return normally even after the token
                // has been cancelled (a non-cooperative provider).
                await m_release.ConfigureAwait(false);
                return configuration;
            }
        }

        private sealed class CancellableGateProvider : IReverseConnectConfigurationProvider
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<bool> Entered => m_entered.Task;

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return configuration;
            }
        }

        private sealed class TokenRegisteringProvider : IReverseConnectConfigurationProvider
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Task<bool> m_release;

            public TokenRegisteringProvider(Task<bool> release)
            {
                m_release = release;
            }

            public Task<bool> Entered => m_entered.Task;

            /// <summary>
            /// The exception observed while linking/registering on the token,
            /// if any. Remains <c>null</c> when the token was cancelled but not
            /// disposed (the expected, non-throwing case).
            /// </summary>
            public Exception? RegistrationError { get; private set; }

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(true);
                // Wait until a stop/dispose has cancelled the manager-owned
                // token, then use it the way a real provider would. Linking or
                // registering on a token whose source was disposed throws
                // ObjectDisposedException; a merely cancelled source does not.
                await m_release.ConfigureAwait(false);
                try
                {
                    using CancellationTokenSource linked =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    using CancellationTokenRegistration reg =
                        cancellationToken.Register(static () => { });
                }
                catch (ObjectDisposedException ex)
                {
                    RegistrationError = ex;
                    throw;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return configuration;
            }
        }

        private sealed class LegacyDisposingManager : ReverseConnectManager
        {
            private int m_disposeBoolCount;

            public LegacyDisposingManager(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public int DisposeBoolCount => Volatile.Read(ref m_disposeBoolCount);

            public bool LastDisposing { get; private set; }

            protected override void Dispose(bool disposing)
            {
                Interlocked.Increment(ref m_disposeBoolCount);
                LastDisposing = disposing;
                base.Dispose(disposing);
            }
        }

        private sealed class SuppressingManager : ReverseConnectManager
        {
            public SuppressingManager(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            protected override void OnUpdateConfiguration(
                ReverseConnectClientConfiguration configuration)
            {
                // Intentionally omit base. This no longer suppresses the
                // update: the candidate is activated unchanged.
            }
        }

        private sealed class MutatingManager : ReverseConnectManager
        {
            private readonly Uri m_injected;

            public MutatingManager(ITelemetryContext telemetry, Uri injected)
                : base(telemetry)
            {
                m_injected = injected;
            }

            protected override void OnUpdateConfiguration(
                ReverseConnectClientConfiguration configuration)
            {
                configuration.ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                    new[]
                    {
                        new ReverseConnectClientEndpoint
                        {
                            EndpointUrl = m_injected.ToString()
                        }
                    });
                base.OnUpdateConfiguration(configuration);
            }
        }

        private sealed class RejectingManager : ReverseConnectManager
        {
            public RejectingManager(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            protected override void OnUpdateConfiguration(
                ReverseConnectClientConfiguration configuration)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Rejected by override.");
            }
        }

        private sealed class RecordingProvider : IReverseConnectConfigurationProvider
        {
            public ReverseConnectClientConfiguration? LastInput { get; private set; }

            public ApplicationConfiguration? LastContext { get; private set; }

            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                LastContext = applicationConfiguration;
                LastInput = configuration;
                return new ValueTask<ReverseConnectClientConfiguration>(configuration);
            }
        }

        private sealed class ReplacingWithoutBaseManager : ReverseConnectManager
        {
            private readonly Uri m_injected;

            public ReplacingWithoutBaseManager(ITelemetryContext telemetry, Uri injected)
                : base(telemetry)
            {
                m_injected = injected;
            }

            protected override void OnUpdateConfiguration(
                ReverseConnectClientConfiguration configuration)
            {
                // Replace the candidate's endpoints in place and omit base; the
                // replacement must still be activated.
                configuration.ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                    new[]
                    {
                        new ReverseConnectClientEndpoint
                        {
                            EndpointUrl = m_injected.ToString()
                        }
                    });
            }
        }

        /// <summary>
        /// Records the reverse-connect candidate the manager hands it on every
        /// invocation (endpoint count and hold/wait timeouts) and then damages
        /// that candidate in place - clearing its endpoints and scrambling its
        /// timeouts - so a naive reuse of the cached configuration would carry the
        /// damage into the next start's input. It returns a fresh, valid
        /// configuration (the intended option endpoint) as the effective result
        /// so the start still opens its listener. Optionally throws on the first
        /// call to exercise the failed-start retry path.
        /// </summary>
        private sealed class MutatingRecordingProvider : IReverseConnectConfigurationProvider
        {
            private readonly Uri m_url;
            private readonly int m_holdTime;
            private int m_calls;

            public MutatingRecordingProvider(Uri url, int holdTime)
            {
                m_url = url;
                m_holdTime = holdTime;
            }

            public bool ThrowOnFirstCall { get; set; }

            public List<int> ObservedEndpointCounts { get; } = [];

            public List<int> ObservedHoldTimes { get; } = [];

            public List<int> ObservedWaitTimeouts { get; } = [];

            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                ObservedEndpointCounts.Add(
                    configuration.ClientEndpoints.IsNull
                        ? 0
                        : configuration.ClientEndpoints.Count);
                ObservedHoldTimes.Add(configuration.HoldTime);
                ObservedWaitTimeouts.Add(configuration.WaitTimeout);

                // Damage the shared candidate in place: without a reapplied
                // overlay decorator the next start would observe this damage.
                configuration.ClientEndpoints =
                    new ArrayOf<ReverseConnectClientEndpoint>(
                        Array.Empty<ReverseConnectClientEndpoint>());
                configuration.HoldTime = -1;
                configuration.WaitTimeout = -1;

                if (ThrowOnFirstCall && ++m_calls == 1)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Preparation rejected on the first attempt.");
                }

                // Return a fresh, valid effective configuration so the start
                // opens the intended option endpoint.
                return new ValueTask<ReverseConnectClientConfiguration>(
                    new ReverseConnectClientConfiguration
                    {
                        ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                            new[]
                            {
                                new ReverseConnectClientEndpoint
                                {
                                    EndpointUrl = m_url.ToString()
                                }
                            }),
                        HoldTime = m_holdTime
                    });
            }
        }

        private sealed class EmptyingProvider : IReverseConnectConfigurationProvider
        {
            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                // Explicit suppression: drop all endpoints.
                return new ValueTask<ReverseConnectClientConfiguration>(
                    new ReverseConnectClientConfiguration());
            }
        }

        private sealed class CancellationObservingProvider : IReverseConnectConfigurationProvider
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_canceled =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<bool> Entered => m_entered.Task;

            /// <summary>
            /// Completes once the provider observes cancellation of the
            /// manager-owned start token, proving a stop/dispose cancelled the
            /// in-flight explicit start.
            /// </summary>
            public Task<bool> Canceled => m_canceled.Task;

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    m_canceled.TrySetResult(true);
                    throw;
                }
                return configuration;
            }
        }

        private sealed class GatedProvider : IReverseConnectConfigurationProvider
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Task<bool> m_release;

            public GatedProvider(Task<bool> release)
            {
                m_release = release;
            }

            public Task<bool> Entered => m_entered.Task;

            public async ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(true);
                await m_release.ConfigureAwait(false);
                return configuration;
            }
        }

        private sealed class ActiveConfigProbingProvider : IReverseConnectConfigurationProvider
        {
            private readonly ReverseConnectManager m_manager;

            public ActiveConfigProbingProvider(ReverseConnectManager manager)
            {
                m_manager = manager;
            }

            public ApplicationConfiguration? ObservedDuringPrepare { get; private set; }

            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                ObservedDuringPrepare = m_manager.ActiveApplicationConfigurationForTest;
                return new ValueTask<ReverseConnectClientConfiguration>(configuration);
            }
        }

        private sealed class ToggleThrowingProvider : IReverseConnectConfigurationProvider
        {
            public bool Throw { get; set; }

            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                if (Throw)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Preparation rejected.");
                }
                return new ValueTask<ReverseConnectClientConfiguration>(configuration);
            }
        }

        private sealed class ReloadObservingManager : ReverseConnectManager
        {
            private readonly TaskCompletionSource<bool> m_reloadObserved;

            public ReloadObservingManager(
                ITelemetryContext telemetry,
                TaskCompletionSource<bool> reloadObserved)
                : base(telemetry)
            {
                m_reloadObserved = reloadObserved;
            }

            protected override async void OnConfigurationChangedAsync(
                object? sender,
                ConfigurationWatcherEventArgs args)
            {
                // Observe the scheduled reload without loading the file so the
                // test stays deterministic.
                await Task.Yield();
                m_reloadObserved.TrySetResult(true);
            }
        }

        /// <summary>
        /// A minimal <see cref="ConnectionWaitingEventArgs"/> fake exposing a
        /// public constructor and a transport handle, so a test can drive the
        /// waiting-connection matcher directly without a real listener.
        /// </summary>
        private sealed class FakeConnectionWaitingEventArgs : ConnectionWaitingEventArgs
        {
            public FakeConnectionWaitingEventArgs(string serverUri, Uri endpointUrl)
                : base(serverUri, endpointUrl)
            {
            }

            public override object Handle { get; } = new object();
        }

        private sealed class FakeListenerHarness
        {
            public FakeListenerHarness(string scheme)
            {
                Scheme = scheme;
                var factory = new Mock<ITransportListenerFactory>();
                factory.SetupGet(f => f.UriScheme).Returns(scheme);
                factory
                    .Setup(f => f.Create(It.IsAny<ITelemetryContext>()))
                    .Returns(() =>
                    {
                        int n = Interlocked.Increment(ref m_createCount);
                        if (n == Volatile.Read(ref m_throwOnCreateNumber))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInternalError,
                                "factory create failed");
                        }
                        var listener = new FakeReverseListener(this);
                        lock (m_listeners)
                        {
                            m_listeners.Add(listener);
                        }
                        return listener;
                    });
                var registry = new DefaultTransportBindingRegistry();
                registry.RegisterListenerFactory(factory.Object);
                Registry = registry;
            }

            public string Scheme { get; }

            public ITransportBindingRegistry Registry { get; }

            public IReadOnlyList<FakeReverseListener> Listeners
            {
                get
                {
                    lock (m_listeners)
                    {
                        return [.. m_listeners];
                    }
                }
            }

            private readonly List<FakeReverseListener> m_listeners = [];

            private int m_createCount;
            private int m_throwOnCreateNumber = -1;

            /// <summary>
            /// Arms the listener factory to throw on the <paramref name="oneBasedOrdinal"/>-th
            /// listener creation (counted across the whole harness lifetime), so
            /// a transactional reconstruction path can be proven to dispose the
            /// hosts it already created before rethrowing.
            /// </summary>
            public void ThrowOnCreateNumber(int oneBasedOrdinal)
            {
                Volatile.Write(ref m_throwOnCreateNumber, oneBasedOrdinal);
            }

            private readonly ConcurrentDictionary<Uri, Exception> m_openFailures = new();
            private readonly ConcurrentDictionary<Uri, Func<CancellationToken, Task>> m_openGates =
                new();
            private readonly ConcurrentDictionary<Uri, Func<CancellationToken, Task>> m_closeGates =
                new();
            private readonly TaskCompletionSource<bool> m_openObserved =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void FailOpenFor(Uri url, Exception error)
            {
                m_openFailures[url] = error;
            }

            public void SetOpenGate(Uri url, Func<CancellationToken, Task> gate)
            {
                m_openGates[url] = gate;
            }

            public void SetCloseGate(Uri url, Func<CancellationToken, Task> gate)
            {
                m_closeGates[url] = gate;
            }

            public Task<bool> OpenObserved => m_openObserved.Task;

            public List<Uri> OpenedUrls()
            {
                return Listeners
                    .Where(l => l.OpenCount > 0 && l.OpenedUrl != null)
                    .Select(l => l.OpenedUrl!)
                    .ToList();
            }

            public async ValueTask OnOpenAsync(Uri url, CancellationToken ct)
            {
                m_openObserved.TrySetResult(true);
                if (m_openGates.TryGetValue(url, out Func<CancellationToken, Task>? gate))
                {
                    await gate(ct).ConfigureAwait(false);
                }
                if (m_openFailures.TryGetValue(url, out Exception? error))
                {
                    throw error;
                }
            }

            public async ValueTask OnCloseAsync(Uri url, CancellationToken ct)
            {
                if (m_closeGates.TryGetValue(url, out Func<CancellationToken, Task>? gate))
                {
                    await gate(ct).ConfigureAwait(false);
                }
            }
        }

#pragma warning disable CS0067 // events are subscribed by ReverseConnectHost, never raised here
        private sealed class FakeReverseListener : ITransportListener
        {
            private readonly FakeListenerHarness m_harness;

            public FakeReverseListener(FakeListenerHarness harness)
            {
                m_harness = harness;
            }

            public string ListenerId { get; } =
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

            public string UriScheme => m_harness.Scheme;

            public Uri? OpenedUrl { get; private set; }

            public int OpenCount;

            public int CloseCount;

            public int DisposeCount;

            private int m_isOpen;

            /// <summary>
            /// True while the listener is currently open (opened and not yet
            /// closed). Assertions inspect this rather than the ever-open
            /// <see cref="OpenCount"/> so restoration/survival is proven by the
            /// active state.
            /// </summary>
            public bool IsOpen => Volatile.Read(ref m_isOpen) != 0;

            /// <summary>
            /// The server certificate registry supplied through
            /// <see cref="TransportListenerSettings.ServerCertificates"/> on the
            /// last open. Tests inspect it to prove which application
            /// configuration (TLS context) the listener was created from.
            /// </summary>
            public ICertificateRegistry? OpenedServerCertificates { get; private set; }

            /// <summary>
            /// The certificate validator supplied through
            /// <see cref="TransportListenerSettings.CertificateValidator"/> on
            /// the last open.
            /// </summary>
            public ICertificateValidatorEx? OpenedCertificateValidator { get; private set; }

            public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

            public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

            public async ValueTask OpenAsync(
                Uri baseAddress,
                TransportListenerSettings settings,
                ITransportListenerCallback callback,
                CancellationToken ct = default)
            {
                OpenedUrl = baseAddress;
                OpenedServerCertificates = settings.ServerCertificates;
                OpenedCertificateValidator = settings.CertificateValidator;
                Interlocked.Increment(ref OpenCount);
                await m_harness.OnOpenAsync(baseAddress, ct).ConfigureAwait(false);
                Volatile.Write(ref m_isOpen, 1);
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                Interlocked.Increment(ref CloseCount);
                if (OpenedUrl != null)
                {
                    return CloseWithGateAsync(OpenedUrl, ct);
                }
                Volatile.Write(ref m_isOpen, 0);
                return default;
            }

            private async ValueTask CloseWithGateAsync(Uri url, CancellationToken ct)
            {
                await m_harness.OnCloseAsync(url, ct).ConfigureAwait(false);
                Volatile.Write(ref m_isOpen, 0);
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
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref DisposeCount);
                return default;
            }
        }
#pragma warning restore CS0067
    }
}
