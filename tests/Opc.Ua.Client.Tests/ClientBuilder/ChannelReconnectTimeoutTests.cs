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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Covers recovery-timeout resolution, construction paths, and per-cycle session budgets.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class ChannelReconnectTimeoutTests
    {
        /// <summary>
        /// Automatic recovery uses the largest usable setting and clamps arithmetic overflow.
        /// </summary>
        [TestCase(1000, 60000, 10000, 60000)]
        [TestCase(30000, 60000, 10000, 90000)]
        [TestCase(1000, 60000, 90000, 90000)]
        [TestCase(1000, double.NaN, 2000, 3000)]
        [TestCase(1000, double.PositiveInfinity, 2000, 3000)]
        [TestCase(0, -1, -1, 3)]
        [TestCase(int.MaxValue, 60000, int.MaxValue, 4294967294d)]
        [TestCase(1000, double.MaxValue, 10000, 4294967294d)]
        public void AutomaticTimeoutUsesLargestFiniteSettingWithoutOverflow(
            int keepAliveInterval,
            double sessionTimeout,
            int operationTimeout,
            double expectedMilliseconds)
        {
            TimeSpan timeout = ManagedSessionOptions.ResolveChannelReconnectTimeout(
                null, keepAliveInterval, sessionTimeout, operationTimeout);

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds)));
        }

        /// <summary>
        /// Finite and infinite overrides take precedence and remain configurable through the builder.
        /// </summary>
        [TestCase(1L)]
        [TestCase(10000L)]
        [TestCase(42949672940000L)]
        [TestCase(-10000L)]
        public void ExplicitTimeoutOverridesAutomaticCalculation(long ticks)
        {
            var timeout = TimeSpan.FromTicks(ticks);
            Assert.That(ManagedSessionOptions.ResolveChannelReconnectTimeout(
                timeout, int.MaxValue, double.MaxValue, int.MaxValue), Is.EqualTo(timeout));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfiguration(telemetry), telemetry);

            Assert.That(builder.WithChannelReconnectTimeout(timeout), Is.SameAs(builder));
            Assert.That(builder.Build().ChannelReconnectTimeout, Is.EqualTo(timeout));
            Assert.That(builder.WithChannelReconnectTimeout(null).Build().ChannelReconnectTimeout, Is.Null);
        }

        /// <summary>
        /// Invalid timeout values fail consistently before fluent, direct, or DI construction connects.
        /// </summary>
        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(-10001L)]
        [TestCase(42949672940001L)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public async Task InvalidTimeoutIsRejectedByFluentDirectAndDiConstructionAsync(long ticks)
        {
            var timeout = TimeSpan.FromTicks(ticks);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateConfiguration(telemetry);
            var options = new ManagedSessionOptions { ChannelReconnectTimeout = timeout };
            var builder = new ManagedSessionBuilder(configuration, telemetry);

            Assert.That(() => builder.WithChannelReconnectTimeout(timeout),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            await Assert.ThatAsync(
                () => Client.ManagedSession.CreateAsync(options, configuration, Mock.Of<ISessionFactory>()),
                Throws.TypeOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(client =>
            {
                client.Configuration = configuration;
                client.Session = options;
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                () => provider.GetRequiredService<IOptions<OpcUaClientOptions>>().Value,
                Throws.TypeOf<OptionsValidationException>()
                    .With.Property(nameof(OptionsValidationException.Failures))
                    .Some.Contains("ChannelReconnectTimeout"));
        }

        /// <summary>
        /// DI passes automatic, infinite, and finite timeout settings to the connection builder.
        /// </summary>
        [TestCase(null)]
        [TestCase(-1)]
        [TestCase(1000)]
        public async Task DiConstructionAppliesAutomaticInfiniteAndFiniteTimeoutsAsync(int? milliseconds)
        {
            TimeSpan? timeout = milliseconds.HasValue
                ? TimeSpan.FromMilliseconds(milliseconds.Value)
                : null;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(client =>
            {
                client.Configuration = CreateConfiguration(telemetry);
                client.Session = new ManagedSessionOptions
                {
                    ChannelReconnectTimeout = timeout,
                    Endpoint = new ConfiguredEndpoint(null, new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    })
                };
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            ManagedSessionOptions options = provider.GetRequiredService<IOptions<OpcUaClientOptions>>().Value.Session;
            TimeSpan? configuredTimeout = null;
            bool configured = false;

            await Assert.ThatAsync(
                () => provider.ConnectManagedSessionAsync(options, builder =>
                {
                    configuredTimeout = builder.Build().ChannelReconnectTimeout;
                    configured = true;
                    throw new InvalidOperationException("Stop before connecting.");
                }, CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Stop before connecting."))
                .ConfigureAwait(false);
            Assert.That(configured, Is.True);
            Assert.That(configuredTimeout, Is.EqualTo(timeout));
        }

        /// <summary>
        /// Raw sessions impose no budget, and configured sessions sample changed settings only for a new cycle.
        /// </summary>
        [Test]
        public async Task SessionBudgetsPreserveRawDefaultsAndResampleOnlyOnTheNextCycleAsync()
        {
            var time = new FakeTimeProvider();
            await using var session = SessionMock.Create();
            session.Channel.SetupGet(channel => channel.OperationTimeout).Returns(1000);
            session.KeepAliveInterval = 500;
            var participant = (IReconnectParticipant)session;
            Assert.That(participant.CreateReconnectBudget(time), Is.Null);

            session.ConfigureChannelReconnectTimeout(null, requestedSessionTimeout: 5000);
            IRetryBudget first = participant.CreateReconnectBudget(time)!;
            Assert.That(first.TryConsume(out TimeSpan initial), Is.True);
            Assert.That(initial, Is.EqualTo(TimeSpan.FromSeconds(5)));

            time.Advance(TimeSpan.FromSeconds(1));
            session.KeepAliveInterval = 10000;
            Assert.That(first.TryConsume(out TimeSpan remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(4)));
            IRetryBudget next = participant.CreateReconnectBudget(time)!;
            Assert.That(next.TryConsume(out TimeSpan resampled), Is.True);
            Assert.That(resampled, Is.EqualTo(TimeSpan.FromSeconds(30)));

            session.ConfigureChannelReconnectTimeout(Timeout.InfiniteTimeSpan, requestedSessionTimeout: 5000);
            IRetryBudget unlimited = participant.CreateReconnectBudget(time)!;
            Assert.That(unlimited.TryConsume(out TimeSpan unbounded), Is.True);
            Assert.That(unbounded, Is.EqualTo(TimeSpan.MaxValue));
            time.Advance(TimeSpan.FromDays(100));
            Assert.That(unlimited.IsExhausted, Is.False);
            Assert.That(first.IsExhausted, Is.True);
            Assert.That(next.IsExhausted, Is.True);
        }

        private static ApplicationConfiguration CreateConfiguration(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:channel-reconnect-timeout",
                ApplicationName = "channel-reconnect-timeout",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
