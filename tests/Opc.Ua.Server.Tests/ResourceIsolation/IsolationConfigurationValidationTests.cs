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
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises startup diagnostics without opening listeners or replacing host-owned policies.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class IsolationConfigurationValidationTests
    {
        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1_200_000_001L)]
        public void RuntimePlanNamesInvalidHandshakeTimeout(long ticks)
        {
            ServerResourceIsolationOptions options = Options();
            options.HandshakeTimeout = TimeSpan.FromTicks(ticks);

            Assert.That(() => options.CreateRuntimePlan(Configuration(), new ServerRateLimitOptions()),
                Throws.ArgumentException
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo(nameof(ServerResourceIsolationOptions.HandshakeTimeout))
                    .And.Message.Contains("positive")
                    .And.Message.Contains("two minutes"));
        }

        [Test]
        public async Task StartupRejectsInvalidTimeoutWithoutInstallingOrDisposingProvidersAsync(
            [Values] ServerResourceIsolationMode mode,
            [Values(-1L, 0L, 1_200_000_001L)] long ticks,
            [Values] bool customProvider)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var isolation = new Mock<IServerResourceIsolationProvider>(MockBehavior.Strict);
            Mock<IDisposable> disposable = isolation.As<IDisposable>();
            var rate = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            ChunkReassemblyBudget? budget = customProvider ? new ChunkReassemblyBudget(100, 0) : null;
            ServerResourceIsolationOptions options = Options();
            options.Mode = mode;
            options.HandshakeTimeout = TimeSpan.FromTicks(ticks);

            await using (var server = new StandardServer(telemetry, new FakeTimeProvider())
            {
                ResourceIsolationOptions = options,
                ResourceIsolationProvider = customProvider ? isolation.Object : null,
                ChunkReassemblyBudget = budget,
                RateLimiterProvider = rate.Object
            })
            {
                Assert.That(() => server.InitializeResourceIsolation(Configuration(), telemetry),
                    Throws.ArgumentException
                        .With.Property(nameof(ArgumentException.ParamName))
                        .EqualTo(nameof(ServerResourceIsolationOptions.HandshakeTimeout))
                        .And.Message.Contains("positive")
                        .And.Message.Contains("two minutes"));
                Assert.That(server.ResourceIsolationProvider,
                    Is.SameAs(customProvider ? isolation.Object : null));
                Assert.That(server.ChunkReassemblyBudget, Is.SameAs(budget));
                Assert.That(server.RateLimiterProvider, Is.SameAs(rate.Object));
                Assert.That(options.HandshakeTimeout, Is.EqualTo(TimeSpan.FromTicks(ticks)));
            }

            disposable.Verify(d => d.Dispose(), Times.Never);
            isolation.VerifyNoOtherCalls();
            rate.VerifyNoOtherCalls();
        }

        [Test]
        public async Task StartupAcceptsExactTimeoutBoundsWithoutValidatingBypassedReservationsAsync(
            [Values] ServerResourceIsolationMode mode,
            [Values(1L, 1_200_000_000L)] long ticks,
            [Values] bool customProvider)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var isolation = new Mock<IServerResourceIsolationProvider>(MockBehavior.Strict);
            Mock<IDisposable> disposable = isolation.As<IDisposable>();
            bool bypassPlan = customProvider || mode == ServerResourceIsolationMode.SharedOnly;
            ServerResourceIsolationOptions options = Options(
                mode == ServerResourceIsolationMode.TrustedReservations ? 1 : 0);
            options.Mode = mode;
            options.HandshakeTimeout = TimeSpan.FromTicks(ticks);
            ApplicationConfiguration configuration = Configuration();
            ServerConfiguration serverConfiguration = configuration.ServerConfiguration!;
            TransportQuotas quotas = configuration.TransportQuotas!;
            if (bypassPlan)
            {
                serverConfiguration.MaxSessionCount = 0;
                serverConfiguration.MaxChannelCount = 0;
                quotas.MaxMessageSize = 0;
                quotas.MaxBufferSize = 0;
                options.MaxRetainedMessageBytes = 0;
                options.MaxTrackedOwners = 0;
            }

            await using (var server = new ListenerSettingsServer(telemetry)
            {
                ResourceIsolationOptions = options,
                ResourceIsolationProvider = customProvider ? isolation.Object : null,
                ResourceIsolationClassifier = Mock.Of<IResourceIsolationClassifier>()
            })
            {
                server.InitializeResourceIsolation(configuration, telemetry);
                TransportListenerSettings settings = server.CreateListenerSettings();
                Assert.That(settings.HandshakeTimeout, Is.EqualTo(TimeSpan.FromTicks(ticks)));
                if (bypassPlan)
                {
                    Assert.That(server.ResourceIsolationProvider,
                        Is.SameAs(customProvider ? isolation.Object : null));
                    Assert.That(server.ChunkReassemblyBudget, Is.Null);
                    Assert.That(serverConfiguration.MaxSessionCount, Is.Zero);
                    Assert.That(serverConfiguration.MaxChannelCount, Is.Zero);
                    Assert.That(quotas.MaxMessageSize, Is.Zero);
                    Assert.That(quotas.MaxBufferSize, Is.Zero);
                    Assert.That(options.MaxRetainedMessageBytes, Is.Zero);
                    Assert.That(options.MaxTrackedOwners, Is.Zero);
                }
                else
                {
                    Assert.That(server.ResourceIsolationProvider,
                        Is.TypeOf<DefaultServerResourceIsolationProvider>());
                    var installed = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
                    Assert.That(installed.Plan.Mode, Is.EqualTo(mode));
                    Assert.That(server.ChunkReassemblyBudget, Is.Not.Null);
                }
            }

            disposable.Verify(d => d.Dispose(), Times.Never);
            isolation.VerifyNoOtherCalls();
        }

        [Test]
        public async Task InvalidTimeoutPreservesPreviouslyOwnedIsolationAndRateLimiterAsync(
            [Values] ServerResourceIsolationMode nextMode,
            [Values(-1L, 0L, 1_200_000_001L)] long ticks)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new StandardServer(telemetry, new FakeTimeProvider())
            {
                ResourceIsolationOptions = Options(),
                RateLimitOptions = new ServerRateLimitOptions { ConnectionsPerSecond = 3, ConnectionBurst = 3 }
            };
            server.InitializeResourceIsolation(Configuration(), telemetry);
            server.InitializeRateLimiting();
            var previous = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            ChunkReassemblyBudget? budget = server.ChunkReassemblyBudget;
            IServerRateLimiterProvider? rate = server.RateLimiterProvider;
            server.ResourceIsolationOptions.Mode = nextMode;
            server.ResourceIsolationOptions.HandshakeTimeout = TimeSpan.FromTicks(ticks);

            Assert.That(() => server.InitializeResourceIsolation(Configuration(), telemetry),
                Throws.ArgumentException
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo(nameof(ServerResourceIsolationOptions.HandshakeTimeout)));
            Assert.That(server.ResourceIsolationProvider, Is.SameAs(previous));
            Assert.That(server.ChunkReassemblyBudget, Is.SameAs(budget));
            Assert.That(server.RateLimiterProvider, Is.SameAs(rate));
            AssertProviderAdmitsConnection(previous);
            Assert.That(rate!.ConnectionRateLimiter!.TryAdmitConnection(null, out _), Is.True);
        }

        [Test]
        public async Task ListenerSettingsRevalidateTimeoutChangedAfterInitializationAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new ListenerSettingsServer(telemetry)
            {
                ResourceIsolationOptions = new ServerResourceIsolationOptions
                {
                    Mode = ServerResourceIsolationMode.SharedOnly
                }
            };
            server.InitializeResourceIsolation(new ApplicationConfiguration(), telemetry);
            server.ResourceIsolationOptions.HandshakeTimeout = TimeSpan.Zero;

            Assert.That(server.CreateListenerSettings,
                Throws.ArgumentException
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo(nameof(ServerResourceIsolationOptions.HandshakeTimeout)));
            Assert.That(server.ResourceIsolationProvider, Is.Null);
        }

        [Test]
        public async Task InvalidFinitePlanPreservesPreviouslyOwnedProviderAndBudgetAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new StandardServer(telemetry, new FakeTimeProvider())
            {
                ResourceIsolationOptions = Options()
            };
            server.InitializeResourceIsolation(Configuration(), telemetry);
            var previous = (DefaultServerResourceIsolationProvider)server.ResourceIsolationProvider!;
            ChunkReassemblyBudget? budget = server.ChunkReassemblyBudget;
            server.ResourceIsolationOptions.MaxTrackedOwners = 0;

            Assert.That(() => server.InitializeResourceIsolation(Configuration(), telemetry),
                Throws.ArgumentException.With.Message.Contains("Owner table"));
            Assert.That(server.ResourceIsolationProvider, Is.SameAs(previous));
            Assert.That(server.ChunkReassemblyBudget, Is.SameAs(budget));
            AssertProviderAdmitsConnection(previous);
        }

        [Test]
        public void ProtectedRateLimitsIdentifyOffendingTotal(
            [Values(0, 1, 2)] int trustedOwnerCount,
            [Values] bool invalidBurst,
            [Values] bool defaultProvider)
        {
            using DefaultServerResourceIsolationProvider isolation = CreateProvider(trustedOwnerCount);
            int minimum = 3 + trustedOwnerCount;
            var options = new ServerRateLimitOptions
            {
                ConnectionsPerSecond = invalidBurst ? minimum : minimum - 1,
                ConnectionBurst = invalidBurst ? minimum - 1 : minimum
            };
            string parameterName = invalidBurst ? "burst" : "connectionsPerSecond";
            string optionName = invalidBurst
                ? nameof(ServerRateLimitOptions.ConnectionBurst)
                : nameof(ServerRateLimitOptions.ConnectionsPerSecond);

            Assert.That(() =>
            {
                using IDisposable limiter = defaultProvider
                    ? new DefaultServerRateLimiterProvider(options, isolation, new FakeTimeProvider())
                    : new ResourceIsolationConnectionRateLimiter(
                        options.ConnectionsPerSecond, options.ConnectionBurst, isolation, new FakeTimeProvider());
            }, Throws.ArgumentException
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo(parameterName)
                .And.Message.Contains(optionName)
                .And.Message.Contains($"at least {minimum}")
                .And.Message.Contains("at least one shared token"));
            Assert.That(options.ConnectionsPerSecond, Is.EqualTo(invalidBurst ? minimum : minimum - 1));
            Assert.That(options.ConnectionBurst, Is.EqualTo(invalidBurst ? minimum - 1 : minimum));
            AssertProviderAdmitsConnection(isolation);
        }

        [Test]
        public void MinimumProtectedRateTotalsRetainExactlyOneSharedToken(
            [Values(0, 1, 2)] int trustedOwnerCount)
        {
            using DefaultServerResourceIsolationProvider isolation = CreateProvider(trustedOwnerCount);
            int minimum = 3 + trustedOwnerCount;
            var options = new ServerRateLimitOptions { ConnectionsPerSecond = minimum, ConnectionBurst = minimum };
            var clock = new FakeTimeProvider();
            using var provider = new DefaultServerRateLimiterProvider(options, isolation, clock);

            Assert.That(provider.ConnectionRateLimiter, Is.TypeOf<ResourceIsolationConnectionRateLimiter>());
            Assert.That(provider.ConnectionRateLimiter!.TryAdmitConnection(null, out _), Is.True);
            Assert.That(provider.ConnectionRateLimiter.TryAdmitConnection(null, out _), Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(provider.ConnectionRateLimiter.TryAdmitConnection(null, out _), Is.True);
            Assert.That(provider.ConnectionRateLimiter.TryAdmitConnection(null, out _), Is.False);
            Assert.That(options.ConnectionsPerSecond, Is.EqualTo(minimum));
            Assert.That(options.ConnectionBurst, Is.EqualTo(minimum));
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public async Task DisabledRateLimitsSkipProtectedMinimumValidationAsync(
            bool enabled,
            bool connectionEnabled)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new StandardServer(telemetry, new FakeTimeProvider())
            {
                ResourceIsolationOptions = Options(2),
                ResourceIsolationClassifier = Mock.Of<IResourceIsolationClassifier>(),
                RateLimitOptions = new ServerRateLimitOptions
                {
                    Enabled = enabled,
                    ConnectionRateLimitEnabled = connectionEnabled,
                    ConnectionsPerSecond = 1,
                    ConnectionBurst = 1
                }
            };
            server.InitializeResourceIsolation(Configuration(), telemetry);
            server.InitializeRateLimiting();

            Assert.That(server.ResourceIsolationProvider, Is.TypeOf<DefaultServerResourceIsolationProvider>());
            Assert.That(server.RateLimiterProvider, Is.TypeOf<DefaultServerRateLimiterProvider>());
            Assert.That(server.RateLimiterProvider!.ConnectionRateLimiter, Is.Null);
            Assert.That(server.RateLimitOptions.ConnectionsPerSecond, Is.EqualTo(1));
            Assert.That(server.RateLimitOptions.ConnectionBurst, Is.EqualTo(1));
        }

        [Test]
        public async Task CustomRateProviderPrecedesProtectedMinimumValidationAsync(
            [Values(0, 1, 2)] int trustedOwnerCount)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var custom = new Mock<IServerRateLimiterProvider>(MockBehavior.Strict);
            await using (var server = new StandardServer(telemetry, new FakeTimeProvider())
            {
                ResourceIsolationOptions = Options(trustedOwnerCount),
                ResourceIsolationClassifier = Mock.Of<IResourceIsolationClassifier>(),
                RateLimiterProvider = custom.Object,
                RateLimitOptions = new ServerRateLimitOptions { ConnectionsPerSecond = 1, ConnectionBurst = 1 }
            })
            {
                server.InitializeResourceIsolation(Configuration(), telemetry);
                server.InitializeRateLimiting();

                Assert.That(server.ResourceIsolationProvider, Is.TypeOf<DefaultServerResourceIsolationProvider>());
                Assert.That(server.RateLimiterProvider, Is.SameAs(custom.Object));
                Assert.That(server.RateLimitOptions.ConnectionsPerSecond, Is.EqualTo(1));
                Assert.That(server.RateLimitOptions.ConnectionBurst, Is.EqualTo(1));
            }

            custom.Verify(p => p.Dispose(), Times.Never);
            custom.VerifyNoOtherCalls();
        }

        [TestCase(2, 3, "connectionsPerSecond")]
        [TestCase(3, 2, "burst")]
        public async Task InvalidRateTotalDoesNotReplaceOrDisposeExistingLimiterAsync(
            int rate,
            int burst,
            string parameterName)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var server = new StandardServer(telemetry, new FakeTimeProvider())
            {
                ResourceIsolationOptions = Options(),
                RateLimitOptions = new ServerRateLimitOptions { ConnectionsPerSecond = 3, ConnectionBurst = 3 }
            };
            server.InitializeResourceIsolation(Configuration(), telemetry);
            server.InitializeRateLimiting();
            IServerRateLimiterProvider? previous = server.RateLimiterProvider;
            server.RateLimitOptions.ConnectionsPerSecond = rate;
            server.RateLimitOptions.ConnectionBurst = burst;

            Assert.That(() => server.InitializeRateLimiting(), Throws.ArgumentException
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo(parameterName));
            Assert.That(server.RateLimiterProvider, Is.SameAs(previous));
            Assert.That(previous!.ConnectionRateLimiter!.TryAdmitConnection(null, out _), Is.True);
            Assert.That(previous.ConnectionRateLimiter.TryAdmitConnection(null, out _), Is.False);
        }

        /// <summary>
        /// Proves failed validation left the provider usable rather than merely retaining its reference.
        /// </summary>
        private static void AssertProviderAdmitsConnection(DefaultServerResourceIsolationProvider provider)
        {
            ResourceIsolationOwner owner = provider.ClassifyConnection(null);
            Assert.That(provider.TryAcquire(ResourceIsolationStage.Connection, owner, 1,
                out IDisposable? lease, out _), Is.True);
            Assert.That(lease, Is.Not.Null);
            lease!.Dispose();
            Assert.That(provider.GetUsage(ResourceIsolationStage.Connection), Is.Zero);
        }

        /// <summary>
        /// Creates finite capacity sufficient for the requested number of provisioned rate buckets.
        /// </summary>
        private static DefaultServerResourceIsolationProvider CreateProvider(int trustedOwnerCount)
        {
            return new DefaultServerResourceIsolationProvider(
                Options(trustedOwnerCount).CreateRuntimePlan(Configuration(), new ServerRateLimitOptions()),
                NUnitTelemetryContext.Create(), classifier: Mock.Of<IResourceIsolationClassifier>());
        }

        /// <summary>
        /// Keeps memory footprints small while preserving explicit protected reservations.
        /// </summary>
        private static ServerResourceIsolationOptions Options(int trustedOwnerCount = 0)
        {
            var owners = new TrustedResourceOwnerOptions[trustedOwnerCount];
            for (int ii = 0; ii < owners.Length; ii++)
            {
                owners[ii] = new TrustedResourceOwnerOptions { Key = "tenant-" + ii };
            }
            return new ServerResourceIsolationOptions
            {
                Mode = trustedOwnerCount == 0
                    ? ServerResourceIsolationMode.Balanced
                    : ServerResourceIsolationMode.TrustedReservations,
                MaxRetainedMessageBytes = 10,
                TrustedOwners = owners
            };
        }

        /// <summary>
        /// Supplies finite totals with room for all protected classes and ordinary reconnect capacity.
        /// </summary>
        private static ApplicationConfiguration Configuration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 2, MaxChannelCount = 100 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 10, MaxBufferSize = 8192 }
            };
        }

        /// <summary>
        /// Exposes listener configuration without certificates, network sockets or server worker threads.
        /// </summary>
        private sealed class ListenerSettingsServer(ITelemetryContext telemetry)
            : StandardServer(telemetry, new FakeTimeProvider())
        {
            /// <summary>
            /// Runs the production listener-settings hook after startup option validation.
            /// </summary>
            public TransportListenerSettings CreateListenerSettings()
            {
                var settings = new TransportListenerSettings();
                ConfigureTransportListenerSettings(settings, new Uri("opc.tcp://localhost:4840"));
                return settings;
            }
        }
    }
}
