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
using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Registry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Tests.Registry
{
    /// <summary>
    /// Tests AAS registry projection metadata required by clause 6.5.2 and 6.5.3.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasRegistryProjectionTests
    {
        /// <summary>
        /// Verifies each source identity is exposed verbatim on the generated AAS type property.
        /// </summary>
        [Test]
        public async Task ProjectionExposesEverySourceIdentityVerbatim()
        {
            var service = new AasRegistryService();
            await service.UpsertResourceAsync(Request(
                "aas-shell-id", "submodel-id", AasRegistryEntityKind.Shell, AasRegistryEntityKind.Submodel));
            await service.UpsertResourceAsync(Request(
                "template-namespace", "template-submodel-id",
                AasRegistryEntityKind.SubmodelTemplate,
                AasRegistryEntityKind.Submodel));
            await service.UpsertResourceAsync(Request(
                "dictionary-id", "concept-id",
                AasRegistryEntityKind.ConceptDictionary,
                AasRegistryEntityKind.ConceptDescription));
            await service.UpsertResourceAsync(Request(
                "store-id", "package-id", AasRegistryEntityKind.PackageStore, AasRegistryEntityKind.Package));

            ProjectionHarness harness = await ProjectionHarness.CreateAsync(service);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Nodes.OfType<AASShellGroupState>().Single().AasIdentifier!.Value,
                    Is.EqualTo("aas-shell-id"));
                Assert.That(harness.Nodes.OfType<AASSubmodelFileState>().First(
                        node => node.SubmodelIdentifier!.Value == "submodel-id").SubmodelIdentifier!.Value,
                    Is.EqualTo("submodel-id"));
                Assert.That(harness.Nodes.OfType<AASSubmodelTemplateGroupState>().Single().TemplateNamespace!.Value,
                    Is.EqualTo("template-namespace"));
                Assert.That(harness.Nodes.OfType<AASConceptDictionaryGroupState>().Single().DictionaryIdentifier!.Value,
                    Is.EqualTo("dictionary-id"));
                Assert.That(harness.Nodes.OfType<AASConceptDescriptionFileState>().Single().ConceptIdentifier!.Value,
                    Is.EqualTo("concept-id"));
                Assert.That(harness.Nodes.OfType<AASPackageStoreGroupState>().Single().StoreIdentifier!.Value,
                    Is.EqualTo("store-id"));
                Assert.That(harness.Nodes.OfType<AASPackageFileState>().Single().PackageIdentifier!.Value,
                    Is.EqualTo("package-id"));
            });
        }

        /// <summary>
        /// Verifies concrete AASRegistry methods carry MethodDeclarationId attributes from AASRegistryType.
        /// </summary>
        [Test]
        public void ConcreteRegistryMethodsCarryDeclarationIds()
        {
            SystemContext context = ProjectionHarness.CreateContext(out NamespaceTable namespaces);
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaAasV3(context);

            Dictionary<string, NodeId> declarations = MethodsOf(
                nodes, context, ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.ObjectTypeIds.AASRegistryType, namespaces))
                .ToDictionary(method => NameOf(method), method => method.NodeId);
            List<MethodState> concrete = MethodsOf(
                nodes, context, ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.ObjectIds.AASRegistry, namespaces));

            Assert.Multiple(() =>
            {
                Assert.That(declarations.Keys, Is.SupersetOf(s_declaredRegistryMethods));
                Assert.That(concrete.Select(NameOf), Is.EquivalentTo(declarations.Keys));
                foreach (MethodState method in concrete)
                {
                    Assert.That(method.MethodDeclarationId,
                        Is.EqualTo(declarations[NameOf(method)]),
                        $"{NameOf(method)} must point at its AASRegistryType declaration.");
                    Assert.That(method.NodeId, Is.Not.EqualTo(method.MethodDeclarationId),
                        $"{NameOf(method)} is the concrete method, not the declaration.");
                }
            });
        }

        private static string NameOf(MethodState method)
        {
            return method.BrowseName.Name ?? string.Empty;
        }

        private static List<MethodState> MethodsOf(
            NodeStateCollection nodes,
            ISystemContext context,
            NodeId nodeId)
        {
            var children = new List<BaseInstanceState>();
            nodes.Single(node => node.NodeId == nodeId).GetChildren(context, children);
            return [.. children.OfType<MethodState>()];
        }

        private static readonly string[] s_declaredRegistryMethods =
            ["LookupShellsByAssetLink", "GetSubmodel"];

        private static AasUpsertResourceRequest Request(
            string groupIdentity,
            string resourceIdentity,
            AasRegistryEntityKind groupKind,
            AasRegistryEntityKind resourceKind)
        {
            return new AasUpsertResourceRequest
            {
                GroupSourceIdentity = groupIdentity,
                ResourceSourceIdentity = resourceIdentity,
                GroupKind = groupKind,
                ResourceKind = resourceKind,
                Content = ByteString.From([1, 2, 3]),
                ContentType = "application/aas+json",
                Format = "aas/3.0+json"
            };
        }

        private sealed class ProjectionHarness
        {
            private ProjectionHarness(List<NodeState> nodes)
            {
                Nodes = nodes;
            }

            public List<NodeState> Nodes { get; }

            public static async ValueTask<ProjectionHarness> CreateAsync(IAasRegistryService service)
            {
                SystemContext context = CreateContext(out NamespaceTable namespaces);
                var nodes = new List<NodeState>();
                var registryNode = new AASRegistryState(null);
                registryNode.Create(
                    context,
                    ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.ObjectIds.AASRegistry, namespaces),
                    new QualifiedName("AASRegistry", (ushort)namespaces.GetIndex(Opc.Ua.Aas.V3.Namespaces.AasV3)),
                    new LocalizedText("AASRegistry"),
                    assignNodeIds: false);
                using var projection = new AasRegistryProjection(
                    context,
                    namespaces,
                    (node, ct) =>
                    {
                        nodes.Add(node);
                        return default;
                    },
                    (nodeId, ct) => default,
                    service);
                await projection.AttachAsync(registryNode, CancellationToken.None).ConfigureAwait(false);
                return new ProjectionHarness(nodes);
            }

            public static SystemContext CreateContext(out NamespaceTable namespaces)
            {
                namespaces = new NamespaceTable();
                namespaces.Append(Opc.Ua.Namespaces.OpcUa);
                namespaces.Append(Opc.Ua.XRegistry.Namespaces.xRegistry);
                namespaces.Append(Opc.Ua.Aas.V3.Namespaces.AasV3);
                return new SystemContext(telemetry: null!)
                {
                    NamespaceUris = namespaces,
                    NodeIdFactory = new SequentialNodeIdFactory((ushort)namespaces.GetIndex(Opc.Ua.Aas.V3.Namespaces.AasV3))
                };
            }
        }

        private sealed class SequentialNodeIdFactory : INodeIdFactory
        {
            public SequentialNodeIdFactory(ushort namespaceIndex)
            {
                m_namespaceIndex = namespaceIndex;
            }

            public NodeId New(ISystemContext context, NodeState node)
            {
                m_next++;
                return new NodeId($"test-{m_next}", m_namespaceIndex);
            }

            private readonly ushort m_namespaceIndex;
            private uint m_next;
        }
    }
}
