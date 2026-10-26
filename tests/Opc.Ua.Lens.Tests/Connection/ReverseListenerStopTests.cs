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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ReverseListenerStopTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PendingRuntimeStopRejectsPrimaryWorkBeforeAcquiringConfiguration(bool connect)
    {
        var context = new StopContext();
        await using (context.ConfigureAwait(false))
        {
            await context.StartListenerAsync().ConfigureAwait(false);
            await Assert.ThatAsync(() => context.AttemptAsync(connect: false),
                Throws.Exception.SameAs(context.ConfigurationFailure)).ConfigureAwait(false);
            int configurationsBeforeStop = context.ConfigurationAttempts;
            using var cancellation = new CancellationTokenSource();
            Task stop = context.Service.StopReverseListenerAsync();
            Task? attempt = null;
            try
            {
                await context.Runtime.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(stop.IsCompleted, Is.False);

                attempt = context.AttemptAsync(connect, cancellation.Token);
                await Assert.ThatAsync(() => attempt.WaitAsync(TimeSpan.FromSeconds(5)),
                    Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);

                Assert.That(context.ConfigurationAttempts, Is.EqualTo(configurationsBeforeStop),
                    "Admission must fail before acquiring another configuration or a network lease.");
                Assert.That(context.Runtime.NetworkRequests, Is.Zero);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                context.Runtime.ReleaseStop.TrySetResult();
                await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (attempt is not null)
                {
                    await ObserveAttemptAsync(attempt).ConfigureAwait(false);
                }
            }
        }
    }

    [TestCase(false, "success")]
    [TestCase(true, "success")]
    [TestCase(false, "cancellation")]
    [TestCase(true, "cancellation")]
    [TestCase(false, "failure")]
    [TestCase(true, "failure")]
    public async Task PrimaryWorkCanRegisterAfterListenerStopEnds(bool connect, string outcome)
    {
        var context = new StopContext();
        await using (context.ConfigureAwait(false))
        {
            await context.StartListenerAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task stop = context.Service.StopReverseListenerAsync(cancellation.Token);
            try
            {
                await context.Runtime.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (outcome == "cancellation")
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    await Assert.ThatAsync(() => stop.WaitAsync(TimeSpan.FromSeconds(5)),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else if (outcome == "failure")
                {
                    context.Runtime.StopFailure = new InvalidOperationException("Controlled listener stop failure.");
                    context.Runtime.ReleaseStop.TrySetResult();
                    await Assert.ThatAsync(() => stop.WaitAsync(TimeSpan.FromSeconds(5)),
                        Throws.Exception.SameAs(context.Runtime.StopFailure)).ConfigureAwait(false);
                }
                else
                {
                    context.Runtime.ReleaseStop.TrySetResult();
                    await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }

                await Assert.ThatAsync(() => context.AttemptAsync(connect).WaitAsync(TimeSpan.FromSeconds(5)),
                    Throws.Exception.SameAs(context.ConfigurationFailure)).ConfigureAwait(false);
                Assert.That(context.ConfigurationAttempts, Is.EqualTo(1),
                    "The next primary operation must reach the configuration boundary after stop finishes.");
                Assert.That(context.Runtime.NetworkRequests, Is.Zero);
            }
            finally
            {
                context.Runtime.ReleaseStop.TrySetResult();
                await ObserveAttemptAsync(stop).ConfigureAwait(false);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AlreadyCanceledStopLeavesInFlightPrimaryWorkUntouched(bool connect)
    {
        var context = new StopContext { PauseConfiguration = true };
        await using (context.ConfigureAwait(false))
        {
            await context.StartListenerAsync().ConfigureAwait(false);
            Task attempt = context.AttemptAsync(connect);
            try
            {
                await context.ConfigurationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.StopReverseListenerAsync(new CancellationToken(canceled: true)),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

                Assert.That(context.ConfigurationToken.IsCancellationRequested, Is.False,
                    "A pre-canceled stop must not cancel somebody else's primary operation.");
                Assert.That(attempt.IsCompleted, Is.False);
                Assert.That(context.Runtime.StopEntered.Task.IsCompleted, Is.False);
                context.ReleaseConfiguration.TrySetResult();
                await Assert.ThatAsync(() => attempt.WaitAsync(TimeSpan.FromSeconds(5)),
                    Throws.Exception.SameAs(context.ConfigurationFailure)).ConfigureAwait(false);
            }
            finally
            {
                context.ReleaseConfiguration.TrySetResult();
                await ObserveAttemptAsync(attempt).ConfigureAwait(false);
            }
        }
    }

    private static async Task ObserveAttemptAsync(Task attempt)
    {
        try
        {
            await attempt.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    private sealed class StopContext : IAsyncDisposable
    {
        public StopContext()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            Reverse = new ReverseConnectionService(Runtime);
            Backend = new StackConnectionBackend(
                telemetry, CreateConfigurationAsync, reverseConnections: Reverse);
            Service = new ConnectionService(telemetry, null, Backend, new ProfileCredentialProvider());
        }

        public GatedReverseRuntime Runtime { get; } = new();

        public ReverseConnectionService Reverse { get; }

        public StackConnectionBackend Backend { get; }

        public ConnectionService Service { get; }

        public InvalidOperationException ConfigurationFailure { get; } =
            new("Configuration boundary reached; no network access is permitted by this fixture.");

        public bool PauseConfiguration { get; init; }

        public int ConfigurationAttempts => Volatile.Read(ref m_configurationAttempts);

        public CancellationToken ConfigurationToken { get; private set; }

        public TaskCompletionSource ConfigurationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseConfiguration { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartListenerAsync()
        {
            return Reverse.StartAsync(new ReverseConnectionProfile
            {
                ListenerUrl = "opc.tcp://localhost:4841",
                ServerUri = "urn:expected-server",
                EndpointUrl = "opc.tcp://server.example.test:4840/Factory"
            });
        }

        public Task AttemptAsync(bool connect, CancellationToken ct = default)
        {
            return connect
                ? Service.ConnectAsync(new ConnectionOptions
                {
                    EndpointUrl = "opc.tcp://localhost:4850/primary",
                    UseSecurity = false
                }, ct)
                : Service.DiscoverEndpointsAsync(
                    new ConnectionSetupSelection("opc.tcp://localhost:4850/primary"), ct);
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseConfiguration.TrySetResult();
            Runtime.ReleaseStop.TrySetResult();
            await Service.DisposeAsync().ConfigureAwait(false);
            await Backend.DisposeAsync().ConfigureAwait(false);
            await Reverse.DisposeAsync().ConfigureAwait(false);
        }

        private Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref m_configurationAttempts);
            ConfigurationToken = ct;
            ConfigurationEntered.TrySetResult();
            return PauseConfiguration
                ? WaitForConfigurationReleaseAsync(ct)
                : Task.FromException<ApplicationConfiguration>(ConfigurationFailure);
        }

        private async Task<ApplicationConfiguration> WaitForConfigurationReleaseAsync(CancellationToken ct)
        {
            await ReleaseConfiguration.Task.WaitAsync(ct).ConfigureAwait(false);
            throw ConfigurationFailure;
        }

        private int m_configurationAttempts;
    }

    private sealed class GatedReverseRuntime : IReverseConnectionRuntimeFactory, IReverseConnectionRuntime
    {
        public ReverseConnectManager Manager
        {
            get
            {
                Interlocked.Increment(ref m_networkRequests);
                throw new InvalidOperationException("No network lease may be acquired in this fixture.");
            }
        }

        public int NetworkRequests => Volatile.Read(ref m_networkRequests);

        public InvalidOperationException? StopFailure { get; set; }

        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReverseConnectionRuntime Create(ReverseConnectionProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return this;
        }

        public Task StartAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<ITransportWaitingConnection> WaitAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref m_networkRequests);
            throw new InvalidOperationException("No reverse connection may be acquired in this fixture.");
        }

        public async Task StopAsync(CancellationToken ct)
        {
            StopEntered.TrySetResult();
            await ReleaseStop.Task.WaitAsync(ct).ConfigureAwait(false);
            if (StopFailure is not null)
            {
                throw StopFailure;
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private int m_networkRequests;
    }
}
