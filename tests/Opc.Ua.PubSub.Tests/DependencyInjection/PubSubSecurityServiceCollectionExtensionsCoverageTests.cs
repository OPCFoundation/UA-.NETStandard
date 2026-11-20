// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests.Security.Sks;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Coverage-gap tests for
    /// <see cref="PubSubSecurityServiceCollectionExtensions"/> —
    /// exercises overloads and null-guard branches not reached by
    /// <see cref="PubSubSecurityServiceCollectionExtensionsTests"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class PubSubSecurityServiceCollectionExtensionsCoverageTests
    {
        private const string GroupId = "cov-group-1";
        private static string PolicyUri => PubSubSecurityPolicyUri.PubSubAes128Ctr;

        private static FakeSecurityKeyService CreateFake()
        {
            return new FakeSecurityKeyService(
                PubSubSecurityPolicyRegistry.Default.GetByUri(PolicyUri)!,
                TimeSpan.FromHours(1));
        }

        // ---- AddPubSubSecurityKeyServiceClient — EndpointDescription overload ----

        [Test]
        public void AddPubSubSecurityKeyServiceClientWithEndpointRegistersProviderDescriptor()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            var endpoint = new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" };

            builder.AddPubSubSecurityKeyServiceClient(GroupId, PolicyUri, endpoint);

            Assert.That(
                services.Any(d => d.ServiceType == typeof(IPubSubSecurityKeyProvider)),
                Is.True,
                "IPubSubSecurityKeyProvider descriptor must be registered.");
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClientWithEndpointAndConfigureRegistersProviderDescriptor()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            var endpoint = new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" };

            builder.AddPubSubSecurityKeyServiceClient(
                GroupId,
                PolicyUri,
                endpoint,
                opts => opts.RequestedFutureKeyCount = 2);

            Assert.That(
                services.Any(d => d.ServiceType == typeof(IPubSubSecurityKeyProvider)),
                Is.True);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClientWithNullEndpointThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            EndpointDescription? endpoint = null;

            Assert.That(
                () => builder.AddPubSubSecurityKeyServiceClient(GroupId, PolicyUri, endpoint!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("endpoint"));
        }

        // ---- AddSecurityKeyServiceClient (IPubSubBuilder) — factory overload null guard ----

        [Test]
        public void AddSecurityKeyServiceClientOnNullPubSubBuilderThrows()
        {
            IPubSubBuilder? builder = null;

            Assert.That(
                () => builder!.AddSecurityKeyServiceClient(GroupId, PolicyUri, _ => CreateFake()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        // ---- AddSecurityKeyServiceClient (IPubSubBuilder) — EndpointDescription overload ----

        [Test]
        public void AddSecurityKeyServiceClientWithEndpointOnPubSubBuilderRegistersDescriptor()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            var endpoint = new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" };

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddSecurityKeyServiceClient(GroupId, PolicyUri, endpoint));

            Assert.That(
                services.Any(d => d.ServiceType == typeof(IPubSubSecurityKeyProvider)),
                Is.True,
                "IPubSubSecurityKeyProvider descriptor must be registered.");
        }

        [Test]
        public void AddSecurityKeyServiceClientWithEndpointOnNullPubSubBuilderThrows()
        {
            IPubSubBuilder? builder = null;
            var endpoint = new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" };

            Assert.That(
                () => builder!.AddSecurityKeyServiceClient(GroupId, PolicyUri, endpoint),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        // ---- AddPubSubSecurityKeyPushTarget (IOpcUaBuilder) ----

        [Test]
        public async Task AddPubSubSecurityKeyPushTargetOnOpcUaBuilderRegistersProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddPubSubSecurityKeyPushTarget(GroupId);

            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetServices<IPubSubSecurityKeyProvider>().Single(),
                Is.TypeOf<PushSecurityKeyProvider>());
        }

        [Test]
        public async Task AddPubSubSecurityKeyPushTargetReturnsSameBuilderAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddPubSubSecurityKeyPushTarget(GroupId);

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(builder));
        }

        [Test]
        public void AddPubSubSecurityKeyPushTargetNullBuilderThrows()
        {
            IOpcUaBuilder? builder = null;

            Assert.That(
                () => builder!.AddPubSubSecurityKeyPushTarget(GroupId),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddPubSubSecurityKeyPushTargetEmptyGroupIdThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddPubSubSecurityKeyPushTarget(string.Empty),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("securityGroupId"));
        }

        // ---- AddSecurityKeyPushTarget (IPubSubBuilder) ----

        [Test]
        public void AddSecurityKeyPushTargetOnNullPubSubBuilderThrows()
        {
            IPubSubBuilder? builder = null;

            Assert.That(
                () => builder!.AddSecurityKeyPushTarget(GroupId),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        // ---- AddSecurityKeyServiceServer (IPubSubBuilder) ----

        [Test]
        public void AddSecurityKeyServiceServerOnNullPubSubBuilderThrows()
        {
            IPubSubBuilder? builder = null;

            Assert.That(
                () => builder!.AddSecurityKeyServiceServer(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public async Task AddSecurityKeyServiceServerOnPubSubBuilderReturnsSameBuilderAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IPubSubBuilder? captured = null;
            IPubSubBuilder? returned = null;

            services.AddOpcUa().AddPubSub(pubsub =>
            {
                captured = pubsub;
                returned = pubsub.AddSecurityKeyServiceServer();
            });

            await using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(returned, Is.SameAs(captured));
        }

        // ---- AddPubSubSecurityKeyServiceServer — configure callback invoked ----

        [Test]
        public async Task AddPubSubSecurityKeyServiceServerWithConfigureCallbackIsInvokedAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            bool callbackInvoked = false;

            builder.AddPubSubSecurityKeyServiceServer(server =>
            {
                callbackInvoked = true;
                _ = server;
            });

            await using ServiceProvider sp = services.BuildServiceProvider();
            _ = sp.GetRequiredService<InMemoryPubSubKeyServiceServer>();

            Assert.That(callbackInvoked, Is.True);
        }

        // ---- RegisterPullSecurityKeyProvider guard branches ----

        [Test]
        public void AddPubSubSecurityKeyServiceClientEmptyGroupIdThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddPubSubSecurityKeyServiceClient(
                    string.Empty,
                    PolicyUri,
                    _ => CreateFake()),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("securityGroupId"));
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClientEmptyPolicyUriThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddPubSubSecurityKeyServiceClient(
                    GroupId,
                    string.Empty,
                    _ => CreateFake()),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("securityPolicyUri"));
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClientNullFactoryThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Func<IServiceProvider, ISecurityKeyService>? factory = null;

            Assert.That(
                () => builder.AddPubSubSecurityKeyServiceClient(
                    GroupId,
                    PolicyUri,
                    factory!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("securityKeyServiceFactory"));
        }

        // ---- AddPubSubSecurityKeyServiceClient — with configure callback (configure != null path) ----

        [Test]
        public async Task AddPubSubSecurityKeyServiceClientWithConfigureRegistersProviderAndStarterAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddPubSubSecurityKeyServiceClient(
                GroupId,
                PolicyUri,
                _ => CreateFake(),
                opts => opts.RequestedFutureKeyCount = 3);

            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(
                    sp.GetServices<IPubSubSecurityKeyProvider>().Single(),
                    Is.TypeOf<PullSecurityKeyProvider>());
                Assert.That(
                    sp.GetServices<IHostedService>().OfType<PubSubSecurityKeyProviderStarter>().Count(),
                    Is.EqualTo(1));
            });
        }

        // ---- AddPubSubSecurityKeyServiceClient — starts provider (configure=null path) ----

        [Test]
        public async Task AddPubSubSecurityKeyServiceClientWithoutConfigureStartsProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceClient(GroupId, PolicyUri, _ => CreateFake());
            await using ServiceProvider sp = services.BuildServiceProvider();

            PubSubSecurityKeyProviderStarter starter = sp
                .GetServices<IHostedService>()
                .OfType<PubSubSecurityKeyProviderStarter>()
                .Single();
            await starter.StartAsync(CancellationToken.None).ConfigureAwait(false);

            var provider = (PullSecurityKeyProvider)sp.GetServices<IPubSubSecurityKeyProvider>().Single();
            PubSubSecurityKey key = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(key.TokenId, Is.GreaterThan(0U));
        }
    }
}
