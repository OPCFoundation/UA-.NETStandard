// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ManagedSessionBuilderTests
    {
        private static ApplicationConfiguration CreateConfig(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "test",
                ClientConfiguration = new ClientConfiguration()
            };
        }

        [Test]
        public void BuildReturnsDefaultsWhenNotConfigured()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            ManagedSessionOptions opts = builder.Build();

            Assert.That(opts, Is.Not.Null);
            Assert.That(opts.SessionName, Is.EqualTo("ManagedSession"));
            Assert.That(opts.SessionTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(opts.CheckDomain, Is.False);
            Assert.That(opts.EnableServerRedundancy, Is.False);
            Assert.That(opts.ReconnectPolicy.Strategy, Is.EqualTo(BackoffStrategy.Exponential));
            Assert.That(opts.Endpoint, Is.Null);
            Assert.That(opts.SubscriptionEngineFactory, Is.Null);
            Assert.That(opts.ReconnectOnCommunicationFailure, Is.True);
            Assert.That(opts.KeepAliveFailureThreshold, Is.EqualTo(1u));
            Assert.That(opts.UseSharedChannelManager, Is.True);
        }

        [Test]
        public void WithMethodsCaptureValuesIntoSnapshot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            }, configuration: null);
            ManagedSessionBuilder builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName("Custom")
                .WithSessionTimeout(TimeSpan.FromSeconds(30))
                .WithPreferredLocales("en-US", "de-DE")
                .WithCheckDomain()
                .WithCommunicationRecovery(false, 3)
                .WithoutChannelManager()
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Linear,
                    MaxRetries = 5
                })
                .WithServerRedundancy();

            ManagedSessionOptions opts = builder.Build();

            Assert.That(opts.Endpoint, Is.SameAs(endpoint));
            Assert.That(opts.SessionName, Is.EqualTo("Custom"));
            Assert.That(opts.SessionTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(opts.PreferredLocales, Is.EquivalentTo(["en-US", "de-DE"]));
            Assert.That(opts.CheckDomain, Is.True);
            Assert.That(opts.ReconnectPolicy.Strategy, Is.EqualTo(BackoffStrategy.Linear));
            Assert.That(opts.ReconnectPolicy.MaxRetries, Is.EqualTo(5));
            Assert.That(opts.EnableServerRedundancy, Is.True);
            Assert.That(opts.ReconnectOnCommunicationFailure, Is.False);
            Assert.That(opts.KeepAliveFailureThreshold, Is.EqualTo(3u));
            Assert.That(opts.UseSharedChannelManager, Is.False);
        }

        [Test]
        public void ChannelManagerSelectionIsExplicitAndLastSelectionWins()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);
            var manager = Mock.Of<IClientChannelManager>();

            builder.WithChannelManager(manager).WithoutChannelManager();
            Assert.That(builder.Build().UseSharedChannelManager, Is.False);
            builder.WithChannelManager(manager);
            Assert.That(builder.Build().UseSharedChannelManager, Is.True);
        }

        [Test]
        public void CommunicationRecoveryRejectsZeroThreshold()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(() => builder.WithCommunicationRecovery(true, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task SessionOwnedTransportRejectsBuilderManagedHttpsResilienceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(new ConfiguredEndpoint(null, new EndpointDescription("opc.https://localhost:4840"), configuration: null))
                .WithHttpsResilience(_ => { })
                .WithoutChannelManager();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await builder.ConnectAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public void ConnectAsyncWithoutEndpointThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(() => builder.ConnectAsync().GetAwaiter().GetResult(),
                Throws.InvalidOperationException);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Assert.That(() => new ManagedSessionBuilder(null!, telemetry),
                Throws.ArgumentNullException);
            Assert.That(() => new ManagedSessionBuilder(CreateConfig(telemetry), null!),
                Throws.ArgumentNullException);

            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(() => builder.UseEndpoint((ConfiguredEndpoint)null!),
                Throws.ArgumentNullException);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(() => builder.UseEndpoint((string)null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.WithSessionName(string.Empty),
                Throws.ArgumentException);
            Assert.That(() => builder.WithSessionTimeout(TimeSpan.Zero),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ReconnectPolicyOptionsBackedReconnectPolicy()
        {
            var options = new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Linear,
                MaxRetries = 3,
                JitterFactor = 0
            };
            var policy = new ReconnectPolicy(options);

            Assert.That(policy.Strategy, Is.EqualTo(BackoffStrategy.Linear));
            Assert.That(policy.MaxRetries, Is.EqualTo(3));
            Assert.That(policy.JitterFactor, Is.Zero);
        }
    }
}
