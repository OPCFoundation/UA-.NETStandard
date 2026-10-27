/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("DI")]
    [Category("Hosting")]
    public sealed class DiProviderCompositionTests
    {
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

        public abstract class RecordingProvider : IDiModelProvider
        {
            protected RecordingProvider(params string[] namespaceUris)
            {
                NamespaceUris = namespaceUris;
            }

            public abstract int Order { get; }

            public ArrayOf<string> NamespaceUris { get; }

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

        public sealed class Provider10 : RecordingProvider
        {
            public Provider10()
                : base("urn:di-tests:provider-10", "urn:di-tests:shared")
            {
            }

            public override int Order => 10;
        }

        public sealed class Provider20 : RecordingProvider
        {
            public Provider20()
                : base("urn:di-tests:shared", "urn:di-tests:provider-20")
            {
            }

            public override int Order => 20;
        }

        public sealed class DiNamespaceProvider : RecordingProvider
        {
            public DiNamespaceProvider()
                : base(DiNodeManager.DiNamespaceUri)
            {
            }

            public override int Order => 30;
        }
    }
}
