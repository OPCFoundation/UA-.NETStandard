// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
    public sealed class OpcUaClientServiceCollectionExtensionsTests
    {
        [Test]
        public void AddOpcUaClientThrowsForNullArgs()
        {
            Assert.That(() => OpcUaClientServiceCollectionExtensions
                .AddOpcUaClient(null!, _ => { }), Throws.ArgumentNullException);

            var services = new ServiceCollection();
            Assert.That(() => services.AddOpcUaClient(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void AddOpcUaClientRegistersExpectedServices()
        {
            var services = new ServiceCollection();
            services.AddOpcUaClient(opt =>
            {
                opt.Configuration = CreateConfig();
                opt.Session = new ManagedSessionOptions
                {
                    Endpoint = new ConfiguredEndpoint(null, new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840"
                    })
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<OpcUaClientOptions>(), Is.Not.Null);
            Assert.That(sp.GetService<ITelemetryContext>(), Is.Not.Null);
            Assert.That(sp.GetService<ISessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<ManagedSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<Func<CancellationToken, Task<Opc.Ua.Client.ManagedSession>>>(), Is.Not.Null);
        }

        [Test]
        public void AddOpcUaClientReturnsBuilderWithServices()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUaClient(opt =>
            {
                opt.Configuration = CreateConfig();
            });

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));
        }

        [Test]
        public void SessionFactoryHasV2EngineByDefault()
        {
            var services = new ServiceCollection();
            services.AddOpcUaClient(opt =>
            {
                opt.Configuration = CreateConfig();
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            ISessionFactory? factory = sp.GetService<ISessionFactory>();
            Assert.That(factory, Is.Not.Null);
            Assert.That(factory, Is.InstanceOf<DefaultSessionFactory>());
            var dsf = (DefaultSessionFactory)factory!;
            Assert.That(dsf.SubscriptionEngineFactory, Is.Not.Null);
            Assert.That(dsf.SubscriptionEngineFactory,
                Is.InstanceOf<DefaultSubscriptionEngineFactory>());
        }

        private static ApplicationConfiguration CreateConfig()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "test",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
