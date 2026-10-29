/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("DI")]
    [Category("Hosting")]
    public sealed class DiProviderCompositionTests
    {
        [Test]
        public void NormalizationDeduplicatesIdentityButKeepsDistinctEqualProviders()
        {
            var first = new EqualProvider();
            var distinct = new EqualProvider();
            ArrayOf<IDiModelProvider> normalized = DiModelProviderUtilities.Normalize([first, first, distinct]);
            var nodes = new NodeStateCollection();
            for (int index = 0; index < normalized.Count; index++)
            {
                normalized[index].AddPredefinedNodes(nodes, context: null!);
            }
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(distinct));
                Assert.That(normalized.Count, Is.EqualTo(2));
                Assert.That(normalized[0], Is.SameAs(first));
                Assert.That(normalized[1], Is.SameAs(distinct));
                Assert.That(first.Calls, Is.EqualTo(1));
                Assert.That(distinct.Calls, Is.EqualTo(1));
            });
        }

        [TestCase("DI")]
        [TestCase("Robotics")]
        public void EqualOrderProvidersRetainTypeNameOrderingAcrossDiOwners(string owner)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "EqualOrderComposition");
            builder.AddDiModel<ZuluProvider>().AddDiModel<AlphaProvider>();
            switch (owner)
            {
                case "DI":
                    builder.AddOpcUaDi();
                    break;
                case "Robotics":
                    builder.AddRobotics();
                    break;
            }
            using ServiceProvider provider = services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = owner switch
            {
                "DI" => provider.GetRequiredService<DiNodeManagerFactory>(),
                _ => provider.GetRequiredService<RoboticsNodeManagerFactory>()
            };
            string[] neutralNamespaces = factory.NamespacesUris.ToArray()!
                .Where(uri => uri.StartsWith("urn:equal-order:", StringComparison.Ordinal)).ToArray();
            Assert.That(neutralNamespaces, Is.EqualTo(new List<string>
            {
                "urn:equal-order:alpha", "urn:equal-order:zulu"
            }));
        }

        [Test]
        public async Task GenericCompositionDeduplicatesModelsAndAwaitsCallbacksInRegistrationOrderAsync()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "GenericDiComposition");
            var trace = new List<string>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IDiPostSetupContext context = Mock.Of<IDiPostSetupContext>();
            builder.ConfigureDevicesFor<DiNodeManager>(_ => trace.Add("owner"));
            Assert.That(builder.AddDiModel<Provider10, DiNodeManager>(async received =>
            {
                Assert.That(received, Is.SameAs(context));
                trace.Add("start");
                await gate.Task.ConfigureAwait(false);
                trace.Add("complete");
            }), Is.SameAs(builder));
            builder.AddDiModel<Provider10, DiNodeManager>(received =>
            {
                Assert.That(received, Is.SameAs(context));
                trace.Add("second");
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            IDiPostSetupConfigurator[] callbacks = provider.GetServices<IDiPostSetupConfigurator>().ToArray();
            Assert.That(provider.GetServices<IDiModelProvider>().Single(), Is.TypeOf<Provider10>());
            Assert.That(callbacks.Select(callback => callback.TargetManagerType),
                Is.EqualTo(new List<Type> { typeof(DiNodeManager), typeof(DiNodeManager), typeof(DiNodeManager) }));
            await callbacks[0].RunAsync(context).ConfigureAwait(false);
            Task pending = callbacks[1].RunAsync(context).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(trace, Is.EqualTo(new List<string> { "owner", "start" }));
            gate.SetResult(true);
            await pending.ConfigureAwait(false);
            await callbacks[2].RunAsync(context).ConfigureAwait(false);
            Assert.That(trace, Is.EqualTo(new List<string> { "owner", "start", "complete", "second" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GenericCompositionRejectsNullArgumentsWithoutRegisteringAnything(bool asynchronous)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "GenericDiValidation");
            int count = services.Count;
            if (asynchronous)
            {
                Assert.Throws<ArgumentNullException>(() =>
                    OpcUaServerDiBuilderExtensions.AddDiModel<Provider10, DiNodeManager>(
                        null!, (Func<IDiPostSetupContext, ValueTask>)(_ => default)));
                Assert.Throws<ArgumentNullException>(() => builder.AddDiModel<Provider10, DiNodeManager>(
                    (Func<IDiPostSetupContext, ValueTask>)null!));
            }
            else
            {
                Assert.Throws<ArgumentNullException>(() =>
                    OpcUaServerDiBuilderExtensions.AddDiModel<Provider10, DiNodeManager>(
                        null!, (Action<IDiPostSetupContext>)(_ => { })));
                Assert.Throws<ArgumentNullException>(() => builder.AddDiModel<Provider10, DiNodeManager>(
                    (Action<IDiPostSetupContext>)null!));
            }
            Assert.That(services, Has.Count.EqualTo(count));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GenericCompositionPreservesCallbackFailuresAsync(bool asynchronous)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "GenericDiFailure");
            var failure = new InvalidOperationException("injected");
            if (asynchronous)
            {
                builder.AddDiModel<Provider10, DiNodeManager>(
                    (Func<IDiPostSetupContext, ValueTask>)(_ => new ValueTask(Task.FromException(failure))));
            }
            else
            {
                builder.AddDiModel<Provider10, DiNodeManager>((Action<IDiPostSetupContext>)(_ => throw failure));
            }
            using ServiceProvider provider = services.BuildServiceProvider();
            IDiPostSetupConfigurator callback = provider.GetServices<IDiPostSetupConfigurator>().Single();
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await callback.RunAsync(Mock.Of<IDiPostSetupContext>()).ConfigureAwait(false));
            Assert.That(observed, Is.SameAs(failure));
        }

        [Test]
        public void PlainProvidersExecuteInOrderAndExposeDeduplicatedNamespaces()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "DiProviderOrder");
            builder.AddDiModel<Provider20>()
                .AddDiModel<Provider10>()
                .AddDiModel<Provider20>()
                .AddOpcUaDi();

            using ServiceProvider provider = services.BuildServiceProvider();
            DiNodeManagerFactory factory = provider.GetRequiredService<DiNodeManagerFactory>();
            IDiModelProvider[] providers = provider.GetServices<IDiModelProvider>().ToArray();
            var nodes = new NodeStateCollection();

            foreach (IDiModelProvider modelProvider in DiModelProviderUtilities.Normalize(providers))
            {
                modelProvider.AddPredefinedNodes(nodes, context: null!);
            }

            Assert.Multiple(() =>
            {
                Assert.That(factory.NamespacesUris.ToArray(), Is.EqualTo(new List<string>
                {
                    "urn:di-tests:provider-10",
                    "urn:di-tests:shared",
                    "urn:di-tests:provider-20",
                    DiNodeManager.DiNamespaceUri
                }));
                Assert.That(providers, Has.Length.EqualTo(2),
                    "TryAddEnumerable suppresses repeated registration of the same provider type.");
                Assert.That(providers.Select(model => model.Order), Is.EqualTo(new List<int> { 20, 10 }),
                    "service resolution retains registration order; callback order is asserted below");
                Assert.That(nodes.Select(node => node.BrowseName.Name),
                    Is.EqualTo(new List<string> { nameof(Provider10), nameof(Provider20) }),
                    "the normalized order is the actual provider callback order");
            });
        }

        [TestCase(0)]
        [TestCase(1)]
        public void EmptyAndSingletonRegistrationsExposeExactlyOneDiNamespace(int providerCount)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "DiProviderCount");
            if (providerCount == 1)
            {
                builder.AddDiModel<Provider10>();
            }
            builder.AddOpcUaDi();

            using ServiceProvider provider = services.BuildServiceProvider();
            string[] expected = providerCount == 0
                ? [DiNodeManager.DiNamespaceUri]
                : ["urn:di-tests:provider-10", "urn:di-tests:shared", DiNodeManager.DiNamespaceUri];

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<DiNodeManagerFactory>().NamespacesUris.ToArray(),
                    Is.EqualTo(expected));
                Assert.That(expected.Count(uri => uri == DiNodeManager.DiNamespaceUri), Is.EqualTo(1));
            });
        }

        [Test]
        public void RepeatedRegistrationKeepsOneProviderAndDeduplicatesDiNamespaceContribution()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "DiProviderDedup");
            builder.AddDiModel<DiNamespaceProvider>()
                .AddDiModel<DiNamespaceProvider>()
                .AddOpcUaDi();

            using ServiceProvider provider = services.BuildServiceProvider();
            IDiModelProvider[] resolved = provider.GetServices<IDiModelProvider>().ToArray();
            string[] namespaces = provider.GetRequiredService<DiNodeManagerFactory>()
                .NamespacesUris.ToArray() ?? [];

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Has.Length.EqualTo(1));
                Assert.That(resolved[0], Is.TypeOf<DiNamespaceProvider>());
                Assert.That(namespaces, Is.EqualTo(new List<string> { DiNodeManager.DiNamespaceUri }));
                Assert.That(namespaces.Count(uri => uri == DiNodeManager.DiNamespaceUri), Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Tests identity-based deduplication independently of value equality.
        /// </summary>
        public sealed class EqualProvider : IDiModelProvider
        {
            /// <summary>
            /// Gets the loading priority.
            /// </summary>
            public int Order => 0;

            /// <summary>
            /// Gets the contributed namespaces.
            /// </summary>
            public ArrayOf<string> NamespaceUris => ["urn:equal-provider"];

            /// <summary>
            /// Gets the number of loading callbacks.
            /// </summary>
            public int Calls { get; private set; }

            /// <summary>
            /// Records a loading callback.
            /// </summary>
            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context) => Calls++;

            /// <summary>
            /// Makes distinct instances value-equal.
            /// </summary>
            public override bool Equals(object? obj) => obj is EqualProvider;

            /// <summary>
            /// Gets the shared equality hash.
            /// </summary>
            public override int GetHashCode() => 1;
        }

        /// <summary>
        /// Contributes a node recording the provider's loading position.
        /// </summary>
        public abstract class RecordingProvider : IDiModelProvider
        {
            /// <summary>
            /// Creates a provider advertising the specified namespaces.
            /// </summary>
            protected RecordingProvider(params string[] namespaceUris)
            {
                NamespaceUris = namespaceUris;
            }

            /// <summary>
            /// Gets the model loading priority.
            /// </summary>
            public abstract int Order { get; }

            /// <summary>
            /// Gets the contributed namespaces.
            /// </summary>
            public ArrayOf<string> NamespaceUris { get; }

            /// <summary>
            /// Appends a node identifying this loading callback.
            /// </summary>
            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
                nodes.Add(new BaseObjectState(null)
                {
                    NodeId = new NodeId(GetType().Name, 1),
                    BrowseName = new QualifiedName(GetType().Name),
                    DisplayName = new LocalizedText(GetType().Name)
                });
            }
        }

        /// <summary>
        /// Supplies the alphabetically first equal-priority provider.
        /// </summary>
        public sealed class AlphaProvider : RecordingProvider
        {
            /// <summary>
            /// Creates the alpha namespace contribution.
            /// </summary>
            public AlphaProvider() : base("urn:equal-order:alpha")
            {
            }

            /// <summary>
            /// Gets the shared loading priority.
            /// </summary>
            public override int Order => 0;
        }

        /// <summary>
        /// Supplies the alphabetically last equal-priority provider.
        /// </summary>
        public sealed class ZuluProvider : RecordingProvider
        {
            /// <summary>
            /// Creates the zulu namespace contribution.
            /// </summary>
            public ZuluProvider() : base("urn:equal-order:zulu")
            {
            }

            /// <summary>
            /// Gets the shared loading priority.
            /// </summary>
            public override int Order => 0;
        }

        /// <summary>
        /// Supplies the first ordered namespace contribution.
        /// </summary>
        public sealed class Provider10 : RecordingProvider
        {
            /// <summary>
            /// Creates the priority-ten namespace contribution.
            /// </summary>
            public Provider10()
                : base("urn:di-tests:provider-10", "urn:di-tests:shared")
            {
            }

            /// <summary>
            /// Gets the loading priority.
            /// </summary>
            public override int Order => 10;
        }

        /// <summary>
        /// Supplies the second ordered namespace contribution.
        /// </summary>
        public sealed class Provider20 : RecordingProvider
        {
            /// <summary>
            /// Creates the priority-twenty namespace contribution.
            /// </summary>
            public Provider20()
                : base("urn:di-tests:shared", "urn:di-tests:provider-20")
            {
            }

            /// <summary>
            /// Gets the loading priority.
            /// </summary>
            public override int Order => 20;
        }

        /// <summary>
        /// Repeats the owner's built-in DI namespace.
        /// </summary>
        public sealed class DiNamespaceProvider : RecordingProvider
        {
            /// <summary>
            /// Creates the duplicate DI namespace contribution.
            /// </summary>
            public DiNamespaceProvider()
                : base(DiNodeManager.DiNamespaceUri)
            {
            }

            /// <summary>
            /// Gets the loading priority.
            /// </summary>
            public override int Order => 30;
        }
    }
}
