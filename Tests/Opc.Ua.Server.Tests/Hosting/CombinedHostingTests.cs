/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.WotCon.Server;

#nullable enable

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Integration tests for the unified OPC UA dependency-injection
    /// surface across multiple co-hosted features (regular server,
    /// client, LDS server, GDS server, WoT Connectivity).
    /// </summary>
    /// <remarks>
    /// These tests <strong>only</strong> exercise the DI registration
    /// shape and the build of the service provider. They do not start
    /// any hosted service (which would require certificates and ports).
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class CombinedHostingTests
    {
        [Test]
        public void AddOpcUaIsIdempotentAcrossFeatures()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddOpcUa()
                .AddServer(opt => ConfigureServerOptions(opt, "IdempotentServer"))
                .Services.AddOpcUa()
                .AddClient(_ => { });

            int telemetryCount = services.Count(
                d => d.ServiceType == typeof(ITelemetryContext));
            Assert.That(telemetryCount, Is.EqualTo(1),
                "ITelemetryContext must be registered exactly once even across multiple features.");
        }

        [Test]
        public void CustomTelemetryContextSurvivesAddOpcUaBeforeChain()
        {
            ITelemetryContext custom = Mock.Of<ITelemetryContext>();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(custom);

            services.AddOpcUa()
                .AddServer(opt => ConfigureServerOptions(opt, "CustomTelemetryServer"))
                .Services.AddOpcUa()
                .AddClient(_ => { });

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<ITelemetryContext>(), Is.SameAs(custom),
                "A user-supplied ITelemetryContext registered before AddOpcUa must be preserved.");
        }

        [Test]
        public void ServerAndLdsCanCoexistInOneHost()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddOpcUa()
                .AddServer(opt =>
                {
                    ConfigureServerOptions(opt, "CoexistServer");
                    opt.EndpointUrls.Clear();
                    opt.EndpointUrls.Add("opc.tcp://localhost:0/CoexistServer");
                })
                .Services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationName = "CoexistLds";
                    opt.ApplicationUri = "urn:localhost:Test:CoexistLds";
                    opt.ProductUri = "uri:opcfoundation.org:Test:CoexistLds";
                    opt.EndpointUrls.Add("opc.tcp://localhost:0/CoexistLds");
                    opt.EnableMulticast = false;
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            IList<IHostedService> hosted = [.. sp.GetServices<IHostedService>()];
            int serverCount = hosted.Count(h => h is OpcUaServerHostedService);
            int ldsCount = hosted.Count(h => h.GetType().Name == "LdsServerHostedService");

            Assert.That(serverCount, Is.EqualTo(1),
                "Exactly one OpcUaServerHostedService must be registered.");
            Assert.That(ldsCount, Is.EqualTo(1),
                "Exactly one LdsServerHostedService must be registered.");
        }

        [Test]
        public void ServerAndGdsCanCoexistInOneHost()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            AddGdsMocks(services);

            services.AddOpcUa()
                .AddServer(opt =>
                {
                    ConfigureServerOptions(opt, "CoexistRegular");
                    opt.EndpointUrls.Clear();
                    opt.EndpointUrls.Add("opc.tcp://localhost:0/CoexistRegular");
                })
                .Services.AddOpcUa()
                .AddGdsServer(opt => ConfigureGdsOptions(opt, "CoexistGds"));

            using ServiceProvider sp = services.BuildServiceProvider();

            IList<IHostedService> hosted = [.. sp.GetServices<IHostedService>()];
            int serverCount = hosted.Count(h => h is OpcUaServerHostedService);
            int gdsCount = hosted.Count(h => h.GetType().Name == "GdsServerHostedService");

            Assert.That(serverCount, Is.EqualTo(1),
                "Exactly one OpcUaServerHostedService must be registered.");
            Assert.That(gdsCount, Is.EqualTo(1),
                "Exactly one GdsServerHostedService must be registered.");
        }

        [Test]
        public void GdsAndLdsCanCoexistInOneHost()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            AddGdsMocks(services);

            services.AddOpcUa()
                .AddGdsServer(opt => ConfigureGdsOptions(opt, "BothGds"))
                .Services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationName = "BothLds";
                    opt.ApplicationUri = "urn:localhost:Test:BothLds";
                    opt.ProductUri = "uri:opcfoundation.org:Test:BothLds";
                    opt.EndpointUrls.Add("opc.tcp://localhost:0/BothLds");
                    opt.EnableMulticast = false;
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            IList<IHostedService> hosted = [.. sp.GetServices<IHostedService>()];
            int gdsCount = hosted.Count(h => h.GetType().Name == "GdsServerHostedService");
            int ldsCount = hosted.Count(h => h.GetType().Name == "LdsServerHostedService");

            Assert.That(gdsCount, Is.EqualTo(1),
                "Exactly one GdsServerHostedService must be registered.");
            Assert.That(ldsCount, Is.EqualTo(1),
                "Exactly one LdsServerHostedService must be registered.");
        }

        [Test]
        public void DuplicateAddServerThrows()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(opt => ConfigureServerOptions(opt, "FirstServer"));

            Assert.That(
                () => services.AddOpcUa()
                    .AddServer(opt => ConfigureServerOptions(opt, "SecondServer")),
                Throws.InvalidOperationException,
                "AddServer must throw when called twice on the same service collection.");
        }

        [Test]
        public void DuplicateAddGdsServerThrows()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddGdsServer(opt => ConfigureGdsOptions(opt, "FirstGds"));

            Assert.That(
                () => services.AddOpcUa()
                    .AddGdsServer(opt => ConfigureGdsOptions(opt, "SecondGds")),
                Throws.InvalidOperationException,
                "AddGdsServer must throw when called twice on the same service collection.");
        }

        [Test]
        public void DuplicateAddLdsServerThrows()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationName = "FirstLds";
                    opt.ApplicationUri = "urn:localhost:Test:FirstLds";
                    opt.ProductUri = "uri:opcfoundation.org:Test:FirstLds";
                });

            Assert.That(
                () => services.AddOpcUa()
                    .AddLdsServer(opt =>
                    {
                        opt.ApplicationName = "SecondLds";
                        opt.ApplicationUri = "urn:localhost:Test:SecondLds";
                        opt.ProductUri = "uri:opcfoundation.org:Test:SecondLds";
                    }),
                Throws.InvalidOperationException,
                "AddLdsServer must throw when called twice on the same service collection.");
        }

        [Test]
        public void NodeManagerRegisteredOnRegularServerIsIsolatedFromGdsLds()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            AddGdsMocks(services);

            services.AddOpcUa()
                .AddServer(opt => ConfigureServerOptions(opt, "NmIsolationServer"))
                .AddNodeManager<FakeAsyncNodeManagerFactory>()
                .Services.AddOpcUa()
                .AddGdsServer(opt => ConfigureGdsOptions(opt, "NmIsolationGds"))
                .Services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationName = "NmIsolationLds";
                    opt.ApplicationUri = "urn:localhost:Test:NmIsolationLds";
                    opt.ProductUri = "uri:opcfoundation.org:Test:NmIsolationLds";
                    opt.EndpointUrls.Add("opc.tcp://localhost:0/NmIsolationLds");
                    opt.EnableMulticast = false;
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            IList<OpcUaServerNodeManagerRegistration> registrations =
                [.. sp.GetServices<OpcUaServerNodeManagerRegistration>()];

            int fakeCount = registrations.Count(
                r => r.AsyncFactory is FakeAsyncNodeManagerFactory);
            Assert.That(fakeCount, Is.EqualTo(1),
                "FakeAsyncNodeManagerFactory should appear exactly once " +
                "in the OpcUaServerNodeManagerRegistration enumerable.");

            // The GDS and LDS hosted services do not consume the
            // OpcUaServerNodeManagerRegistration enumerable: their
            // hosted-service ctors do not depend on it. Verify the
            // design invariant by inspecting the IHostedService
            // descriptors directly.
            ServiceDescriptor? gdsDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "GdsServerHostedService");
            ServiceDescriptor? ldsDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "LdsServerHostedService");
            Assert.That(gdsDescriptor, Is.Not.Null);
            Assert.That(ldsDescriptor, Is.Not.Null);

            bool gdsTakesRegistrations = HasParameterOfType(
                gdsDescriptor!.ImplementationType!,
                typeof(IEnumerable<OpcUaServerNodeManagerRegistration>));
            bool ldsTakesRegistrations = HasParameterOfType(
                ldsDescriptor!.ImplementationType!,
                typeof(IEnumerable<OpcUaServerNodeManagerRegistration>));

            Assert.That(gdsTakesRegistrations, Is.False,
                "GDS hosted service must not consume OpcUaServerNodeManagerRegistration.");
            Assert.That(ldsTakesRegistrations, Is.False,
                "LDS hosted service must not consume OpcUaServerNodeManagerRegistration.");
        }

        [Test]
        public void AddWotConServerRegistersUnderServerFeature()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddOpcUa()
                .AddServer(opt => ConfigureServerOptions(opt, "WotConHostServer"))
                .Services.AddOpcUa()
                .AddWotConServer(opt =>
                {
                    opt.AssetNamespaceUri = "urn:test:wot:assets";
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            IList<OpcUaServerNodeManagerRegistration> registrations =
                [.. sp.GetServices<OpcUaServerNodeManagerRegistration>()];

            int wotCount = registrations.Count(
                r => r.SyncFactory is WotConnectivityNodeManagerFactory);
            Assert.That(wotCount, Is.EqualTo(1),
                "WotConnectivityNodeManagerFactory must be registered exactly once " +
                "as an OpcUaServerNodeManagerRegistration.");

            // The WotCon node-manager factory itself must be resolvable
            // for tooling / introspection.
            WotConnectivityNodeManagerFactory factory =
                sp.GetRequiredService<WotConnectivityNodeManagerFactory>();
            Assert.That(factory, Is.Not.Null);
        }

        private static void ConfigureServerOptions(OpcUaServerOptions opt, string name)
        {
            opt.ApplicationName = name;
            opt.ApplicationUri = $"urn:localhost:Test:{name}";
            opt.ProductUri = $"uri:opcfoundation.org:Test:{name}";
            opt.EndpointUrls.Add($"opc.tcp://localhost:0/{name}");
            opt.AutoAcceptUntrustedCertificates = true;
        }

        private static void ConfigureGdsOptions(GdsServerOptions opt, string name)
        {
            opt.ApplicationName = name;
            opt.ApplicationUri = $"urn:localhost:Test:{name}";
            opt.ProductUri = $"uri:opcfoundation.org:Test:{name}";
            opt.AutoApprove = false;
        }

        /// <summary>
        /// Registers minimal Moq-backed implementations of the four
        /// pluggable services that <c>GdsServerHostedService</c> requires
        /// in its constructor so the service provider can construct the
        /// hosted service when an enumeration of
        /// <see cref="IHostedService"/> is requested.
        /// </summary>
        private static void AddGdsMocks(IServiceCollection services)
        {
            services.AddSingleton(Mock.Of<IApplicationsDatabase>());
            services.AddSingleton(Mock.Of<IUserDatabase>());
            services.AddSingleton(Mock.Of<ICertificateRequest>());
            services.AddSingleton(Mock.Of<ICertificateGroup>());
        }

        private static bool HasParameterOfType(Type implementationType, Type parameterType)
        {
            foreach (System.Reflection.ConstructorInfo ctor in implementationType.GetConstructors(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance))
            {
                foreach (System.Reflection.ParameterInfo p in ctor.GetParameters())
                {
                    if (p.ParameterType == parameterType)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// A non-functional <see cref="IAsyncNodeManagerFactory"/> used
        /// to verify that node-manager registrations created via the
        /// regular-server feature appear in the
        /// <see cref="OpcUaServerNodeManagerRegistration"/> enumerable
        /// and nowhere else.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by the DI container via the registration under test.")]
        private sealed class FakeAsyncNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris { get; } = ["urn:test:fake"];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException(
                    "FakeAsyncNodeManagerFactory is a registration-only stub " +
                    "and must not be instantiated by the hosted server during tests.");
            }
        }
    }
}
