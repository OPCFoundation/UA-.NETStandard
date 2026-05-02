// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using System;
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
        }

        [Test]
        public void WithMethodsCaptureValuesIntoSnapshot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName("Custom")
                .WithSessionTimeout(TimeSpan.FromSeconds(30))
                .WithPreferredLocales("en-US", "de-DE")
                .WithCheckDomain()
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
            Assert.That(opts.PreferredLocales, Is.EquivalentTo(new[] { "en-US", "de-DE" }));
            Assert.That(opts.CheckDomain, Is.True);
            Assert.That(opts.ReconnectPolicy.Strategy, Is.EqualTo(BackoffStrategy.Linear));
            Assert.That(opts.ReconnectPolicy.MaxRetries, Is.EqualTo(5));
            Assert.That(opts.EnableServerRedundancy, Is.True);
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

            Assert.That(() => builder.UseEndpoint((ConfiguredEndpoint)null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.UseEndpoint((string)null!),
                Throws.ArgumentNullException);
            Assert.That(() => builder.WithSessionName(""),
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
            Assert.That(policy.JitterFactor, Is.EqualTo(0));
        }
    }
}
