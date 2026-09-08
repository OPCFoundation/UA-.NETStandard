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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Exercises composition on the default hosted server, with only its network transport replaced.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [Category("Server")]
    [SetCulture("en-US")]
    [SetUICulture("en-US")]
    [NonParallelizable]
    public sealed class ServerCompositionHostingTests
    {
        [Test]
        public void CompositionOverloadsRejectNullBuilders()
        {
            IOpcUaServerBuilder builder = null!;
            var authenticator = new AnonymousAuthenticator();
            IAsyncNodeManagerFactory asyncFactory = Mock.Of<IAsyncNodeManagerFactory>();
            INodeManagerFactory syncFactory = Mock.Of<INodeManagerFactory>();
            Action[] calls =
            [
                () => builder.ConfigureServerProperties(_ => { }),
                () => builder.ConfigureResources(_ => { }),
                () => builder.ConfigureResources((_, _) => { }),
                () => builder.AddIdentityAuthenticator(authenticator),
                () => builder.AddIdentityAuthenticator((_, _) => authenticator),
                () => builder.AddStartupTask<CompositionStartupTask>(),
                () => builder.AddStartupTask((_, _, _) => default),
                () => builder.AddNodeManager(asyncFactory),
                () => builder.AddNodeManager(syncFactory),
                () => builder.AddNodeManagers((_, _) => []),
                () => builder.ConfigureAliasNames(_ => { })
            ];

            foreach (Action call in calls)
            {
                Assert.That(call, Throws.ArgumentNullException
                    .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
            }
        }

        [Test]
        public void CompositionOverloadsRejectNullArgumentsWithoutRegisteringServices()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            int count = builder.Services.Count;
            (Action Call, string Parameter)[] calls =
            [
                (() => builder.ConfigureServerProperties(null!), "configure"),
                (() => builder.ConfigureResources((Action<ResourceManager>)null!), "configure"),
                (() => builder.ConfigureResources((Action<IServiceProvider, ResourceManager>)null!), "configure"),
                (() => builder.AddIdentityAuthenticator(instance: null!), "instance"),
                (() => builder.AddIdentityAuthenticator(factory: null!), "factory"),
                (() => builder.AddStartupTask(null!), "callback"),
                (() => builder.AddNodeManager((IAsyncNodeManagerFactory)null!), "factory"),
                (() => builder.AddNodeManager((INodeManagerFactory)null!), "factory"),
                (() => builder.AddNodeManagers(null!), "factories"),
                (() => builder.ConfigureAliasNames(null!), "configure")
            ];

            foreach ((Action call, string parameter) in calls)
            {
                Assert.That(call, Throws.ArgumentNullException
                    .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo(parameter));
                Assert.That(builder.Services, Has.Count.EqualTo(count));
            }
        }

        [Test]
        public void CompositionOverloadsReturnOriginalBuilderAndDeferCallbacks()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            int calls = 0;
            var authenticator = new AnonymousAuthenticator();
            IAsyncNodeManagerFactory asyncFactory = Mock.Of<IAsyncNodeManagerFactory>();
            INodeManagerFactory syncFactory = Mock.Of<INodeManagerFactory>();

            Assert.That(builder.ConfigureServerProperties(_ => calls++), Is.SameAs(builder));
            Assert.That(builder.ConfigureResources(_ => calls++), Is.SameAs(builder));
            Assert.That(builder.ConfigureResources((_, _) => calls++), Is.SameAs(builder));
            Assert.That(builder.AddIdentityAuthenticator(authenticator), Is.SameAs(builder));
            Assert.That(builder.AddIdentityAuthenticator((_, _) =>
            {
                calls++;
                return authenticator;
            }), Is.SameAs(builder));
            Assert.That(builder.AddStartupTask<CompositionStartupTask>(), Is.SameAs(builder));
            Assert.That(builder.AddStartupTask((_, _, _) =>
            {
                calls++;
                return default;
            }), Is.SameAs(builder));
            Assert.That(builder.AddNodeManager(asyncFactory), Is.SameAs(builder));
            Assert.That(builder.AddNodeManager(syncFactory), Is.SameAs(builder));
            Assert.That(builder.AddNodeManagers((_, _) =>
            {
                calls++;
                return [];
            }), Is.SameAs(builder));
            Assert.That(builder.ConfigureAliasNames(_ => calls++), Is.SameAs(builder));
            Assert.That(calls, Is.Zero);

            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            OpcUaServerNodeManagerRegistration[] registrations = provider
                .GetServices<OpcUaServerNodeManagerRegistration>().ToArray();
            Assert.That(registrations, Has.Length.EqualTo(3));
            Assert.That(registrations[0].AsyncFactory, Is.SameAs(asyncFactory));
            Assert.That(registrations[0].SyncFactory, Is.Null);
            Assert.That(registrations[1].SyncFactory, Is.SameAs(syncFactory));
            Assert.That(registrations[1].AsyncFactory, Is.Null);
            Assert.That(calls, Is.Zero, "Resolving wrappers must not execute configuration-aware callbacks.");
        }

        [Test]
        public void IdentityFactoryRejectsNullResultAtResolution()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            int calls = 0;
            builder.AddIdentityAuthenticator((_, _) =>
            {
                calls++;
                return null!;
            });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            OpcUaServerIdentityAuthenticatorRegistration registration = provider
                .GetServices<OpcUaServerIdentityAuthenticatorRegistration>().Last();

            Assert.That(calls, Is.Zero);
            Assert.That(
                () => registration.CreateAuthenticators(provider, null).ToArray(),
                Throws.InvalidOperationException.With.Message.Contains("returned null"));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void AuthenticatorInstanceKeepsItsIdentityAndRemainsCallerOwned()
        {
            var authenticator = new Mock<IUserTokenAuthenticator>(MockBehavior.Strict);
            authenticator.As<IDisposable>();
            IOpcUaServerBuilder builder = CreateBuilder().AddIdentityAuthenticator(authenticator.Object);
            using (ServiceProvider provider = builder.Services.BuildServiceProvider())
            {
                OpcUaServerIdentityAuthenticatorRegistration registration = provider
                    .GetServices<OpcUaServerIdentityAuthenticatorRegistration>().Last();
                IUserTokenAuthenticator[] created = registration.CreateAuthenticators(provider, null).ToArray();

                Assert.That(created, Has.Length.EqualTo(1));
                Assert.That(created[0], Is.SameAs(authenticator.Object));
                Assert.That(registration.IsFallback, Is.False);
            }
            authenticator.As<IDisposable>().Verify(instance => instance.Dispose(), Times.Never);
        }

        [Test]
        public async Task StartupDelegateForwardsServicesContextCancellationAndCompletionAsync()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            var dependency = new CompositionObservations();
            var release = NewSignal<bool>();
            var entered = NewSignal<bool>();
            IServerContext context = Mock.Of<IServerContext>();
            using var cancellation = new CancellationTokenSource();
            builder.Services.AddSingleton(dependency);
            builder.AddStartupTask(async (services, server, ct) =>
            {
                Assert.That(services.GetRequiredService<CompositionObservations>(), Is.SameAs(dependency));
                Assert.That(server, Is.SameAs(context));
                Assert.That(ct, Is.EqualTo(cancellation.Token));
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                dependency.Events.Enqueue("completed");
            });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            IServerStartupTask task = provider.GetServices<IServerStartupTask>().Single();

            Task running = task.OnServerStartedAsync(context, cancellation.Token).AsTask();
            try
            {
                await AwaitBoundedAsync(entered.Task).ConfigureAwait(false);
                Assert.That(running.IsCompleted, Is.False);
                Assert.That(dependency.Events, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await AwaitBoundedAsync(running).ConfigureAwait(false);
            Assert.That(dependency.Events, Is.EqualTo(s_completedEvents));
        }

        [Test]
        public async Task HostedStartupTasksAreAwaitedInOrderAndGenericRegistrationIsIdempotentAsync()
        {
            var observations = new CompositionObservations();
            var entered = NewSignal<bool>();
            var release = NewSignal<bool>();
            HostedFixture fixture = HostedFixture.Create(builder =>
            {
                builder.Services.AddSingleton(observations);
                builder.Services.AddSingleton<IServerStartupTask>(new ExistingStartupTask(observations));
                builder.AddStartupTask<CompositionStartupTask>();
                builder.AddStartupTask<CompositionStartupTask>();
                builder.AddStartupTask(async (_, context, ct) =>
                {
                    Assert.That(context.CurrentState, Is.EqualTo(ServerState.Running));
                    Assert.That(ct.CanBeCanceled, Is.True);
                    observations.Events.Enqueue("delegate-entered");
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    observations.Events.Enqueue("delegate-completed");
                });
                builder.AddStartupTask((_, _, _) =>
                {
                    observations.Events.Enqueue("last");
                    return default;
                });
            });
            await using var cleanup = fixture.ConfigureAwait(false);

            Task starting = fixture.StartAsync();
            try
            {
                await AwaitBoundedAsync(entered.Task).ConfigureAwait(false);
                Assert.That(starting.IsCompleted, Is.False);
                Assert.That(observations.Events, Is.EqualTo(s_enteredEvents));
            }
            finally
            {
                release.TrySetResult(true);
            }
            await AwaitBoundedAsync(starting).ConfigureAwait(false);

            Assert.That(observations.Events, Is.EqualTo(s_startupEvents));
            Assert.That(observations.StartupCount, Is.EqualTo(1));
            Assert.That(observations.Context, Is.SameAs(fixture.Context));
            HistoryServerCapabilitiesState history = fixture.Context
                .FindNodeManagers<IDiagnosticsNodeManager>().Single()
                .FindPredefinedNode<HistoryServerCapabilitiesState>(observations.HistoryNodeId);
            Assert.That(history.MaxReturnDataValues.Value, Is.EqualTo(73u));
            Assert.That(history.MaxReturnEventValues.Value, Is.EqualTo(29u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BuildInfoDefaultsComeFromEffectiveApplicationAsync(bool sharedApplication)
        {
            var fixture = new HostedFixture((services, root) =>
            {
                IOpcUaBuilder builder = services.AddOpcUa();
                if (sharedApplication)
                {
                    builder.ConfigureApplication(options =>
                    {
                        options.ApplicationName = "SharedComposition";
                        options.ApplicationUri = "urn:localhost:SharedComposition";
                        options.ProductUri = "urn:composition:shared-product";
                        options.PkiRoot = root;
                    });
                }
                return builder.AddServer(options => ConfigureOptions(options, root));
            });
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            BuildInfo info = ReadBuildInfo(fixture.Context);
            Assert.That(info.ProductName,
                Is.EqualTo(sharedApplication ? "SharedComposition" : "CompositionServer"));
            Assert.That(info.ProductUri,
                Is.EqualTo(sharedApplication ? "urn:composition:shared-product" : "urn:composition:product"));
            Assert.That(info.SoftwareVersion, Is.EqualTo(Utils.GetAssemblySoftwareVersion()));
            Assert.That(info.BuildNumber, Is.EqualTo(Utils.GetAssemblyBuildNumber()));
            Assert.That(info.ManufacturerName, Is.Empty);
            Assert.That(info.BuildDate, Is.EqualTo((DateTimeUtc)DateTime.MinValue));
            Assert.That(fixture.Context.CurrentState, Is.EqualTo(ServerState.Running));
            Assert.That(fixture.Server, Is.InstanceOf<ReverseConnectServer>());
        }

        [Test]
        public async Task ConfiguredServerPropertiesPopulateRuntimeBuildInfoWithoutChangingConfigurationAsync()
        {
            var buildDate = new DateTime(2025, 11, 23, 14, 15, 16, DateTimeKind.Utc);
            HostedFixture fixture = HostedFixture.Create(builder =>
                builder.ConfigureServerProperties(properties =>
                {
                    properties.ProductName = "Composition Product";
                    properties.ProductUri = "urn:composition:published-product";
                    properties.ManufacturerName = "Composition Manufacturer";
                    properties.SoftwareVersion = "7.8.9";
                    properties.BuildNumber = "build-123";
                    properties.BuildDate = buildDate;
                }));
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            BuildInfo info = ReadBuildInfo(fixture.Context);
            Assert.That(info.ProductName, Is.EqualTo("Composition Product"));
            Assert.That(info.ProductUri, Is.EqualTo("urn:composition:published-product"));
            Assert.That(info.ManufacturerName, Is.EqualTo("Composition Manufacturer"));
            Assert.That(info.SoftwareVersion, Is.EqualTo("7.8.9"));
            Assert.That(info.BuildNumber, Is.EqualTo("build-123"));
            Assert.That(info.BuildDate, Is.EqualTo((DateTimeUtc)buildDate));
            Assert.That(fixture.Configuration.ApplicationName, Is.EqualTo("CompositionServer"));
            Assert.That(fixture.Configuration.ProductUri, Is.EqualTo("urn:composition:product"));
            Assert.That(ReadValue(fixture.Context, VariableIds.Server_ServerStatus_BuildInfo_ProductName)
                .WrappedValue.GetString(), Is.EqualTo(info.ProductName));
        }

        [Test]
        public async Task DirectServerPropertiesWinOptionsWithoutMutatingEitherRegistrationAsync()
        {
            var direct = new ServerProperties
            {
                ProductName = string.Empty,
                ProductUri = string.Empty,
                ManufacturerName = "Instance Manufacturer",
                BuildNumber = "instance-build"
            };
            HostedFixture fixture = HostedFixture.Create(builder =>
            {
                builder.ConfigureServerProperties(properties =>
                {
                    properties.ProductName = "Ignored Option Name";
                    properties.ProductUri = "urn:ignored:options";
                    properties.SoftwareVersion = "99.99";
                    properties.ManufacturerName = "Ignored Option Manufacturer";
                });
                builder.Services.AddSingleton(direct);
            });
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            BuildInfo info = ReadBuildInfo(fixture.Context);
            Assert.That(info.ProductName, Is.EqualTo("CompositionServer"));
            Assert.That(info.ProductUri, Is.EqualTo("urn:composition:product"));
            Assert.That(info.ManufacturerName, Is.EqualTo("Instance Manufacturer"));
            Assert.That(info.BuildNumber, Is.EqualTo("instance-build"));
            Assert.That(info.SoftwareVersion, Is.EqualTo(Utils.GetAssemblySoftwareVersion()),
                "Unset instance fields use runtime defaults, not fields from the losing options object.");
            Assert.That(direct.ProductName, Is.Empty);
            Assert.That(direct.ProductUri, Is.Empty);
            Assert.That(direct.SoftwareVersion, Is.Null.Or.Empty);
            ServerProperties options = fixture.Services.GetRequiredService<IOptions<ServerProperties>>().Value;
            Assert.That(options.ProductName, Is.EqualTo("Ignored Option Name"));
            Assert.That(options.ProductUri, Is.EqualTo("urn:ignored:options"));
            Assert.That(options.SoftwareVersion, Is.EqualTo("99.99"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LoadedConfigurationOverridesDriveBuildInfoAndDeferredFactoriesAsync(bool useStream)
        {
            using var factory = new MarkerNodeManagerFactory("urn:composition:loaded", 42);
            ApplicationConfiguration seenConfiguration = null!;
            int callbackCount = 0;
            int overrideCount = 0;
            var fixture = new HostedFixture((services, root) =>
            {
                IOpcUaBuilder rootBuilder = services.AddOpcUa().ConfigureApplication(options =>
                {
                    options.ApplicationName = "Ignored Shared Name";
                    options.ProductUri = "urn:ignored:shared";
                    options.PkiRoot = root;
                });
                string xml = CreateConfigurationXml(root);
                IOpcUaServerBuilder serverBuilder;
                if (useStream)
                {
                    serverBuilder = rootBuilder.AddServer(
                        new MemoryStream(Encoding.UTF8.GetBytes(xml)),
                        OverrideConfiguration);
                }
                else
                {
                    string path = Path.Combine(root, "server.xml");
                    File.WriteAllText(path, xml);
                    serverBuilder = rootBuilder.AddServer(path, OverrideConfiguration);
                }
                serverBuilder.Services.Configure<OpcUaServerOptions>(options =>
                {
                    options.ApplicationName = "Ignored Options Name";
                    options.ProductUri = "urn:ignored:options";
                });
                return serverBuilder.AddNodeManagers((services, configuration) =>
                {
                    Assert.That(services.GetService<ApplicationConfiguration>(), Is.Null);
                    Assert.That(overrideCount, Is.EqualTo(1));
                    seenConfiguration = configuration;
                    callbackCount++;
                    return [factory];
                });
            });
            await using var cleanup = fixture.ConfigureAwait(false);

            Assert.That(callbackCount, Is.Zero);
            Assert.That(factory.CreateCount, Is.Zero);
            await fixture.StartAsync().ConfigureAwait(false);

            BuildInfo info = ReadBuildInfo(fixture.Context);
            Assert.That(info.ProductName, Is.EqualTo("Loaded Override"));
            Assert.That(info.ProductUri, Is.EqualTo("urn:composition:loaded-override"));
            Assert.That(seenConfiguration, Is.SameAs(fixture.Configuration));
            Assert.That(factory.Configuration, Is.SameAs(seenConfiguration));
            Assert.That(seenConfiguration.TransportQuotas.MaxStringLength, Is.EqualTo(654321));
            Assert.That(seenConfiguration.ServerConfiguration.MaxSessionCount, Is.EqualTo(77));
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            AssertMarker(fixture.Context, factory.NamespacesUris[0], 42);

            void OverrideConfiguration(ApplicationConfiguration configuration)
            {
                overrideCount++;
                Assert.That(configuration.ApplicationName, Is.EqualTo("XmlComposition"));
                configuration.ApplicationName = "Loaded Override";
                configuration.ProductUri = "urn:composition:loaded-override";
            }
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(3, false)]
        public async Task ConfigurationFactoryCanProduceZeroOneOrManyManagersInOrderAsync(
            int count,
            bool returnNullArray)
        {
            MarkerNodeManagerFactory[] factories = Enumerable.Range(0, count)
                .Select(index => new MarkerNodeManagerFactory("urn:composition:factory:" + index, 100 + index))
                .ToArray();
            int callbackCount = 0;
            HostedFixture fixture = HostedFixture.Create(builder =>
                builder.AddNodeManagers((services, configuration) =>
                {
                    Assert.That(configuration.ApplicationName, Is.EqualTo("CompositionServer"));
                    Assert.That(services.GetService<ApplicationConfiguration>(), Is.Null);
                    callbackCount++;
                    return returnNullArray
                        ? default
                        : factories.Cast<IAsyncNodeManagerFactory>().ToArray().ToArrayOf();
                }));
            await using var cleanup = fixture.ConfigureAwait(false);

            Assert.That(callbackCount, Is.Zero);
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(fixture.Context.FindNodeManagers<MarkerNodeManager>().Count(), Is.EqualTo(count));
            int previousIndex = 0;
            for (int index = 0; index < count; index++)
            {
                MarkerNodeManagerFactory factory = factories[index];
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(factory.Configuration, Is.SameAs(fixture.Configuration));
                AssertMarker(fixture.Context, factory.NamespacesUris[0], 100 + index);
                int namespaceIndex = fixture.Context.DefaultSystemContext.NamespaceUris
                    .GetIndex(factory.NamespacesUris[0]);
                Assert.That(namespaceIndex, Is.GreaterThan(previousIndex));
                previousIndex = namespaceIndex;
            }
            Assert.That(ReadBuildInfo(fixture.Context).ProductName, Is.EqualTo("CompositionServer"));
        }

        [Test]
        public async Task GenericFactoryIsConstructedAfterConfigurationInsteadOfHostedServiceResolutionAsync()
        {
            var observations = new CompositionObservations();
            var fixture = new HostedFixture((services, root) =>
            {
                services.AddSingleton(observations);
                return services.AddOpcUa()
                    .AddServer(
                        new MemoryStream(Encoding.UTF8.GetBytes(CreateConfigurationXml(root))),
                        _ => observations.ConfigurationLoaded = true)
                    .AddNodeManager<DelayedNodeManagerFactory>();
            });
            await using var cleanup = fixture.ConfigureAwait(false);

            Assert.That(observations.FactoryConstructions, Is.Zero);
            Assert.That(observations.ConfigurationLoaded, Is.False);
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(observations.FactoryConstructions, Is.EqualTo(1));
            Assert.That(observations.FactorySawLoadedConfiguration, Is.True);
            Assert.That(ReadBuildInfo(fixture.Context).ProductName, Is.EqualTo("XmlComposition"));
            Assert.That(ReadBuildInfo(fixture.Context).ProductUri, Is.EqualTo("urn:composition:xml-product"));
            AssertMarker(fixture.Context, DelayedNodeManagerFactory.NamespaceUri, 51);
            Assert.That(fixture.Services.GetRequiredService<DelayedNodeManagerFactory>(),
                Is.SameAs(observations.Factory));
        }

        [Test]
        public async Task SuppliedAsyncAndLegacyFactoriesCreateLiveNodesAndRemainCallerOwnedAsync()
        {
            using var asyncFactory = new MarkerNodeManagerFactory("urn:composition:async-instance", 11);
            var syncFactory = new Mock<INodeManagerFactory>(MockBehavior.Strict);
            syncFactory.As<IDisposable>();
            syncFactory.SetupGet(factory => factory.NamespacesUris).Returns(["urn:composition:sync-instance"]);
            LegacyMarkerNodeManager legacyManager = null!;
            syncFactory.Setup(factory => factory.Create(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration) =>
                {
                    legacyManager = new LegacyMarkerNodeManager(server, configuration);
                    return legacyManager;
                });
            HostedFixture fixture = HostedFixture.Create(builder => builder
                .AddNodeManager(asyncFactory)
                .AddNodeManager(syncFactory.Object));
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.StartAsync().ConfigureAwait(false);
                AssertMarker(fixture.Context, "urn:composition:async-instance", 11);
                AssertMarker(fixture.Context, "urn:composition:sync-instance", 22);
                syncFactory.Verify(factory => factory.Create(
                    It.IsAny<IServerInternal>(), fixture.Configuration), Times.Once);
            }

            Assert.That(asyncFactory.CreateCount, Is.EqualTo(1));
            Assert.That(asyncFactory.DisposeCount, Is.Zero);
            Assert.That(asyncFactory.Manager.Disposed, Is.True);
            Assert.That(legacyManager.Disposed, Is.True);
            syncFactory.As<IDisposable>().Verify(factory => factory.Dispose(), Times.Never);
        }

        [Test]
        public async Task NullFactoryElementAbortsBeforeAnyAddressSpaceOrStartupTaskAsync()
        {
            int starts = 0;
            HostedFixture fixture = HostedFixture.Create(builder => builder
                .AddNodeManagers((_, _) => new ArrayOf<IAsyncNodeManagerFactory>(
                    new IAsyncNodeManagerFactory[] { null! }))
                .AddStartupTask((_, _, _) =>
                {
                    starts++;
                    return default;
                }));
            await using var cleanup = fixture.ConfigureAwait(false);

            Exception failure = await fixture.StartAndCaptureFailureAsync().ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure.Message, Does.Contain("null factory"));
            Assert.That(starts, Is.Zero);
            Assert.That(fixture.Transport.Server, Is.Null,
                "The invalid collection must fail before service hosts or node managers are started.");
        }

        [Test]
        public async Task StartupTaskFailureFaultsExecuteTaskStopsServerAndSkipsLaterTasksAsync()
        {
            var failure = new InvalidOperationException("composition-startup-failure");
            var observations = new CompositionObservations();
            using var factory = new MarkerNodeManagerFactory("urn:composition:failing-startup", 18);
            HostedFixture fixture = HostedFixture.Create(builder =>
            {
                builder.Services.AddSingleton(observations);
                builder.AddNodeManager(factory);
                builder.AddStartupTask((_, context, _) =>
                {
                    observations.Context = context;
                    AssertMarker(context, factory.NamespacesUris[0], 18);
                    throw failure;
                });
                builder.AddStartupTask((_, _, _) =>
                {
                    observations.StartupCount++;
                    return default;
                });
            });
            await using var cleanup = fixture.ConfigureAwait(false);

            Exception actual = await fixture.StartAndCaptureFailureAsync().ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(fixture.ExecuteTask.IsFaulted, Is.True);
            Assert.That(observations.StartupCount, Is.Zero);
            Assert.That(factory.Manager.Disposed, Is.True);
            Assert.That(factory.DisposeCount, Is.Zero);
            fixture.Transport.Listener.Verify(listener => listener.CloseAsync(
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            Assert.That(fixture.Logs.Errors, Does.Contain(failure));
        }

        [Test]
        public async Task StoppingDuringStartupCancelsTheCallbackAndDisposesTheServerAsync()
        {
            var entered = NewSignal<bool>();
            var cancelled = NewSignal<bool>();
            using var factory = new MarkerNodeManagerFactory("urn:composition:cancelled-startup", 26);
            int laterCalls = 0;
            CancellationToken observedToken = default;
            HostedFixture fixture = HostedFixture.Create(builder => builder
                .AddNodeManager(factory)
                .AddStartupTask(async (_, context, ct) =>
                {
                    observedToken = ct;
                    AssertMarker(context, factory.NamespacesUris[0], 26);
                    using CancellationTokenRegistration registration = ct.Register(() => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    await cancelled.Task.ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                })
                .AddStartupTask((_, _, _) =>
                {
                    laterCalls++;
                    return default;
                }));
            await using var cleanup = fixture.ConfigureAwait(false);
            Task starting = fixture.StartAsync();
            await AwaitBoundedAsync(entered.Task).ConfigureAwait(false);

            await fixture.StopAsync().ConfigureAwait(false);
            OperationCanceledException failure = null!;
            try
            {
                await starting.ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.CancellationToken, Is.EqualTo(observedToken));
            Assert.That(observedToken.IsCancellationRequested, Is.True);
            Assert.That(fixture.ExecuteTask.IsCanceled, Is.True);
            Assert.That(laterCalls, Is.Zero);
            Assert.That(factory.Manager.Disposed, Is.True);
            fixture.Transport.Listener.Verify(listener => listener.CloseAsync(
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public async Task NullAuthenticatorResultFaultsHostedStartupAndCleansUpRunningServerAsync()
        {
            int starts = 0;
            HostedFixture fixture = HostedFixture.Create(builder => builder
                .AddIdentityAuthenticator((_, _) => null!)
                .AddStartupTask((_, _, _) =>
                {
                    starts++;
                    return default;
                }));
            await using var cleanup = fixture.ConfigureAwait(false);

            Exception failure = await fixture.StartAndCaptureFailureAsync().ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure.Message, Does.Contain("authenticator factory returned null"));
            Assert.That(starts, Is.Zero);
            fixture.Transport.Listener.Verify(listener => listener.CloseAsync(
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public async Task UsernameInstanceAndCertificateFactoryForwardIdentityThroughLiveRegistryAsync()
        {
            byte[] password = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
            var userHandler = new UserNameIdentityTokenHandler("composition-user", password);
            var userIdentity = new UserIdentity(userHandler);
            using Certificate certificate = CertificateBuilder.Create("CN=Composition User")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            var certificateHandler = new X509IdentityTokenHandler(new X509IdentityToken
            {
                PolicyId = "certificate",
                CertificateData = certificate.RawData.ToByteString()
            });
            var certificateIdentity = new UserIdentity(certificateHandler);
            using var cancellation = new CancellationTokenSource();
            int userCalls = 0;
            int certificateCalls = 0;
            int factoryCalls = 0;
            ICertificateValidatorEx observedValidator = null!;
            var instance = new UserNamePasswordAuthenticator((handler, ct) =>
            {
                Assert.That(handler, Is.SameAs(userHandler));
                Assert.That(handler.DecryptedPassword, Is.EqualTo(password));
                Assert.That(ct, Is.EqualTo(cancellation.Token));
                userCalls++;
                return new ValueTask<IUserIdentity>(userIdentity);
            });
            var dependency = new CompositionObservations();
            HostedFixture fixture = HostedFixture.Create(builder =>
            {
                builder.Services.AddSingleton(dependency);
                builder.AddIdentityAuthenticator(instance);
                builder.AddIdentityAuthenticator((services, validator) =>
                {
                    Assert.That(services.GetRequiredService<CompositionObservations>(), Is.SameAs(dependency));
                    observedValidator = validator!;
                    factoryCalls++;
                    return new X509Authenticator((handler, ct) =>
                    {
                        Assert.That(handler, Is.SameAs(certificateHandler));
                        Assert.That(ct, Is.EqualTo(cancellation.Token));
                        certificateCalls++;
                        return new ValueTask<IUserIdentity>(certificateIdentity);
                    });
                });
            });
            await using var cleanup = fixture.ConfigureAwait(false);

            Assert.That(factoryCalls, Is.Zero);
            await fixture.StartAsync().ConfigureAwait(false);
            AuthenticationResult username = await fixture.Server.CurrentInstance.IdentityRegistry.AuthenticateAsync(
                CreateAuthenticationContext(fixture.Context, userHandler), cancellation.Token).ConfigureAwait(false);
            AuthenticationResult x509 = await fixture.Server.CurrentInstance.IdentityRegistry.AuthenticateAsync(
                CreateAuthenticationContext(fixture.Context, certificateHandler), cancellation.Token)
                .ConfigureAwait(false);

            Assert.That(username.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(username.Identity, Is.SameAs(userIdentity));
            Assert.That(username.Error, Is.Null);
            Assert.That(x509.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(x509.Identity, Is.SameAs(certificateIdentity));
            Assert.That(x509.Error, Is.Null);
            Assert.That(observedValidator, Is.SameAs(fixture.Configuration.CertificateManager));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(userCalls, Is.EqualTo(1));
            Assert.That(certificateCalls, Is.EqualTo(1));
            Assert.That(fixture.Configuration.ServerConfiguration.UserTokenPolicies
                .ToArray().Select(policy => policy.TokenType), Is.EqualTo(s_anonymousTokens),
                "Registering authentication must not implicitly advertise username or certificate policies.");
        }

        [Test]
        public async Task ResourceCallbacksRunAfterDefaultsInOrderOnTheLiveResourceManagerAsync()
        {
            var seen = new List<ResourceManager>();
            var observations = new CompositionObservations();
            HostedFixture fixture = HostedFixture.Create(builder =>
            {
                builder.Services.AddSingleton(observations);
                builder.ConfigureResources(resources =>
                {
                    seen.Add(resources);
                    ServiceResult builtIn = resources.Translate(["en-US"],
                        new ServiceResult(StatusCodes.BadNodeIdUnknown));
                    Assert.That(builtIn.LocalizedText.Text, Is.EqualTo("BadNodeIdUnknown"));
                    resources.Add("Composition.Greeting", "de-DE", "Erste Übersetzung");
                    observations.Events.Enqueue("first");
                });
                builder.ConfigureResources((services, resources) =>
                {
                    Assert.That(services.GetRequiredService<CompositionObservations>(), Is.SameAs(observations));
                    Assert.That(resources.Translate(["de-DE"], "Composition.Greeting", "fallback").Text,
                        Is.EqualTo("Erste Übersetzung"));
                    seen.Add(resources);
                    resources.Add("Composition.Greeting", "de-DE", "Guten Tag");
                    resources.Add("Composition.Greeting", "fr-FR", "Bonjour");
                    resources.Add(StatusCodes.BadNodeIdUnknown, "de-DE", "Unbekannter Knoten");
                    observations.Events.Enqueue("second");
                });
            });
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            ResourceManager resources = fixture.Server.CurrentInstance.ResourceManager;
            Assert.That(seen, Has.Count.EqualTo(2));
            Assert.That(seen[0], Is.SameAs(resources));
            Assert.That(seen[1], Is.SameAs(resources));
            Assert.That(observations.Events, Is.EqualTo(s_resourceEvents));
            LocalizedText german = resources.Translate(["de-DE"], "Composition.Greeting", "fallback");
            Assert.That(german.Text, Is.EqualTo("Guten Tag"));
            Assert.That(german.Locale, Is.EqualTo("de-DE"));
            Assert.That(resources.Translate(["fr-FR"], "Composition.Greeting", "fallback").Text,
                Is.EqualTo("Bonjour"));
            ServiceResult translated = resources.Translate(["de-DE"], new ServiceResult(StatusCodes.BadNodeIdUnknown));
            Assert.That(translated.LocalizedText.Text, Is.EqualTo("Unbekannter Knoten"));
            Assert.That(translated.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
            Assert.That(resources.Translate(["en-US"], new ServiceResult(StatusCodes.BadNodeIdUnknown))
                .LocalizedText.Text, Is.EqualTo("BadNodeIdUnknown"));
        }

        [Test]
        public void ResourceManagersAreNotSharedByServersCreatedFromOneProvider()
        {
            int calls = 0;
            IOpcUaServerBuilder builder = CreateBuilder().ConfigureResources(resources =>
            {
                calls++;
                resources.Add("Composition.Shared", "en-US", "Configured");
            });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            using var first = new ResourceHookServer(provider);
            using var second = new ResourceHookServer(provider);
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using ResourceManager firstResources = first.CreateResources(configuration);
            using ResourceManager secondResources = second.CreateResources(configuration);

            firstResources.Add("Composition.Shared", "en-US", "Only first");

            Assert.That(calls, Is.EqualTo(2));
            Assert.That(firstResources, Is.Not.SameAs(secondResources));
            Assert.That(secondResources.Translate(["en-US"], "Composition.Shared", "fallback").Text,
                Is.EqualTo("Configured"));
            Assert.That(firstResources.Translate(["en-US"], "Composition.Shared", "fallback").Text,
                Is.EqualTo("Only first"));
            Assert.That(provider.GetService<ResourceManager>(), Is.Null);
        }

        [Test]
        public async Task ResourceCallbackFailurePreventsStartupTasksAndClosesTransportAsync()
        {
            int starts = 0;
            int resourceCalls = 0;
            HostedFixture fixture = HostedFixture.Create(builder => builder
                .ConfigureResources(_ =>
                {
                    resourceCalls++;
                    throw new InvalidOperationException("resource-composition-failure");
                })
                .AddStartupTask((_, _, _) =>
                {
                    starts++;
                    return default;
                }));
            await using var cleanup = fixture.ConfigureAwait(false);

            Exception failure = await fixture.StartAndCaptureFailureAsync().ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure).StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(resourceCalls, Is.EqualTo(1));
            Assert.That(starts, Is.Zero);
            fixture.Transport.Listener.Verify(listener => listener.CloseAsync(
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [TestCase("properties")]
        [TestCase("resources")]
        [TestCase("aliases")]
        [TestCase("instance")]
        [TestCase("post-configure")]
        public void CustomStandardServerRejectsUnsupportedCompositionHooks(string hook)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<StandardServer>(_ => { });
            switch (hook)
            {
                case "properties":
                    builder.ConfigureServerProperties(_ => { });
                    break;
                case "resources":
                    builder.ConfigureResources(_ => { });
                    break;
                case "aliases":
                    builder.ConfigureAliasNames(options => options.MaterializeAliasNodes = true);
                    break;
                case "instance":
                    services.AddSingleton(new ServerProperties());
                    break;
                case "post-configure":
                    services.PostConfigure<ServerProperties>(properties => properties.ProductName = "Unsupported");
                    break;
            }
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<IOpcUaServerFactory>()
                    .CreateServer(NUnitTelemetryContext.Create(isServer: true), TimeProvider.System),
                Throws.InvalidOperationException.With.Message.Contains("custom OPC UA server")
                    .And.Message.Contains("ConfigureServerProperties")
                    .And.Message.Contains("ConfigureResources")
                    .And.Message.Contains("ConfigureAliasNames"));
        }

        [Test]
        public async Task RawStandardServerStillSupportsFactoriesIdentityAndStartupTasksAsync()
        {
            using var factory = new MarkerNodeManagerFactory("urn:composition:legacy-server", 91);
            var observations = new CompositionObservations();
            var fixture = new HostedFixture((services, root) =>
            {
                services.AddSingleton(observations);
                services.AddOptions<ServerProperties>();
                services.AddOptions<AliasNameServerOptions>();
                return services.AddOpcUa()
                    .AddServer<StandardServer>(options => ConfigureOptions(options, root))
                    .AddNodeManager(factory)
                    .AddIdentityAuthenticator(new AnonymousAuthenticator())
                    .AddStartupTask<CompositionStartupTask>();
            });
            await using var cleanup = fixture.ConfigureAwait(false);

            await fixture.StartAsync().ConfigureAwait(false);
            AuthenticationResult identity = await fixture.Server.CurrentInstance.IdentityRegistry.AuthenticateAsync(
                CreateAuthenticationContext(fixture.Context, new AnonymousIdentityTokenHandler()),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(fixture.Server, Is.TypeOf<StandardServer>());
            AssertMarker(fixture.Context, factory.NamespacesUris[0], 91);
            Assert.That(observations.StartupCount, Is.EqualTo(1));
            Assert.That(identity.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(identity.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task StandardAliasMaterializationIsOptInWhileQueriesRemainLiveAsync(bool materialize, bool topics)
        {
            NodeId categoryId = topics ? ObjectIds.Topics : ObjectIds.TagVariables;
            var descriptor = new AliasNameCategoryDescriptor(
                categoryId,
                QualifiedName.From(topics ? BrowseNames.Topics : BrowseNames.TagVariables),
                AliasNameCapabilities.All);
            using var store = new InMemoryAliasNameStore([descriptor]);
            using var registry = new AliasNameStoreRegistry();
            store.Seed(categoryId, "BuildName",
                VariableIds.Server_ServerStatus_BuildInfo_ProductName, null, ReferenceTypeIds.AliasFor);
            HostedFixture fixture = HostedFixture.Create(builder =>
            {
                builder.AddAliasNameStore(store);
                if (materialize)
                {
                    builder.ConfigureAliasNames(options => options.MaterializeAliasNodes = true);
                    registry.Register(store);
                    builder.AddAliasNameStoreRegistry(registry);
                }
            });
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            AliasNameCategoryState category = fixture.Context
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            Assert.That(category, Is.Not.Null);
            ArrayOf<AliasNameDataType> initial = await FindAliasesAsync(fixture.Context, category)
                .ConfigureAwait(false);
            Assert.That(initial.Count, Is.EqualTo(1));
            Assert.That(initial[0].AliasName.Name, Is.EqualTo("BuildName"));
            Assert.That(initial[0].ReferencedNodes[0],
                Is.EqualTo((ExpandedNodeId)VariableIds.Server_ServerStatus_BuildInfo_ProductName));

            NodeId aliasId = default;
            var references = new List<IReference>();
            category.GetReferences(fixture.Context.DefaultSystemContext, references);
            AliasNameState[] aliases = references
                .Where(reference => reference.ReferenceTypeId == ReferenceTypeIds.Organizes && !reference.IsInverse)
                .Select(reference => fixture.Context.FindPredefinedNode<AliasNameState>(
                    ExpandedNodeId.ToNodeId(reference.TargetId, fixture.Context.DefaultSystemContext.NamespaceUris)))
                .Where(node => node != null)
                .ToArray();
            Assert.That(aliases, Has.Length.EqualTo(materialize ? 1 : 0));
            if (materialize)
            {
                AliasNameState alias = aliases.Single();
                aliasId = alias.NodeId;
                Assert.That(alias.BrowseName.Name, Is.EqualTo("BuildName"));
                Assert.That(alias.BrowseName.NamespaceIndex, Is.Not.Zero);
                Assert.That(alias.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.AliasNameType));
                Assert.That(alias.ReferenceExists(ReferenceTypeIds.AliasFor, false,
                    VariableIds.Server_ServerStatus_BuildInfo_ProductName), Is.True);
                BaseVariableState target = fixture.Context.FindPredefinedNode<BaseVariableState>(
                    VariableIds.Server_ServerStatus_BuildInfo_ProductName);
                Assert.That(target.ReferenceExists(ReferenceTypeIds.AliasFor, true, aliasId), Is.True);
                Assert.That(category.FindAliasVerbose, Is.Not.Null);
                Assert.That(category.AddAliasesToCategory, Is.Not.Null);
                Assert.That(category.DeleteAliasesFromCategory, Is.Not.Null);
                Assert.That(category.LastChange.Value, Is.Zero);
            }
            else
            {
                Assert.That(category.FindAliasVerbose, Is.Null);
                Assert.That(category.AddAliasesToCategory, Is.Null);
                Assert.That(category.DeleteAliasesFromCategory, Is.Null);
                Assert.That(fixture.Services.GetRequiredService<IOptions<AliasNameServerOptions>>()
                    .Value.MaterializeAliasNodes, Is.False);
            }

            StatusCode[] changes = await store.AddAliasesAsync(categoryId,
                [new AliasAddRequest("BuildNumber", VariableIds.Server_ServerStatus_BuildInfo_BuildNumber,
                    null, ReferenceTypeIds.AliasFor)], CancellationToken.None).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> current = await FindAliasesAsync(fixture.Context, category)
                .ConfigureAwait(false);
            Assert.That(changes, Has.Length.EqualTo(1));
            Assert.That(changes[0].Code, Is.EqualTo((uint)StatusCodes.Good));
            Assert.That(current.ToArray().Select(alias => alias.AliasName.Name),
                Is.EquivalentTo(s_mutatedAliases));
            references.Clear();
            category.GetReferences(fixture.Context.DefaultSystemContext, references);
            Assert.That(references.Count(reference =>
                reference.ReferenceTypeId == ReferenceTypeIds.Organizes && !reference.IsInverse),
                Is.EqualTo(materialize ? 1 : 0),
                "Later mutations must not rebuild the startup browse snapshot.");
            if (materialize)
            {
                Assert.That(category.LastChange.Value, Is.EqualTo(1u));
                Assert.That(fixture.Context.FindPredefinedNode<AliasNameState>(aliasId), Is.SameAs(aliases[0]));
            }
        }

        [Test]
        public async Task DefaultHostedReverseConnectUsesInjectedSessionHookAndStopsItsTimerAsync()
        {
            var clock = new FakeTimeProvider();
            var enabled = new Uri("opc.tcp://localhost:4841/enabled");
            var disabled = new Uri("opc.tcp://localhost:4842/disabled");
            SessionManager sessionManager = null!;
            HostedFixture fixture = HostedFixture.Create(
                builder =>
                {
                    builder.Services.AddSingleton<TimeProvider>(clock);
                    builder.AddSessionManager((_, server, configuration) =>
                    {
                        sessionManager = new SessionManager(server, configuration, clock);
                        return sessionManager;
                    });
                },
                options =>
                {
                    options.ReverseConnect = new ServerReverseConnectOptions { ConnectIntervalMs = 2000 };
                    options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                    {
                        EndpointUrl = enabled.ToString(),
                        Timeout = 3210,
                        MaxSessionCount = 0
                    });
                    options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                    {
                        EndpointUrl = disabled.ToString(),
                        Enabled = false
                    });
                });
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(fixture.Server, Is.InstanceOf<ReverseConnectServer>());
            Assert.That(fixture.Server.CurrentInstance.SessionManager, Is.SameAs(sessionManager));
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                It.IsAny<Uri>(), It.IsAny<int>()), Times.Never);
            clock.Advance(TimeSpan.FromSeconds(2));
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(enabled, 3210), Times.Once);
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                disabled, It.IsAny<int>()), Times.Never);
            var reverseServer = (ReverseConnectServer)fixture.Server;
            Assert.That(reverseServer.GetReverseConnections()[enabled].LastState,
                Is.EqualTo(ReverseConnectState.Connecting));
            Assert.That(ReadBuildInfo(fixture.Context).ProductName, Is.EqualTo("CompositionServer"));

            await fixture.StopAsync().ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(10));
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                It.IsAny<Uri>(), It.IsAny<int>()), Times.Once);
            Assert.That(reverseServer.GetReverseConnections()[enabled].LastState,
                Is.EqualTo(ReverseConnectState.Connecting),
                "A leaked timer would attempt another connection against the stopped server.");
            fixture.Transport.Listener.Verify(listener => listener.CloseAsync(
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public async Task DefaultHostedServerWithoutReverseClientsNeverDialsAsync()
        {
            var clock = new FakeTimeProvider();
            HostedFixture fixture = HostedFixture.Create(
                builder => builder.Services.AddSingleton<TimeProvider>(clock));
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMinutes(1));

            Assert.That(fixture.Server, Is.InstanceOf<ReverseConnectServer>());
            Assert.That(((ReverseConnectServer)fixture.Server).GetReverseConnections(), Is.Empty);
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                It.IsAny<Uri>(), It.IsAny<int>()), Times.Never);
            Assert.That(fixture.Context.CurrentState, Is.EqualTo(ServerState.Running));
        }

        private static IOpcUaServerBuilder CreateBuilder()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return services.AddOpcUa().AddServer(_ => { });
        }

        private static TaskCompletionSource<T> NewSignal<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task AwaitBoundedAsync(Task task)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var expired = NewSignal<bool>();
            using CancellationTokenRegistration registration =
                timeout.Token.Register(() => expired.TrySetResult(true));
            if (await Task.WhenAny(task, expired.Task).ConfigureAwait(false) != task)
            {
                throw new TimeoutException("The hosted composition operation did not complete.");
            }
            await task.ConfigureAwait(false);
        }

        private static BuildInfo ReadBuildInfo(IServerContext context)
        {
            DataValue value = ReadValue(context, VariableIds.Server_ServerStatus_BuildInfo);
            Assert.That(value.WrappedValue.TryGetStructure(out BuildInfo info), Is.True);
            return info;
        }

        private static DataValue ReadValue(IServerContext context, NodeId nodeId)
        {
            BaseVariableState node = context.FindPredefinedNode<BaseVariableState>(nodeId)!;
            return ReadValue(context, node);
        }

        private static DataValue ReadValue(IServerContext context, BaseVariableState node)
        {
            Assert.That(node, Is.Not.Null);
            var value = new DataValue();
            ServiceResult result = node.ReadAttribute(context.DefaultSystemContext,
                Attributes.Value, NumericRange.Null, QualifiedName.Null, ref value);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(value.StatusCode.Code, Is.EqualTo((uint)StatusCodes.Good));
            return value;
        }

        private static void AssertMarker(IServerContext context, string namespaceUri, int marker)
        {
            int namespaceIndex = context.DefaultSystemContext.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThan(0));
            NodeId nodeId = new("Marker", checked((ushort)namespaceIndex));
            BaseVariableState node = context.FindNodeManagers<MarkerNodeManager>()
                .Select(manager => manager.FindPredefinedNode<BaseVariableState>(nodeId))
                .Concat(context.FindNodeManagers<LegacyMarkerNodeManager>()
                    .Select(manager => manager.FindPredefinedNode<BaseVariableState>(nodeId)))
                .Single(value => value != null);
            DataValue value = ReadValue(context, node);
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(marker));
        }

        private static AuthenticationContext CreateAuthenticationContext(
            IServerContext context,
            IUserIdentityTokenHandler handler)
        {
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy { TokenType = handler.TokenType },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                context.MessageContext);
        }

        private static async Task<ArrayOf<AliasNameDataType>> FindAliasesAsync(
            IServerContext context,
            AliasNameCategoryState category)
        {
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await category.FindAlias.CallAsync(
                context.DefaultSystemContext, category.NodeId,
                [new Variant("%"), new Variant(NodeId.Null)],
                argumentErrors, output, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(argumentErrors.All(ServiceResult.IsGood), Is.True);
            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output[0].TryGetStructure(out ArrayOf<AliasNameDataType> aliases), Is.True);
            return aliases;
        }

        private static void ConfigureOptions(OpcUaServerOptions options, string root)
        {
            options.ApplicationName = "CompositionServer";
            options.ApplicationUri = "urn:localhost:CompositionServer";
            options.ProductUri = "urn:composition:product";
            options.PkiRoot = root;
            options.IncludeSignAndEncryptPolicies = false;
            options.IncludeUnsecurePolicyNone = true;
            options.AutoAcceptUntrustedCertificates = true;
            options.EndpointUrls.Add("opc.tcp://localhost:0/CompositionServer");
            options.ConfigureBuilder = builder => builder.SetShutdownDelay(0);
        }

        private static string CreateConfigurationXml(string root)
        {
            string pki = System.Security.SecurityElement.Escape(root.Replace('\\', '/'))!;
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <ApplicationConfiguration
                  xmlns:ua="http://opcfoundation.org/UA/2008/02/Types.xsd"
                  xmlns="http://opcfoundation.org/UA/SDK/Configuration.xsd">
                  <ApplicationName>XmlComposition</ApplicationName>
                  <ApplicationUri>urn:localhost:XmlComposition</ApplicationUri>
                  <ProductUri>urn:composition:xml-product</ProductUri>
                  <ApplicationType>Server_0</ApplicationType>
                  <SecurityConfiguration>
                    <ApplicationCertificates>
                      <CertificateIdentifier>
                        <StoreType>Directory</StoreType>
                        <StorePath>{pki}/own</StorePath>
                        <SubjectName>CN=XmlComposition, O=OPC Foundation, DC=localhost</SubjectName>
                        <CertificateTypeString>RsaSha256</CertificateTypeString>
                      </CertificateIdentifier>
                    </ApplicationCertificates>
                    <TrustedIssuerCertificates>
                      <StoreType>Directory</StoreType><StorePath>{pki}/issuer</StorePath>
                    </TrustedIssuerCertificates>
                    <TrustedPeerCertificates>
                      <StoreType>Directory</StoreType><StorePath>{pki}/trusted</StorePath>
                    </TrustedPeerCertificates>
                    <RejectedCertificateStore>
                      <StoreType>Directory</StoreType><StorePath>{pki}/rejected</StorePath>
                    </RejectedCertificateStore>
                    <AutoAcceptUntrustedCertificates>true</AutoAcceptUntrustedCertificates>
                  </SecurityConfiguration>
                  <TransportConfigurations></TransportConfigurations>
                  <TransportQuotas>
                    <OperationTimeout>90000</OperationTimeout>
                    <MaxStringLength>654321</MaxStringLength>
                    <MaxByteStringLength>1048576</MaxByteStringLength>
                    <MaxArrayLength>65535</MaxArrayLength>
                    <MaxMessageSize>4194304</MaxMessageSize>
                    <MaxBufferSize>65535</MaxBufferSize>
                    <ChannelLifetime>300000</ChannelLifetime>
                    <SecurityTokenLifetime>3600000</SecurityTokenLifetime>
                  </TransportQuotas>
                  <ServerConfiguration>
                    <BaseAddresses><ua:String>opc.tcp://localhost:0/XmlComposition</ua:String></BaseAddresses>
                    <SecurityPolicies>
                      <ServerSecurityPolicy>
                        <SecurityMode>None_1</SecurityMode>
                        <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</SecurityPolicyUri>
                      </ServerSecurityPolicy>
                    </SecurityPolicies>
                    <UserTokenPolicies>
                      <ua:UserTokenPolicy><ua:TokenType>Anonymous_0</ua:TokenType></ua:UserTokenPolicy>
                    </UserTokenPolicies>
                    <DiagnosticsEnabled>true</DiagnosticsEnabled>
                    <MaxSessionCount>77</MaxSessionCount>
                    <MinSessionTimeout>10000</MinSessionTimeout>
                    <MaxSessionTimeout>3600000</MaxSessionTimeout>
                    <ShutdownDelay>0</ShutdownDelay>
                  </ServerConfiguration>
                </ApplicationConfiguration>
                """;
        }

        public sealed class CompositionObservations
        {
            public ConcurrentQueue<string> Events { get; } = new();
            public IServerContext Context { get; set; } = null!;
            public NodeId HistoryNodeId { get; set; }
            public int StartupCount { get; set; }
            public int FactoryConstructions { get; set; }
            public bool ConfigurationLoaded { get; set; }
            public bool FactorySawLoadedConfiguration { get; set; }
            public DelayedNodeManagerFactory Factory { get; set; } = null!;
        }

        public sealed class CompositionStartupTask(CompositionObservations observations) : IServerStartupTask
        {
            public async ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                observations.StartupCount++;
                observations.Context = server;
                observations.Events.Enqueue("generic");
                IDiagnosticsNodeManager diagnostics = server.FindNodeManagers<IDiagnosticsNodeManager>().Single();
                HistoryServerCapabilitiesState history = await diagnostics
                    .GetDefaultHistoryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                history.MaxReturnDataValues.Value = 73;
                history.MaxReturnEventValues.Value = 29;
                observations.HistoryNodeId = history.NodeId;
            }
        }

        private sealed class ExistingStartupTask(CompositionObservations observations) : IServerStartupTask
        {
            public ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                Assert.That(server.CurrentState, Is.EqualTo(ServerState.Running));
                observations.Events.Enqueue("existing");
                return default;
            }
        }

        public sealed class DelayedNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public const string NamespaceUri = "urn:composition:delayed";

            public DelayedNodeManagerFactory(CompositionObservations observations)
            {
                observations.FactoryConstructions++;
                observations.Factory = this;
                observations.FactorySawLoadedConfiguration = observations.ConfigurationLoaded;
            }

            public ArrayOf<string> NamespacesUris => [NamespaceUri];
            public MarkerNodeManager Manager { get; private set; } = null!;

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Manager = new MarkerNodeManager(server, configuration, NamespaceUri, 51);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }
        }

        private sealed class MarkerNodeManagerFactory(string namespaceUri, int marker) :
            IAsyncNodeManagerFactory, IDisposable
        {
            public ArrayOf<string> NamespacesUris => [namespaceUri];
            public int CreateCount { get; private set; }
            public int DisposeCount { get; private set; }
            public ApplicationConfiguration Configuration { get; private set; } = null!;
            public MarkerNodeManager Manager { get; private set; } = null!;

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CreateCount++;
                Configuration = configuration;
                Manager = new MarkerNodeManager(server, configuration, namespaceUri, marker);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        public sealed class MarkerNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string namespaceUri,
            int marker) : AsyncCustomNodeManager(server, configuration, NullLogger.Instance, namespaceUri)
        {
            public bool Disposed { get; private set; }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var variable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Marker", NamespaceIndex),
                    BrowseName = new QualifiedName("Marker", NamespaceIndex),
                    DisplayName = LocalizedText.From("Marker"),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = marker
                };
                variable.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference> references))
                {
                    references = new List<IReference>();
                    externalReferences[ObjectIds.ObjectsFolder] = references;
                }
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, variable.NodeId));
                await AddPredefinedNodeAsync(SystemContext, variable, cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Disposed = true;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class LegacyMarkerNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration) :
            CustomNodeManager2(server, configuration, NullLogger.Instance, "urn:composition:sync-instance")
        {
            public bool Disposed { get; private set; }

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                var variable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Marker", NamespaceIndex),
                    BrowseName = new QualifiedName("Marker", NamespaceIndex),
                    DisplayName = LocalizedText.From("Legacy Marker"),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = 22
                };
                AddPredefinedNode(SystemContext, variable);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Disposed = true;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class ResourceHookServer(IServiceProvider services) : DependencyInjectionStandardServer(
            services, NUnitTelemetryContext.Create(isServer: true), TimeProvider.System)
        {
            public ResourceManager CreateResources(ApplicationConfiguration configuration)
            {
                return CreateResourceManager(Mock.Of<IServerInternal>(), configuration);
            }
        }

        private sealed class HostedFixture : IAsyncDisposable
        {
            public HostedFixture(Func<IServiceCollection, string, IOpcUaServerBuilder> configure)
            {
                m_root = Path.Combine(Path.GetTempPath(), "uac", Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(m_root);
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddProvider(Logs));
                services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create(isServer: true));
                IOpcUaServerBuilder builder = configure(services, m_root);
                builder.AddStartupTask((_, context, _) =>
                {
                    Context = context;
                    m_ready.TrySetResult(true);
                    return default;
                });
                var bindings = new DefaultTransportBindingRegistry();
                bindings.RegisterListenerFactory(Transport);
                services.AddSingleton<ITransportBindingRegistry>(bindings);
                m_provider = services.BuildServiceProvider();
                _ = HostedService;
            }

            public static HostedFixture Create(
                Action<IOpcUaServerBuilder> compose = null!,
                Action<OpcUaServerOptions> configure = null!)
            {
                return new HostedFixture((services, root) =>
                {
                    IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
                    {
                        ConfigureOptions(options, root);
                        configure?.Invoke(options);
                    });
                    compose?.Invoke(builder);
                    return builder;
                });
            }

            public IServerContext Context { get; private set; } = null!;
            public RecordingTransportFactory Transport { get; } = new();
            public CapturingLoggerProvider Logs { get; } = new();
            public IServiceProvider Services => m_provider;
            public StandardServer Server => Transport.Server;
            public ApplicationConfiguration Configuration => Transport.Configuration;
            public Task ExecuteTask => HostedService.ExecuteTask!;
            private BackgroundService HostedService => m_provider.GetServices<IHostedService>()
                .OfType<BackgroundService>().Single();

            public async Task StartAsync()
            {
                await HostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Task<Task> waiting = Task.WhenAny(m_ready.Task, ExecuteTask);
                await AwaitBoundedAsync(waiting).ConfigureAwait(false);
                Task completion = await waiting.ConfigureAwait(false);
                await completion.ConfigureAwait(false);
                if (completion != m_ready.Task)
                {
                    throw new InvalidOperationException("The hosted service stopped before completing startup.");
                }
            }

            public async Task<Exception> StartAndCaptureFailureAsync()
            {
                try
                {
                    await HostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    await AwaitBoundedAsync(ExecuteTask).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    return exception;
                }
                throw new InvalidOperationException("Expected hosted startup to fail.");
            }

            public async Task StopAsync()
            {
                if (m_stopped)
                {
                    return;
                }
                m_stopped = true;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await HostedService.StopAsync(timeout.Token).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await m_provider.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Directory.Exists(m_root))
                        {
                            Directory.Delete(m_root, recursive: true);
                        }
                    }
                }
            }

            private readonly string m_root;
            private readonly ServiceProvider m_provider;
            private readonly TaskCompletionSource<bool> m_ready = NewSignal<bool>();
            private bool m_stopped;
        }

        private sealed class RecordingTransportFactory : TcpServiceHost, ITransportListenerFactory
        {
            public RecordingTransportFactory()
            {
                Listener.SetupGet(listener => listener.ListenerId).Returns("composition");
                Listener.SetupGet(listener => listener.UriScheme).Returns(Utils.UriSchemeOpcTcp);
            }

            public Mock<ITransportListener> Listener { get; } = new();
            public StandardServer Server { get; private set; } = null!;
            public ApplicationConfiguration Configuration { get; private set; } = null!;
            public override string UriScheme => Utils.UriSchemeOpcTcp;

            public override ITransportListener Create(ITelemetryContext telemetry)
            {
                return Listener.Object;
            }

            ValueTask<List<EndpointDescription>> ITransportListenerFactory.CreateServiceHostAsync(
                ServerBase serverBase,
                IDictionary<string, ServiceHost> hosts,
                ApplicationConfiguration configuration,
                ArrayOf<string> baseAddresses,
                ApplicationDescription serverDescription,
                ArrayOf<ServerSecurityPolicy> securityPolicies,
                ICertificateRegistry serverCertificates,
                ICertificateValidatorEx clientCertificateValidator,
                CancellationToken ct)
            {
                Server = (StandardServer)serverBase;
                Configuration = configuration;
                return CreateServiceHostAsync(serverBase, hosts, configuration, baseAddresses,
                    serverDescription, securityPolicies, serverCertificates, clientCertificateValidator, ct);
            }
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public ConcurrentQueue<Exception> Errors { get; } = new();

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Errors);
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingLogger(ConcurrentQueue<Exception> errors) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (logLevel == LogLevel.Error && exception != null)
                {
                    errors.Enqueue(exception);
                }
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IdentityFactoryReceivesTheSuppliedNullableValidator(bool supplyValidator)
        {
            ICertificateValidatorEx validator = supplyValidator ? Mock.Of<ICertificateValidatorEx>() : null!;
            var observations = new CompositionObservations();
            var authenticator = new AnonymousAuthenticator();
            int calls = 0;
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.Services.AddSingleton(observations);
            builder.AddIdentityAuthenticator((services, certificateValidator) =>
            {
                Assert.That(services.GetRequiredService<CompositionObservations>(), Is.SameAs(observations));
                Assert.That(certificateValidator, Is.SameAs(validator));
                calls++;
                return authenticator;
            });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            OpcUaServerIdentityAuthenticatorRegistration registration = provider
                .GetServices<OpcUaServerIdentityAuthenticatorRegistration>().Last();
            Assert.That(calls, Is.Zero);

            IUserTokenAuthenticator[] authenticators =
                registration.CreateAuthenticators(provider, validator).ToArray();

            Assert.That(authenticators, Has.Length.EqualTo(1));
            Assert.That(authenticators[0], Is.SameAs(authenticator));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ThrowingFactoryRunsAfterCertificateSetupAndPreservesItsFailureAsync()
        {
            var expected = new InvalidOperationException("configuration-factory-failure");
            int calls = 0;
            int laterFactoryCalls = 0;
            int startupCalls = 0;
            HostedFixture fixture = HostedFixture.Create(builder => builder
                .AddNodeManagers((_, configuration) =>
                {
                    using CertificateEntryCollection certificates = configuration.CertificateManager
                        .SnapshotApplicationCertificates();
                    Assert.That(certificates, Has.Count.GreaterThan(0));
                    Assert.That(configuration.ApplicationName, Is.EqualTo("CompositionServer"));
                    calls++;
                    throw expected;
                })
                .AddNodeManagers((_, _) =>
                {
                    laterFactoryCalls++;
                    return [];
                })
                .AddStartupTask((_, _, _) =>
                {
                    startupCalls++;
                    return default;
                }));
            await using var cleanup = fixture.ConfigureAwait(false);

            Exception actual = await fixture.StartAndCaptureFailureAsync().ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(fixture.ExecuteTask.IsFaulted, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(laterFactoryCalls, Is.Zero);
            Assert.That(startupCalls, Is.Zero);
            Assert.That(fixture.Transport.Server, Is.Null);
        }

        [Test]
        public async Task StartupFailureStopsTheReverseTimerBeforeExecuteTaskFaultsAsync()
        {
            var expected = new InvalidOperationException("reverse-startup-failure");
            var clock = new FakeTimeProvider();
            var client = new Uri("opc.tcp://localhost:0/startup-failure");
            HostedFixture fixture = HostedFixture.Create(
                builder =>
                {
                    builder.Services.AddSingleton<TimeProvider>(clock);
                    builder.AddStartupTask((_, context, _) =>
                    {
                        Assert.That(context.CurrentState, Is.EqualTo(ServerState.Running));
                        throw expected;
                    });
                },
                options =>
                {
                    options.ReverseConnect = new ServerReverseConnectOptions { ConnectIntervalMs = 2000 };
                    options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                    {
                        EndpointUrl = client.ToString(),
                        Timeout = 1456,
                        MaxSessionCount = 0
                    });
                });
            await using var cleanup = fixture.ConfigureAwait(false);

            Exception actual = await fixture.StartAndCaptureFailureAsync().ConfigureAwait(false);
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(fixture.ExecuteTask.IsFaulted, Is.True);
            Assert.That(fixture.Server, Is.InstanceOf<ReverseConnectServer>());
            var server = (ReverseConnectServer)fixture.Server;
            Assert.That(server.GetReverseConnections()[client].LastState, Is.EqualTo(ReverseConnectState.Closed));

            // Do not call StopAsync first: ExecuteTask must already have completed its cleanup.
            clock.Advance(TimeSpan.FromSeconds(10));

            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                It.IsAny<Uri>(), It.IsAny<int>()), Times.Never);
            fixture.Transport.Listener.Verify(listener => listener.CloseAsync(
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            Assert.That(server.GetReverseConnections()[client].LastState, Is.EqualTo(ReverseConnectState.Closed));
            Assert.That(fixture.Logs.Errors, Does.Contain(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LoadedReverseConnectionsUseDocumentAndLoadedOverridesRatherThanOptionsAsync(bool useStream)
        {
            var clock = new FakeTimeProvider();
            var enabled = new Uri("opc.tcp://localhost:0/xml-enabled");
            var disabled = new Uri("opc.tcp://localhost:0/xml-disabled");
            var optionsOnly = new Uri("opc.tcp://localhost:0/options-only");
            int overrides = 0;
            var fixture = new HostedFixture((services, root) =>
            {
                services.AddSingleton<TimeProvider>(clock);
                IOpcUaBuilder builder = services.AddOpcUa();
                string configurationXml = CreateReverseConfigurationXml(root);
                IOpcUaServerBuilder server;
                if (useStream)
                {
                    server = builder.AddServer(
                        new MemoryStream(Encoding.UTF8.GetBytes(configurationXml)),
                        OverrideReverseConnection);
                }
                else
                {
                    string configurationFile = Path.Combine(root, "reverse.xml");
                    File.WriteAllText(configurationFile, configurationXml);
                    server = builder.AddServer(configurationFile, OverrideReverseConnection);
                }
                services.Configure<OpcUaServerOptions>(options =>
                {
                    options.ReverseConnect = new ServerReverseConnectOptions { ConnectIntervalMs = 500 };
                    options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                    {
                        EndpointUrl = optionsOnly.ToString()
                    });
                });
                return server;
            });
            await using var cleanup = fixture.ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(overrides, Is.EqualTo(1));
            Assert.That(fixture.Server, Is.InstanceOf<ReverseConnectServer>());
            var reverse = (ReverseConnectServer)fixture.Server;
            Assert.That(reverse.GetReverseConnections().Keys, Is.EquivalentTo(new[] { enabled, disabled }));
            Assert.That(reverse.GetReverseConnections()[enabled].ConfigEntry, Is.True);
            Assert.That(reverse.GetReverseConnections()[enabled].Timeout, Is.EqualTo(2345));
            Assert.That(reverse.GetReverseConnections()[disabled].Enabled, Is.False);
            Assert.That(fixture.Configuration.ServerConfiguration.ReverseConnect.ConnectTimeout, Is.EqualTo(6000));
            Assert.That(ReadBuildInfo(fixture.Context).ProductName, Is.EqualTo("XmlComposition"));

            clock.Advance(TimeSpan.FromMilliseconds(500));
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                It.IsAny<Uri>(), It.IsAny<int>()), Times.Never);
            clock.Advance(TimeSpan.FromMilliseconds(1000));
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(enabled, 2345), Times.Once);
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                disabled, It.IsAny<int>()), Times.Never);
            fixture.Transport.Listener.Verify(listener => listener.CreateReverseConnection(
                optionsOnly, It.IsAny<int>()), Times.Never);

            void OverrideReverseConnection(ApplicationConfiguration configuration)
            {
                overrides++;
                ReverseConnectServerConfiguration loaded = configuration.ServerConfiguration.ReverseConnect;
                Assert.That(loaded.Clients.Count, Is.EqualTo(2));
                Assert.That(loaded.ConnectInterval, Is.EqualTo(3000));
                Assert.That(loaded.Clients[0].Timeout, Is.EqualTo(1234));
                loaded.ConnectInterval = 1500;
                loaded.Clients[0].Timeout = 2345;
            }
        }

        private static string CreateReverseConfigurationXml(string root)
        {
            const string reverseXml = """
                <ReverseConnect>
                  <Clients>
                    <ReverseConnectClient>
                      <EndpointUrl>opc.tcp://localhost:0/xml-enabled</EndpointUrl>
                      <Timeout>1234</Timeout>
                      <MaxSessionCount>0</MaxSessionCount>
                      <Enabled>true</Enabled>
                    </ReverseConnectClient>
                    <ReverseConnectClient>
                      <EndpointUrl>opc.tcp://localhost:0/xml-disabled</EndpointUrl>
                      <Timeout>4321</Timeout>
                      <MaxSessionCount>0</MaxSessionCount>
                      <Enabled>false</Enabled>
                    </ReverseConnectClient>
                  </Clients>
                  <ConnectInterval>3000</ConnectInterval>
                  <ConnectTimeout>6000</ConnectTimeout>
                  <RejectTimeout>9000</RejectTimeout>
                </ReverseConnect>
                """;
            string xml = CreateConfigurationXml(root);
            int closingTag = xml.IndexOf("</ServerConfiguration>", StringComparison.Ordinal);
            return xml.Insert(closingTag, reverseXml);
        }

        private static readonly string[] s_completedEvents = ["completed"];
        private static readonly string[] s_enteredEvents = ["existing", "generic", "delegate-entered"];
        private static readonly string[] s_startupEvents =
            ["existing", "generic", "delegate-entered", "delegate-completed", "last"];
        private static readonly string[] s_resourceEvents = ["first", "second"];
        private static readonly string[] s_mutatedAliases = ["BuildName", "BuildNumber"];
        private static readonly UserTokenType[] s_anonymousTokens = [UserTokenType.Anonymous];
    }
}
