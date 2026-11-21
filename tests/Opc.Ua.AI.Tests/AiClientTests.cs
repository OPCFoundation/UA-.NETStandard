// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AI.Client;

namespace Opc.Ua.AI.Tests
{
    [TestFixture]
    [Category("AI")]
    [Category("Client")]
    public sealed class AIClientTests
    {
        [Test]
        public void RootReportsAINamespaceAndWellKnownFolders()
        {
            var harness = new AISessionHarness();

            Assert.Multiple(() =>
            {
                Assert.That(harness.Client.IsAINamespaceAvailable, Is.True);
                Assert.That(harness.Client.AIRootId, Is.EqualTo(harness.AIRootId));
                Assert.That(harness.Client.ModelsFolderId, Is.EqualTo(harness.ModelsFolderId));
                Assert.That(harness.Client.DeploymentsFolderId, Is.EqualTo(harness.DeploymentsFolderId));
            });
        }

        [Test]
        public async Task DiscoverModelsReturnsTypedModelInstances()
        {
            var harness = new AISessionHarness();
            harness.AddModel("ModelA");

            ArrayOf<NodeId> nodes = await harness.Client.DiscoverModelsAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0], Is.EqualTo(harness.ModelNodeId));
        }

        [Test]
        public async Task EnumerateDeploymentsYieldsBrowseMetadata()
        {
            var harness = new AISessionHarness();
            harness.AddDeployment("Primary");

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in harness.Client.EnumerateDeploymentsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].BrowseName.Name, Is.EqualTo("Primary"));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.DeploymentNodeId));
        }

        [Test]
        public async Task ModelReadReturnsNamedSnapshotValues()
        {
            var harness = new AISessionHarness();
            harness.AddValueChild(harness.ModelNodeId, BrowseNames.ModelId, new NodeId(2100u, 3), "model-1");
            harness.AddValueChild(harness.ModelNodeId, BrowseNames.Name, new NodeId(2101u, 3), "demo");
            harness.AddValueChild(harness.ModelNodeId, BrowseNames.Version, new NodeId(2102u, 3), "1.0");
            harness.AddValueChild(harness.ModelNodeId, BrowseNames.Digest, new NodeId(2103u, 3), ByteString.From([1, 2, 3]));

            AIModelSnapshot snapshot = await harness.Client.Model(harness.ModelNodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ModelId, Is.EqualTo("model-1"));
                Assert.That(snapshot.Name, Is.EqualTo("demo"));
                Assert.That(snapshot.Version, Is.EqualTo("1.0"));
                Assert.That(snapshot.Digest.Length, Is.EqualTo(3));
            });
        }

        [Test]
        public void AIBrowseClientIsNotPublicApi()
        {
            Type[] exported = typeof(AIClient).Assembly.GetExportedTypes();

            Assert.That(exported.Any(t => t.Name == "AIBrowseClient"), Is.False);
        }

        [Test]
        public void PublicApiDoesNotExposeObjectOrByteArrayOnClientTypes()
        {
            Type[] exported = typeof(AIClient).Assembly.GetExportedTypes()
                .Where(t => t.Namespace == typeof(AIClient).Namespace)
                .ToArray();

            foreach (Type type in exported)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.DeclaringType == typeof(object) ||
                        method.Name is nameof(object.Equals) or nameof(object.GetHashCode))
                    {
                        continue;
                    }
                    Assert.That(method.ReturnType, Is.Not.EqualTo(typeof(byte[])), type.FullName + "." + method.Name);
                    Assert.That(method.ReturnType, Is.Not.EqualTo(typeof(object)), type.FullName + "." + method.Name);
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(byte[])), method.Name);
                        Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(object)), method.Name);
                    }
                }
            }
        }
    }
}
