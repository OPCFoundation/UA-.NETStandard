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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotProjectionDeclarationContextTests
    {
        [TestCase("type")]
        [TestCase("ua:HasTypeDefinition")]
        public async Task CoordinatorDistinguishesContainedDeclarationsFromInstancesOfTheSameModel(
            string typeRelation)
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "model", WoTDocumentKindEnum.ThingModel,
                TypeDeclarationDocument()).ConfigureAwait(false);
            await AddAsync(registry, "nested", WoTDocumentKindEnum.ThingDescription,
                Document("nested", "uav:componentOf", "model")).ConfigureAwait(false);
            await AddAsync(registry, "leaf", WoTDocumentKindEnum.ThingDescription,
                Document("leaf", "uav:componentOf", "nested")).ConfigureAwait(false);
            await AddAsync(registry, "instance", WoTDocumentKindEnum.ThingDescription,
                Document("instance", typeRelation, "model")).ConfigureAwait(false);
            await AddAsync(registry, "instance-child", WoTDocumentKindEnum.ThingDescription,
                Document("instance-child", "uav:componentOf", "instance")).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, new WotProtocolBinderRegistry([]),
                documentConverter: new FakeWotDocumentConverter());

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(result.Results.All(item => item.Outcome == WoTOutcomeEnum.Warning), Is.True,
                string.Join("; ", result.Results.Select(item => $"{item.ResourceId}: {item.Message}")));
            var plans = new List<WotBindingPlan>();
            foreach (HostOperation operation in host.Operations.Where(operation => operation.Op == "add"))
            {
                ArrayOf<WotBindingPlan> bindings = operation.Document!.BindingPlans;
                for (int i = 0; i < bindings.Count; i++)
                {
                    plans.Add(bindings[i]);
                }
            }
            Assert.That(PlanFor("model").IsDeclarationContext, Is.True);
            Assert.That(PlanFor("nested").IsDeclarationContext, Is.True);
            Assert.That(PlanFor("leaf").IsDeclarationContext, Is.True);
            Assert.That(PlanFor("instance").IsDeclarationContext, Is.False);
            Assert.That(PlanFor("instance-child").IsDeclarationContext, Is.False);
            Assert.That(PlanFor("model").FullySupported, Is.True);
            Assert.That(PlanFor("nested").FullySupported, Is.True);
            Assert.That(PlanFor("leaf").FullySupported, Is.True);
            Assert.That(PlanFor("instance").UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(PlanFor("instance-child").UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(PlanFor("nested").ProjectedAffordances.Count, Is.EqualTo(1));
            Assert.That(PlanFor("nested").ProjectedAffordances[0].Name, Is.EqualTo("Run"));

            WotBindingPlan PlanFor(string id)
            {
                WotResource resource = registry.Current.AllResources().Single(item => item.ResourceId == id);
                return plans.Single(plan => plan.ResourceXid == resource.Xid);
            }
        }

        [Test]
        public async Task ContainmentAddsTheModelAncestorToTheClosureBeforeItsChild()
        {
            using var registry = new WotRegistryService();
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            byte[] model = TypeDeclarationDocument();
            byte[] child = Document("child", "uav:componentOf", "model");
            await AddAsync(registry, "model", WoTDocumentKindEnum.ThingModel, model).ConfigureAwait(false);
            await AddAsync(registry, "child", WoTDocumentKindEnum.ThingDescription, child).ConfigureAwait(false);
            foreach (WotResource resource in registry.Current.AllResources())
            {
                contents[resource.DefaultVersion!.DigestHex] = ByteString.From(
                    resource.ResourceId == "model" ? model : child);
            }
            WotResource selected = registry.Current.AllResources().Single(resource => resource.ResourceId == "child");

            var closures = await WotDependencyGraph.BuildClosuresAsync(
                registry.Current, [selected], 64,
                (version, _) => new ValueTask<ByteString>(contents[version.DigestHex]), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].IsProjectable, Is.True);
            Assert.That(closures[0].OrderedResources.Select(resource => resource.ResourceId),
                Is.EqualTo(s_expectedOrder));
        }

        [Test]
        public async Task DeclarationMethodsStayPresentWithoutInventedRuntimeBindings()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("InitLock", [], []);
            var declaration = new WotProjectedAffordance(
                WotAffordanceKind.Action, "InitLock", "/actions/InitLock",
                method.NodeId.ToString(), h.Root.NodeId.ToString());
            WotBindingPlan plan = new WotBindingPlan("declaration", [], [], [], [])
                .WithProjectedAffordances([declaration])
                .WithDeclarationContext(true);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);

            IAsyncDisposable? runtime = await factory.CreateAsync(h.Builder, [plan]).ConfigureAwait(false);
            Assert.That(runtime, Is.Not.Null);
            await using var owner = runtime!.ConfigureAwait(false);
            Assert.That(h.Builder.Node(method.NodeId).Node, Is.SameAs(method));
            Assert.That(method.OnCallMethod2Async, Is.Null);
            Assert.That(method.Executable, Is.True);
            Assert.That(plan.ProjectedAffordances[0], Is.SameAs(declaration));
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);

            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await factory.CreateAsync(
                    h.Builder, [plan.WithDeclarationContext(false)]).ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeclarationContextAllowsAbsentFormsButDoesNotIgnoreMalformedForms(bool malformed)
        {
            string forms = malformed ? ", \"forms\": null" : string.Empty;
            byte[] document = Encoding.UTF8.GetBytes($$"""
                {
                  "@type": "uav:object",
                  "uav:id": "nsu=urn:model;s=Declaration",
                  "actions": { "Run": { "uav:id": "nsu=urn:model;s=Run"{{forms}} } }
                }
                """);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "declaration", WoTDocumentKindEnum.ThingDescription, document).WithDeclarationContext(true);
            var registry = new WotProtocolBinderRegistry([]);

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.IsDeclarationContext, Is.True);
            Assert.That(plan.ProjectedAffordances.Count, Is.EqualTo(1));
            Assert.That(plan.FullySupported, Is.EqualTo(!malformed));
            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(malformed ? 1 : 0));
        }

        [TestCase("uav:componentOf")]
        [TestCase("ua:HasTypeDefinition")]
        public void PortableNodeIdsDoNotBecomeMissingDocumentDependencies(string relation)
        {
            IReadOnlyList<(string Href, string RefType)> references = WotDependencyGraph.ExtractReferences(
                Document("instance", relation, "nsu=urn:loaded;s=Parent"), 64);

            Assert.That(references, Is.Empty);
        }

        [TestCase("ParentNodeId")]
        [TestCase("HasComponent")]
        [TestCase("HasProperty")]
        [TestCase("HasOrderedComponent")]
        [TestCase("CustomOwnership")]
        [TestCase("Readable")]
        public async Task StockCoordinatorUsesRootOwnershipWithoutFollowingInstanceTypeDefinitions(string ownership)
        {
            UANodeSet model = NativeModel();
            UANodeSet declaration = NativeDeclaration(ownership);
            var instance = new UANodeSet
            {
                NamespaceUris = [kNativeNamespace],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;i=1200",
                        BrowseName = "1:Pump",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "ns=1;i=1000" },
                            new Reference { ReferenceType = "HasComponent", Value = "ns=1;i=2000" }
                        ]
                    }
                ]
            };
            using var registry = new WotRegistryService();
            await AddAsync(registry, "model", WoTDocumentKindEnum.ThingModel,
                TypeDeclarationDocument()).ConfigureAwait(false);
            await AddAsync(registry, "declaration", WoTDocumentKindEnum.ThingDescription,
                NativeRootDocument(1100, 1101, ownership == "Readable")).ConfigureAwait(false);
            await AddAsync(registry, "instance", WoTDocumentKindEnum.ThingDescription,
                NativeRootDocument(1200, 2000, false)).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, new WotProtocolBinderRegistry([]),
                documentConverter: new NativePartitionConverter(new Dictionary<string, UANodeSet>
                {
                    ["model"] = model,
                    ["declaration"] = declaration,
                    ["instance"] = instance
                }));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(result.Results.All(item => item.Outcome == WoTOutcomeEnum.Warning), Is.True,
                string.Join("; ", result.Results.Select(item => $"{item.ResourceId}: {item.Message}")));
            var plans = new List<WotBindingPlan>();
            foreach (HostOperation operation in host.Operations.Where(operation => operation.Op == "add"))
            {
                ArrayOf<WotBindingPlan> bindings = operation.Document!.BindingPlans;
                for (int i = 0; i < bindings.Count; i++)
                {
                    plans.Add(bindings[i]);
                }
            }
            string declarationXid = registry.Current.AllResources()
                .Single(resource => resource.ResourceId == "declaration").Xid;
            string instanceXid = registry.Current.AllResources()
                .Single(resource => resource.ResourceId == "instance").Xid;
            WotBindingPlan declarationPlan = plans.Single(plan => plan.ResourceXid == declarationXid);
            Assert.That(declarationPlan.IsDeclarationContext, Is.True);
            Assert.That(declarationPlan.FullySupported, Is.True);
            WotBindingPlan instancePlan = plans.Single(plan => plan.ResourceXid == instanceXid);
            Assert.That(instancePlan.IsDeclarationContext, Is.False);
            Assert.That(instancePlan.UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(instancePlan.ProjectedAffordances[0].NodeId, Is.EqualTo(
                "nsu=urn:native-ownership;i=2000"));
            Assert.That(declaration.Items![0], Is.TypeOf<UAObject>());
            Assert.That(instance.Items![0], Is.TypeOf<UAObject>());

            var harness = new WotProjectionBindingRuntimeTestHarness();
            var factory = new WotProjectionBindingRuntimeFactory(harness.ChannelFactory);
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await factory.CreateAsync(harness.Builder, [instancePlan]).ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task AvailableNativePartitionsProvideOwnershipAcrossSeparateDocumentClosures()
        {
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };
            using WotDocument model = WotNodeSetConverter.FromNodeSet(NativeModel(), options: options);
            using WotDocument child = WotNodeSetConverter.FromNodeSet(
                NativeDeclaration("ParentNodeId"), options: options);
            Assert.That(model.TryGetNativeProjection(out _) || model.TryGetEnvelope(out _), Is.True);
            Assert.That(child.TryGetNativeProjection(out _) || child.TryGetEnvelope(out _), Is.True);
            using var registry = new WotRegistryService();
            await AddAsync(registry, "model", WoTDocumentKindEnum.ThingModel,
                model.Utf8Json.ToArray()).ConfigureAwait(false);
            await AddAsync(registry, "child", WoTDocumentKindEnum.ThingDescription,
                child.Utf8Json.ToArray()).ConfigureAwait(false);
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            foreach (WotResource resource in registry.Current.AllResources())
            {
                contents[resource.DefaultVersion!.DigestHex] = ByteString.From(
                    resource.ResourceId == "model" ? model.Utf8Json.ToArray() : child.Utf8Json.ToArray());
            }
            var context = new WotProjectionDeclarationContext(
                registry.Current, 64,
                (version, _) => new ValueTask<ByteString>(contents[version.DigestHex]));
            context.AddAvailableNativePartitions(contents, options, CancellationToken.None);
            WotResource childResource = registry.Current.AllResources()
                .Single(resource => resource.ResourceId == "child");

            Assert.That(await context.IsDeclarationAsync(childResource, CancellationToken.None).ConfigureAwait(false),
                Is.True);
        }

        [Test]
        public void NativeOwnershipWalkIsBoundedAndDoesNotFollowTypeDefinitionOrForwardEdges()
        {
            var index = new WotNativeOwnershipIndex(8);
            var nodes = new UANodeSet
            {
                NamespaceUris = [kNativeNamespace],
                Items =
                [
                    new UAVariableType { NodeId = "ns=1;i=1", BrowseName = "1:Type", DataType = "i=12" },
                    new UAVariable { NodeId = "ns=1;i=2", BrowseName = "1:Declaration", ParentNodeId = "ns=1;i=1" },
                    new UAObject
                    {
                        NodeId = "ns=1;i=3", BrowseName = "1:Instance",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "ns=1;i=1" },
                            new Reference { ReferenceType = "HasComponent", Value = "ns=1;i=2" }
                        ]
                    }
                ]
            };
            index.Add(nodes, "ns=1;i=2", ExpandedNodeId.Null);
            Assert.That(index.IsDeclaration(new ExpandedNodeId(2, kNativeNamespace)), Is.True);
            Assert.That(index.IsDeclaration(new ExpandedNodeId(3, kNativeNamespace)), Is.False);
            var limited = new WotNativeOwnershipIndex(1);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => limited.Add(nodes, "ns=1;i=2", ExpandedNodeId.Null));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        private static UANodeSet NativeModel()
        {
            return new UANodeSet
            {
                NamespaceUris = [kNativeNamespace],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:Type",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" }
                        ]
                    },
                    new UAMethod { NodeId = "ns=1;i=2000", BrowseName = "1:Run", ParentNodeId = "ns=1;i=1000" },
                    new UAReferenceType
                    {
                        NodeId = "ns=1;i=3000", BrowseName = "1:Owns",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=47" }
                        ]
                    }
                ]
            };
        }

        private static UANodeSet NativeDeclaration(string ownership)
        {
            var references = new List<Reference>
            {
                new Reference { ReferenceType = "HasTypeDefinition", Value = "i=58" }
            };
            if (ownership is not ("Readable" or "ParentNodeId"))
            {
                references.Add(new Reference
                {
                    ReferenceType = ownership switch
                    {
                        "HasProperty" => "Property",
                        "HasOrderedComponent" => "Ordered",
                        "CustomOwnership" => "Custom",
                        _ => "Component"
                    },
                    IsForward = false,
                    Value = "ParentAlias"
                });
            }
            return new UANodeSet
            {
                NamespaceUris = ["urn:unrelated", kNativeNamespace],
                Aliases =
                [
                    new NodeIdAlias { Alias = "Owner", Value = "ns=2;i=1000" },
                    new NodeIdAlias { Alias = "ParentAlias", Value = "ns=2;i=1000" },
                    new NodeIdAlias { Alias = "Component", Value = "i=47" },
                    new NodeIdAlias { Alias = "Property", Value = "i=46" },
                    new NodeIdAlias { Alias = "Ordered", Value = "i=49" },
                    new NodeIdAlias { Alias = "Custom", Value = "ns=2;i=3000" }
                ],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=2;i=1100",
                        BrowseName = "2:Declaration",
                        ParentNodeId = ownership == "ParentNodeId" ? "ParentAlias" : null,
                        References = references.ToArray()
                    },
                    new UAMethod { NodeId = "ns=2;i=1101", BrowseName = "2:Run", ParentNodeId = "ns=2;i=1100" }
                ]
            };
        }

        private static byte[] NativeRootDocument(uint root, uint method, bool readableOwnership)
        {
            string relation = readableOwnership ? "uav:componentOf" : "type";
            return Encoding.UTF8.GetBytes($$"""
                {
                  "@context": { "uav": "http://opcfoundation.org/UA/WoT-Binding/" },
                  "@type": "uav:object",
                  "id": "urn:native:{{root}}",
                  "uav:id": "nsu=urn:native-ownership;i={{root}}",
                  "links": [{ "rel": "{{relation}}", "href": "model" }],
                  "actions": { "Run": { "uav:id": "nsu=urn:native-ownership;i={{method}}" } }
                }
                """);
        }

        private sealed class NativePartitionConverter(IReadOnlyDictionary<string, UANodeSet> partitions)
            : IWotDocumentConverter
        {
            public ValueTask<WotConversionOutput> ConvertAsync(
                WotResource resource,
                ByteString content,
                WotRegistrySnapshot snapshot,
                IReadOnlyDictionary<string, ByteString> contents,
                CancellationToken cancellationToken)
            {
                uint root = resource.ResourceId switch
                {
                    "model" => 1000u,
                    "declaration" => 1100u,
                    _ => 1200u
                };
                return new ValueTask<WotConversionOutput>(
                    new WotConversionOutput(
                        partitions[resource.ResourceId], [], new ExpandedNodeId(root, kNativeNamespace)));
            }
        }

        private static ValueTask<WotRegistryMutationResult> AddAsync(
            WotRegistryService registry, string id, WoTDocumentKindEnum kind, byte[] document)
        {
            return registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = kind == WoTDocumentKindEnum.ThingModel
                    ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                ResourceId = id,
                Kind = kind,
                Content = ByteString.From(document)
            });
        }

        private static byte[] Document(string id, string relation, string parent)
        {
            return Encoding.UTF8.GetBytes($$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
                  ],
                  "@type": "uav:object",
                  "id": "urn:{{id}}",
                  "title": "{{id}}",
                  "uav:id": "nsu=urn:declarations;s={{id}}",
                  "links": [{ "rel": "{{relation}}", "href": "{{parent}}" }],
                  "actions": {
                    "Run": {
                      "@type": "uav:method",
                      "uav:id": "nsu=urn:declarations;s={{id}}.Run"
                    }
                  }
                }
                """);
        }

        private static byte[] TypeDeclarationDocument()
        {
            return Encoding.UTF8.GetBytes(
                """
                {
                  "@context": { "uav": "http://opcfoundation.org/UA/WoT-Binding/" },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "id": "urn:model",
                  "uav:id": "nsu=urn:native-ownership;i=1000",
                  "title": "Type"
                }
                """);
        }

        private static readonly string[] s_expectedOrder = ["model", "child"];
        private const string kNativeNamespace = "urn:native-ownership";
    }
}
