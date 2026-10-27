/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Machinery.Tests
{
    [TestFixture]
    [Category("Machinery")]
    [Category("Hosting")]
    public sealed class MachineryProviderCompositionTests
    {
        [Test]
        public void RepeatedNeutralInstanceIsAdaptedAndInvokedOnlyOnce()
        {
            var services = new ServiceCollection();
            var plain = new PlainProvider10();
            services.AddSingleton<IDiModelProvider>(plain);
            services.AddSingleton<IDiModelProvider>(plain);
            using ServiceProvider provider = services.BuildServiceProvider();
            int adaptations = 0;
            ArrayOf<IMachineryModelProvider> merged = provider.GetDiModelProviders<IMachineryModelProvider>(
                model =>
                {
                    adaptations++;
                    Assert.That(model, Is.SameAs(plain));
                    return new DiModelProviderAdapter(model);
                });
            var nodes = new NodeStateCollection();
            foreach (IMachineryModelProvider model in merged)
            {
                model.AddPredefinedNodes(nodes, context: null!);
            }

            Assert.Multiple(() =>
            {
                Assert.That(adaptations, Is.EqualTo(1));
                Assert.That(merged.Count, Is.EqualTo(1));
                Assert.That(plain.CallCount, Is.EqualTo(1));
                Assert.That(nodes.Select(node => node.BrowseName.Name),
                    Is.EqualTo(new List<string> { "plain10" }));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MachineryOwnerMergesFamilyAndPlainProvidersOnceBuiltInFirst(bool registerPlainFirst)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "MachineryProviderMerge");

            if (registerPlainFirst)
            {
                builder.AddDiModel<PlainProvider10>()
                    .AddDiModel<PlainProvider10>()
                    .AddDiModel<PlainProvider20>();
            }
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            if (!registerPlainFirst)
            {
                builder.AddDiModel<PlainProvider10>()
                    .AddDiModel<PlainProvider10>()
                    .AddDiModel<PlainProvider20>();
            }

            var shared = new SharedProvider();
            services.AddSingleton<IMachineryModelProvider>(shared);
            services.AddSingleton<IDiModelProvider>(shared);

            using ServiceProvider provider = services.BuildServiceProvider();
            ArrayOf<IMachineryModelProvider> resolved = provider.GetDiModelProviders<IMachineryModelProvider>(
                static plain => new DiModelProviderAdapter(plain));
            ArrayOf<IMachineryModelProvider> normalized =
                MachineryModelProviderUtilities.Normalize(resolved, MachineryParts.BuildingBlocks);
            MachineryNodeManagerFactory factory = provider.GetRequiredService<MachineryNodeManagerFactory>();
            IMachineryModelProvider[] customProviders = (normalized.ToArray() ?? [])
                .Where(model => model is not MachineryModelProvider)
                .ToArray();
            var nodes = new NodeStateCollection();
            foreach (IMachineryModelProvider model in customProviders)
            {
                model.AddPredefinedNodes(nodes, context: null!);
            }

            MachineryServerOptions options = provider.GetRequiredService<IOptions<MachineryServerOptions>>().Value;
            var expectedNamespaces = new List<string>();
            void AddExpected(string uri)
            {
                if (!expectedNamespaces.Contains(uri))
                {
                    expectedNamespaces.Add(uri);
                }
            }

            foreach (string uri in normalized[0].NamespaceUris)
            {
                AddExpected(uri);
            }
            foreach (IMachineryModelProvider model in customProviders)
            {
                foreach (string uri in model.NamespaceUris)
                {
                    AddExpected(uri);
                }
            }
            AddExpected(options.InstanceNamespaceUri);
            AddExpected(DiNodeManager.DiNamespaceUri);

            Assert.Multiple(() =>
            {
                Assert.That(normalized[0], Is.TypeOf<MachineryModelProvider>(),
                    "the built-in provider stays first despite custom providers with minimum order");
                Assert.That(normalized[1], Is.SameAs(shared));
                Assert.That(customProviders, Has.Length.EqualTo(3),
                    "one shared object plus two value-equal but distinct plain providers");
                Assert.That(ReferenceEquals(resolved[1], shared), Is.True);
                Assert.That((resolved.ToArray() ?? []).Count(model => ReferenceEquals(model, shared)),
                    Is.EqualTo(1));
                Assert.That(customProviders.Select(model => model.Order),
                    Is.EqualTo(new List<int> { int.MinValue, 10, 20 }));
                Assert.That(nodes.Select(node => node.BrowseName.Name),
                    Is.EqualTo(new List<string> { "shared", "plain10", "plain20" }));
                Assert.That(((SharedProvider)normalized[1]).CallCount, Is.EqualTo(1));
                Assert.That(provider.GetServices<IDiModelProvider>().OfType<RecordingPlainProvider>()
                    .Select(model => model.CallCount),
                    Is.EqualTo(new List<int> { 1, 1 }));
                Assert.That(factory.NamespacesUris.ToArray() ?? [], Is.EqualTo(expectedNamespaces));
                Assert.That(expectedNamespaces.FindAll(uri => uri == DiNodeManager.DiNamespaceUri),
                    Has.Count.EqualTo(1));
                Assert.That(provider.GetServices<IDiModelProvider>()
                    .Count(model => model is PlainProvider10), Is.EqualTo(1),
                    "repeated AddDiModel of the same type registers one plain provider");
            });
        }

        public abstract class RecordingPlainProvider : IDiModelProvider
        {
            protected RecordingPlainProvider(int order, string label, string namespaceUri)
            {
                Order = order;
                Label = label;
                NamespaceUris = [namespaceUri, "urn:machinery-tests:shared"];
            }

            public int Order { get; }
            public string Label { get; }
            public ArrayOf<string> NamespaceUris { get; }
            public int CallCount { get; private set; }

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
                CallCount++;
                MachineryProviderCompositionTests.RecordNode(Label, nodes);
            }

            public override bool Equals(object? obj) => obj is RecordingPlainProvider;
            public override int GetHashCode() => 1;
        }

        public sealed class PlainProvider10 : RecordingPlainProvider
        {
            public PlainProvider10()
                : base(10, "plain10", "urn:machinery-tests:plain10")
            {
            }
        }

        public sealed class PlainProvider20 : RecordingPlainProvider
        {
            public PlainProvider20()
                : base(20, "plain20", "urn:machinery-tests:plain20")
            {
            }
        }

        private sealed class SharedProvider : IMachineryModelProvider
        {
            public int Order => int.MinValue;
            public ArrayOf<string> NamespaceUris => ["urn:machinery-tests:shared"];
            public int CallCount { get; private set; }

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
                CallCount++;
                MachineryProviderCompositionTests.RecordNode("shared", nodes);
            }
        }

        private static void RecordNode(string callbackName, NodeStateCollection nodes)
        {
            nodes.Add(new BaseObjectState(null)
            {
                NodeId = new NodeId(callbackName, 1),
                BrowseName = new QualifiedName(callbackName),
                DisplayName = new LocalizedText(callbackName)
            });
        }
    }
}
