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
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http.Resilience;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Extends coverage of <see cref="ManagedSessionBuilder"/>:
    /// exercises all <c>With*</c>, <c>Use*</c>, and <c>Configure*</c>
    /// methods not yet hit by <see cref="ManagedSessionBuilderTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ManagedSessionBuilderExtendedTests
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

        // ----------------------------------------------------------------
        // UseWebApiEndpoint
        // ----------------------------------------------------------------

        [Test]
        public void UseWebApiEndpoint_SetsHttpsOpenApiTransportProfile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseWebApiEndpoint("https://server:4843/")
                .Build();

            Assert.That(opts.Endpoint, Is.Not.Null);
            Assert.That(opts.Endpoint!.Description.TransportProfileUri,
                Is.EqualTo(Profiles.HttpsOpenApiTransport));
            Assert.That(opts.Endpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public void UseWebApiEndpoint_NullUrl_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(() => builder.UseWebApiEndpoint(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void UseWebApiEndpoint_VerboseEncoding_IsPreserved()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionBuilder builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseWebApiEndpoint("https://server:4843/", WebApiEncoding.Verbose);

            // Build() only returns ManagedSessionOptions; encoding lives in the
            // builder's private WebApiClientOptions. Smoke-test that no exception
            // is thrown and the endpoint URL is set correctly.
            ManagedSessionOptions opts = builder.Build();

            Assert.That(opts.Endpoint, Is.Not.Null);
            Assert.That(opts.Endpoint!.Description.EndpointUrl,
                Does.StartWith("https://server:4843/"));
        }

        // ----------------------------------------------------------------
        // UseWssOpenApiEndpoint
        // ----------------------------------------------------------------

        [Test]
        public void UseWssOpenApiEndpoint_SetsWssOpenApiTransportProfile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseWssOpenApiEndpoint("wss://server:4843/")
                .Build();

            Assert.That(opts.Endpoint, Is.Not.Null);
            Assert.That(opts.Endpoint!.Description.TransportProfileUri,
                Is.EqualTo(Profiles.WssOpenApiTransport));
            Assert.That(opts.Endpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public void UseWssOpenApiEndpoint_NullUrl_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(() => builder.UseWssOpenApiEndpoint(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void UseWssOpenApiEndpoint_VerboseEncoding_NoException()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseWssOpenApiEndpoint("wss://server:4843/", WebApiEncoding.Verbose)
                .Build();

            Assert.That(opts.Endpoint, Is.Not.Null);
        }

        // ----------------------------------------------------------------
        // WithWebApiAuthentication
        // ----------------------------------------------------------------

        [Test]
        public void WithWebApiAuthentication_NullConfigure_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithWebApiAuthentication(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithWebApiAuthentication_ActionIsCalled()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            bool invoked = false;
            new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithWebApiAuthentication(opt =>
                {
                    invoked = true;
                    opt.Encoding = WebApiEncoding.Verbose;
                });

            Assert.That(invoked, Is.True);
        }

        // ----------------------------------------------------------------
        // WithUserIdentity
        // ----------------------------------------------------------------

        [Test]
        public void WithUserIdentity_NullIdentity_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithUserIdentity(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithUserIdentity_SetsIdentityOnOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IUserIdentity identity = new UserIdentity();

#pragma warning disable CS0618 // Testing the legacy identity path is intentional
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithUserIdentity(identity)
                .Build();

            Assert.That(opts.Identity, Is.SameAs(identity));
#pragma warning restore CS0618
        }

        // ----------------------------------------------------------------
        // WithIdentityProvider
        // ----------------------------------------------------------------

        [Test]
        public void WithIdentityProvider_NullProvider_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithIdentityProvider(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithIdentityProvider_SetsProviderOnOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IClientIdentityProvider provider = new AnonymousIdentityProvider();

            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithIdentityProvider(provider)
                .Build();

            Assert.That(opts.IdentityProvider, Is.SameAs(provider));
        }

        // ----------------------------------------------------------------
        // WithTimeProvider
        // ----------------------------------------------------------------

        [Test]
        public void WithTimeProvider_NullProvider_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithTimeProvider(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithTimeProvider_SetsProviderOnOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            TimeProvider fakeTime = TimeProvider.System;

            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithTimeProvider(fakeTime)
                .Build();

            Assert.That(opts.TimeProvider, Is.SameAs(fakeTime));
        }

        // ----------------------------------------------------------------
        // WithTransferSubscriptionsOnRecreate
        // ----------------------------------------------------------------

        [Test]
        public void WithTransferSubscriptionsOnRecreate_DefaultsToTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithTransferSubscriptionsOnRecreate()
                .Build();

            Assert.That(opts.TransferSubscriptionsOnRecreate, Is.True);
        }

        [Test]
        public void WithTransferSubscriptionsOnRecreate_CanBeSetFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithTransferSubscriptionsOnRecreate(false)
                .Build();

            Assert.That(opts.TransferSubscriptionsOnRecreate, Is.False);
        }

        [Test]
        public void WithTokenReuseFailover_DefaultsToTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithTokenReuseFailover()
                .Build();

            Assert.That(opts.EnableTokenReuseFailover, Is.True);
        }

        [Test]
        public void WithTokenReuseFailover_CanBeSetFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithTokenReuseFailover(false)
                .Build();

            Assert.That(opts.EnableTokenReuseFailover, Is.False);
        }

        [Test]
        public void WithNetworkRedundancy_SetsAlternateEndpoints()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var alternateEndpoint = new ConfiguredEndpoint(
                null!,
                new EndpointDescription("opc.tcp://alternate:4840"));
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithNetworkRedundancy(new ArrayOf<ConfiguredEndpoint>(new[] { alternateEndpoint }.AsMemory()))
                .Build();

            Assert.That(opts.NetworkRedundancy, Is.Not.Null);
            Assert.That(opts.NetworkRedundancy!.AlternateEndpoints.Count, Is.EqualTo(1));
            Assert.That(opts.NetworkRedundancy.AlternateEndpoints[0], Is.SameAs(alternateEndpoint));
        }

        // ----------------------------------------------------------------
        // WithPoolNotifications
        // ----------------------------------------------------------------

        [Test]
        public void WithPoolNotifications_DefaultsToTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithPoolNotifications()
                .Build();

            Assert.That(opts.PoolNotifications, Is.True);
        }

        [Test]
        public void WithPoolNotifications_CanBeSetFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithPoolNotifications(false)
                .Build();

            Assert.That(opts.PoolNotifications, Is.False);
        }

        // ----------------------------------------------------------------
        // WithModelChangeTracking
        // ----------------------------------------------------------------

        [Test]
        public void WithModelChangeTracking_DefaultsToTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithModelChangeTracking()
                .Build();

            Assert.That(opts.ModelChangeTracking, Is.True);
        }

        [Test]
        public void WithModelChangeTracking_CanBeSetFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithModelChangeTracking(false)
                .Build();

            Assert.That(opts.ModelChangeTracking, Is.False);
        }

        // ----------------------------------------------------------------
        // UseSubscriptionEngine
        // ----------------------------------------------------------------

        [Test]
        public void UseSubscriptionEngine_NullFactory_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.UseSubscriptionEngine(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseSubscriptionEngine_SetsFactoryOnOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockFactory = new Mock<ISubscriptionEngineFactory>();

            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .UseSubscriptionEngine(mockFactory.Object)
                .Build();

            Assert.That(opts.SubscriptionEngineFactory, Is.SameAs(mockFactory.Object));
        }

        // ----------------------------------------------------------------
        // UseSessionFactory
        // ----------------------------------------------------------------

        [Test]
        public void UseSessionFactory_NullFactory_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.UseSessionFactory(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseSessionFactory_ReturnsBuilderForChaining()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockFactory = new Mock<ISessionFactory>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            ManagedSessionBuilder returned = builder.UseSessionFactory(mockFactory.Object);

            Assert.That(returned, Is.SameAs(builder));
        }

        // ----------------------------------------------------------------
        // WithChannelManager
        // ----------------------------------------------------------------

        [Test]
        public void WithChannelManager_NullManager_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithChannelManager(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithChannelManager_ReturnsBuilderForChaining()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockManager = new Mock<IClientChannelManager>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            ManagedSessionBuilder returned = builder.WithChannelManager(mockManager.Object);

            Assert.That(returned, Is.SameAs(builder));
        }

        // ----------------------------------------------------------------
        // WithHttpsResilience
        // ----------------------------------------------------------------

        [Test]
        public void WithHttpsResilience_NullConfigure_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithHttpsResilience(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithHttpsResilience_ReturnsBuilderForChaining()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            ManagedSessionBuilder returned = builder.WithHttpsResilience(_ => { });

            Assert.That(returned, Is.SameAs(builder));
        }

        // ----------------------------------------------------------------
        // WithServerRedundancy(IServerRedundancyHandler)
        // ----------------------------------------------------------------

        [Test]
        public void WithServerRedundancyHandler_NullHandler_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithServerRedundancy((IServerRedundancyHandler)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithServerRedundancyHandler_EnablesRedundancyOnOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockHandler = new Mock<IServerRedundancyHandler>();

            ManagedSessionOptions opts = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry)
                .WithServerRedundancy(mockHandler.Object)
                .Build();

            Assert.That(opts.EnableServerRedundancy, Is.True);
        }

        // ----------------------------------------------------------------
        // WithReconnectPolicy(IReconnectPolicy)
        // ----------------------------------------------------------------

        [Test]
        public void WithReconnectPolicyObject_NullPolicy_Throws()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            Assert.That(
                () => builder.WithReconnectPolicy((IReconnectPolicy)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithReconnectPolicyObject_ReturnsBuilderForChaining()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockPolicy = new Mock<IReconnectPolicy>();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);

            ManagedSessionBuilder returned = builder.WithReconnectPolicy(mockPolicy.Object);

            Assert.That(returned, Is.SameAs(builder));
        }

        // ----------------------------------------------------------------
        // Chaining — all With*/Use* methods return the same builder
        // ----------------------------------------------------------------

        [Test]
        public void AllWithMethods_ReturnSameBuilderForChaining()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfig(telemetry), telemetry);
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            }, configuration: null);

            var mockPolicy = new Mock<IReconnectPolicy>();
            var mockHandler = new Mock<IServerRedundancyHandler>();
            var mockFactory = new Mock<ISubscriptionEngineFactory>();
            var mockSessionFactory = new Mock<ISessionFactory>();
            var mockChannelMgr = new Mock<IClientChannelManager>();
            IClientIdentityProvider idProvider = new AnonymousIdentityProvider();

            ManagedSessionBuilder result = builder
                .UseEndpoint(endpoint)
                .WithSessionName("Chain")
                .WithSessionTimeout(TimeSpan.FromMinutes(2))
                .WithCheckDomain(false)
                .WithPreferredLocales("en-US")
                .WithServerRedundancy()
                .WithServerRedundancy(mockHandler.Object)
                .WithReconnectPolicy(mockPolicy.Object)
                .WithReconnectPolicy(p => p with { MaxRetries = 10 })
                .WithIdentityProvider(idProvider)
                .WithTimeProvider(TimeProvider.System)
                .WithTransferSubscriptionsOnRecreate()
                .WithPoolNotifications()
                .WithModelChangeTracking()
                .UseSubscriptionEngine(mockFactory.Object)
                .UseSessionFactory(mockSessionFactory.Object)
                .WithChannelManager(mockChannelMgr.Object)
                .WithHttpsResilience(_ => { });

            Assert.That(result, Is.SameAs(builder));
            ManagedSessionOptions opts = builder.Build();
            Assert.That(opts.SessionName, Is.EqualTo("Chain"));
            Assert.That(opts.TransferSubscriptionsOnRecreate, Is.True);
            Assert.That(opts.PoolNotifications, Is.True);
            Assert.That(opts.ModelChangeTracking, Is.True);
        }
    }
}
