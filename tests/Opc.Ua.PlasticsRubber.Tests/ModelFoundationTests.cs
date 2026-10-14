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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;

namespace Opc.Ua.PlasticsRubber.Tests
{
    /// <summary>
    /// Checks compiled model declarations against the pinned, redistributable inputs.
    /// </summary>
    [TestFixture]
    [Category("PlasticsRubber")]
    public sealed class ModelFoundationTests
    {
        /// <summary>
        /// Gets the parameterized test identities.
        /// </summary>
        private static IEnumerable<string> ModelNames =>
            (CompanionModels.All.ToArray() ?? []).Select(model => model.Name);

        /// <summary>
        /// Gets only models that declare their own data types.
        /// </summary>
        private static IEnumerable<string> DataTypeModelNames =>
            (CompanionModels.All.ToArray() ?? [])
                .Where(model => model.Register != null)
                .Select(model => model.Name);

        /// <summary>
        /// Loads immutable test inputs without using runtime NodeSet import.
        /// </summary>
        [OneTimeSetUp]
        public async Task ReadModelInputsAsync()
        {
            foreach (CompanionModel model in CompanionModels.All.ToArray() ?? [])
            {
                string path = Path.Combine(
                    TestContext.CurrentContext.TestDirectory, "Model", model.ClrNamespace + ".NodeSet2.xml");
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                using var reader = new StreamReader(stream);
                string text = await reader.ReadToEndAsync().ConfigureAwait(false);
                m_sources.Add(model.Name, XDocument.Parse(text));
            }
        }

        /// <summary>
        /// Pins the assembly's own payload and all model-dependency contracts.
        /// </summary>
        [TestCaseSource(nameof(ModelNames))]
        public void DependencyMetadataPreservesModelIdentity(string name)
        {
            CompanionModel model = GetModel(name);
            XElement declared = GetRoot(name).Element(s_ns + "Models")!.Element(s_ns + "Model")!;
            ModelDependencyAttribute[] metadata =
                [.. model.Assembly.GetCustomAttributes<ModelDependencyAttribute>()];
            ModelDependencyAttribute[] producers =
                [.. metadata.Where(item => item.Payload != null && item.ModelUri == model.Uri)];

            Assert.That(producers, Has.Length.EqualTo(1));
            ModelDependencyAttribute own = producers[0];
            Assert.Multiple(() =>
            {
                Assert.That(own.ModelUri, Is.EqualTo(model.Uri));
                Assert.That(own.Prefix, Is.EqualTo(model.ClrNamespace));
                Assert.That(own.Name, Is.EqualTo(model.Name));
                Assert.That(own.Version, Is.EqualTo((string?)declared.Attribute("Version")));
                Assert.That(
                    DateTime.Parse(own.PublicationDate!, CultureInfo.InvariantCulture).ToUniversalTime(),
                    Is.EqualTo(DateTime.Parse(
                        (string)declared.Attribute("PublicationDate")!,
                        CultureInfo.InvariantCulture).ToUniversalTime()));
                Assert.That(own.Payload, Is.Not.Empty);
            });

            foreach (XElement dependency in declared.Elements(s_ns + "RequiredModel"))
            {
                string uri = (string)dependency.Attribute("ModelUri")!;
                // The generator deliberately treats namespace zero as implicit.
                if (uri == Ua.Namespaces.OpcUa)
                {
                    continue;
                }
                Assert.That(metadata.Any(item => item.ModelUri == uri), Is.True, uri);
            }
        }

        /// <summary>
        /// Checks every published identifier, node class, qualified name and type binding.
        /// </summary>
        [TestCaseSource(nameof(ModelNames))]
        public void CompiledLoaderPreservesEveryDeclaredNode(string name)
        {
            CompanionModel model = GetModel(name);
            SystemContext context = CompanionModels.CreateSystemContext();
            var roots = new NodeStateCollection();
            model.Load(roots, context);
            Dictionary<NodeId, NodeState> nodes = Flatten(roots, context);
            XElement source = GetRoot(name);
            var sourceUris = new List<string> { Ua.Namespaces.OpcUa };
            sourceUris.AddRange(source.Element(s_ns + "NamespaceUris")!.Elements().Select(uri => uri.Value));
            var aliases = source.Element(s_ns + "Aliases")!.Elements()
                .ToDictionary(alias => (string)alias.Attribute("Alias")!, alias => alias.Value);

            var expectedIds = new HashSet<NodeId>(source.Elements()
                .Where(node => node.Attribute("NodeId") != null)
                .Select(node => Remap(
                    (string)node.Attribute("NodeId")!, sourceUris, aliases, context.NamespaceUris)));
            int index = context.NamespaceUris.GetIndex(model.Uri);
            // Inherited declarations without published numeric IDs receive generated string IDs.
            NodeId[] actualIds = [.. nodes.Keys.Where(id => id.NamespaceIndex == index && id.IdType == IdType.Numeric)];
            Assert.That(
                actualIds,
                Has.Length.EqualTo(model.NodeCount),
                "Missing: " +
                string.Join(", ", expectedIds.Except(actualIds).Take(20)) +
                "; unexpected: " +
                string.Join(", ", actualIds.Except(expectedIds).Take(20)));
            foreach (XElement declaration in source.Elements().Where(node => node.Attribute("NodeId") != null))
            {
                string sourceId = (string)declaration.Attribute("NodeId")!;
                NodeId id = Remap(sourceId, sourceUris, aliases, context.NamespaceUris);
                Assert.That(nodes.TryGetValue(id, out NodeState? node), Is.True, sourceId);
                NodeState loaded = node!;
                string browseName = (string)declaration.Attribute("BrowseName")!;
                QualifiedName declaredName = QualifiedName.Parse(browseName);
                ushort sourceIndex = declaredName.NamespaceIndex;
                string? localName = declaredName.Name;
                Assert.Multiple(() =>
                {
                    Assert.That(
                        loaded.NodeClass.ToString(), Is.EqualTo(declaration.Name.LocalName[2..]), sourceId);
                    Assert.That(loaded.BrowseName.Name, Is.EqualTo(localName), sourceId);
                    Assert.That(
                        loaded.BrowseName.NamespaceIndex,
                        Is.EqualTo(context.NamespaceUris.GetIndex(sourceUris[sourceIndex])),
                        sourceId);
                });

                if (loaded is BaseVariableState variable)
                {
                    string dataType = (string?)declaration.Attribute("DataType") ?? "i=24";
                    int valueRank = int.Parse(
                        (string?)declaration.Attribute("ValueRank") ?? "-1", CultureInfo.InvariantCulture);
                    Assert.Multiple(() =>
                    {
                        Assert.That(
                            variable.DataType,
                            Is.EqualTo(Remap(dataType, sourceUris, aliases, context.NamespaceUris)),
                            sourceId);
                        Assert.That(variable.ValueRank, Is.EqualTo(valueRank), sourceId);
                    });
                }

                foreach (XElement reference in declaration.Element(s_ns + "References")!.Elements())
                {
                    string kind = (string)reference.Attribute("ReferenceType")!;
                    string resolved = aliases.TryGetValue(kind, out string? alias) ? alias : kind;
                    NodeId target = Remap(reference.Value, sourceUris, aliases, context.NamespaceUris);
                    if (loaded is BaseInstanceState instance)
                    {
                        if (resolved == "i=40")
                        {
                            Assert.That(instance.TypeDefinitionId, Is.EqualTo(target), sourceId);
                        }
                        else if (resolved == "i=37")
                        {
                            Assert.That(instance.ModellingRuleId, Is.EqualTo(target), sourceId);
                        }
                    }
                    if (loaded is BaseTypeState type &&
                        resolved == "i=45" &&
                        (string?)reference.Attribute("IsForward") == "false")
                    {
                        Assert.That(type.SuperTypeId, Is.EqualTo(target), sourceId);
                    }
                }
            }
        }

        /// <summary>
        /// Ensures every ObjectType has a typed generated client in its owning assembly.
        /// </summary>
        [TestCaseSource(nameof(ModelNames))]
        public void EveryObjectTypeHasItsGeneratedClient(string name)
        {
            CompanionModel model = GetModel(name);
            foreach (XElement type in GetRoot(name).Elements(s_ns + "UAObjectType"))
            {
                string browseName = (string)type.Attribute("BrowseName")!;
                string localName = browseName[(browseName.AsSpan().IndexOf(':') + 1)..];
                Type? client = model.Assembly.GetType(model.ClrNamespace + "." + localName + "Client");
                Assert.That(client, Is.Not.Null, browseName);
                Assert.That(
                    client!.GetConstructor([typeof(ISessionClient), typeof(NodeId), typeof(ITelemetryContext)]),
                    Is.Not.Null,
                    browseName);
            }
        }

        /// <summary>
        /// Checks all generated enum and structure registrations against source definitions.
        /// </summary>
        [TestCaseSource(nameof(DataTypeModelNames))]
        public void EveryDataTypeHasGeneratedFactoryMetadata(string name)
        {
            CompanionModel model = GetModel(name);
            ServiceMessageContext context = CompanionModels.CreateMessageContext();
            foreach (XElement type in GetRoot(name).Elements(s_ns + "UADataType"))
            {
                NodeId local = NodeId.Parse((string)type.Attribute("NodeId")!).WithNamespaceIndex(
                    context.NamespaceUris.GetIndexOrAppend(model.Uri));
                var id = NodeId.ToExpandedNodeId(local, context.NamespaceUris);
                XElement definition = type.Element(s_ns + "Definition")!;
                if (definition.Elements().All(field => field.Attribute("Value") != null))
                {
                    Assert.That(context.Factory.TryGetEnumeratedType(id, out IEnumeratedType? registered), Is.True);
                    Assert.That(registered!.Type.Assembly, Is.SameAs(model.Assembly));
                    foreach (XElement field in definition.Elements())
                    {
                        int number = int.Parse((string)field.Attribute("Value")!, CultureInfo.InvariantCulture);
                        Assert.That(registered.TryGetSymbol(number, out string? symbol), Is.True);
                        Assert.That(registered.TryGetValue(symbol!, out int decoded), Is.True);
                        Assert.That(decoded, Is.EqualTo(number));
                    }
                }
                else
                {
                    Assert.That(context.Factory.TryGetEncodeableType(id, out IEncodeableType? registered), Is.True);
                    IEncodeable instance = registered!.CreateInstance();
                    Assert.Multiple(() =>
                    {
                        Assert.That(registered.Type.Assembly, Is.SameAs(model.Assembly));
                        Assert.That(instance.TypeId, Is.EqualTo(id));
                        Assert.That(context.Factory.TryGetEncodeableType(instance.BinaryEncodingId, out _), Is.True);
                        Assert.That(context.Factory.TryGetEncodeableType(instance.XmlEncodingId, out _), Is.True);
                    });
                }
            }
        }

        /// <summary>
        /// Verifies that the full dependency graph composes without duplicate producers.
        /// </summary>
        [Test]
        public void AllModelsComposeWithExistingDependencyProducers()
        {
            SystemContext context = CompanionModels.CreateSystemContext();
            var roots = new NodeStateCollection();
            roots.AddOpcUaDi(context);
            roots.AddOpcUaIA(context);
            roots.AddOpcUaMachinery(context);
            foreach (CompanionModel model in CompanionModels.All)
            {
                model.Load(roots, context);
            }
            Dictionary<NodeId, NodeState> nodes = Flatten(roots, context);
            foreach (CompanionModel model in CompanionModels.All)
            {
                int index = context.NamespaceUris.GetIndex(model.Uri);
                Assert.That(
                    nodes.Values.Count(node => node.NodeId.NamespaceIndex == index &&
                        node.NodeId.IdType == IdType.Numeric),
                    Is.EqualTo(model.NodeCount),
                    model.ClrNamespace);
            }

            ModelDependencyAttribute[] declarations = [.. (CompanionModels.All.ToArray() ?? [])
                .Select(model => model.Assembly)
                .Distinct()
                .SelectMany(assembly => assembly.GetCustomAttributes<ModelDependencyAttribute>())
                .Where(item => item.Payload != null)];
            Assert.That(declarations.Select(item => item.ModelUri), Is.Unique);
            Assert.That(declarations, Has.Length.EqualTo(14));
        }

        /// <summary>
        /// All twelve extrusion parts have one producer without losing any model payload.
        /// </summary>
        [Test]
        public void AllExtrusionPartsShareOneProducingAssembly()
        {
            CompanionModel[] models = [.. (CompanionModels.All.ToArray() ?? [])
                .Where(model => model.Name.StartsWith("Extrusion", StringComparison.Ordinal))];
            Assembly assembly = typeof(Extrusion.ExtrusionDeviceState).Assembly;
            ModelDependencyAttribute[] producers = [.. assembly.GetCustomAttributes<ModelDependencyAttribute>()
                .Where(item => item.Payload != null)];

            Assert.Multiple(() =>
            {
                Assert.That(models, Has.Length.EqualTo(12));
                Assert.That(models.Select(model => model.Assembly), Is.All.SameAs(assembly));
                Assert.That(assembly.GetName().Name, Is.EqualTo("Opc.Ua.Extrusion"));
                Assert.That(producers.Select(item => item.ModelUri),
                    Is.EquivalentTo(models.Select(model => model.Uri)));
            });
        }

        /// <summary>
        /// Checks the authored alarm's mandatory UInt16 status and enum properties.
        /// </summary>
        [Test]
        public void AuthoredAlarmFactoryCreatesMandatoryStatusChildren()
        {
            SystemContext context = CompanionModels.CreateSystemContext();
            MonitoredParameterAlarmState alarm = context.CreateInstanceOfMonitoredParameterAlarmType();
            Assert.Multiple(() =>
            {
                Assert.That(alarm.Status, Is.Not.Null);
                Assert.That(alarm.Status!.DataType, Is.EqualTo(Ua.DataTypeIds.UInt16));
                Assert.That(alarm.Status.TypeDefinitionId, Is.EqualTo(VariableTypeIds.MultiStateValueDiscreteType));
                Assert.That(alarm.Status.EnumValues, Is.Not.Null);
                Assert.That(alarm.Status.ValueAsText, Is.Not.Null);
            });
        }

        /// <summary>
        /// Keeps the repurposed status identifier out of the old temperature property.
        /// </summary>
        [Test]
        public void RepurposedStatusIdentifierIsNotAnEngineeringUnitProperty()
        {
            SystemContext context = CompanionModels.CreateSystemContext();
            var roots = new NodeStateCollection();
            roots.AddOpcUaPlasticsRubber(context);
            Dictionary<NodeId, NodeState> nodes = Flatten(roots, context);
            var statusId = NodeId.Create(6711, Namespaces.PlasticsRubber, context.NamespaceUris);
            var status = (BaseVariableState)nodes[statusId];
            var temperature = (BaseVariableState)nodes[NodeId.Create(
                6017, Namespaces.PlasticsRubber, context.NamespaceUris)];
            var children = new List<BaseInstanceState>();
            temperature.GetChildren(context, children);
            Assert.Multiple(() =>
            {
                Assert.That(status.BrowseName.Name, Is.EqualTo("Status"));
                Assert.That(status.DataType, Is.EqualTo(Ua.DataTypeIds.UInt16));
                Assert.That(status.ModellingRuleId, Is.EqualTo(Ua.ObjectIds.ModellingRule_Optional));
                Assert.That(status.Parent!.NodeId, Is.EqualTo(NodeId.Create(
                    1056, Namespaces.PlasticsRubber, context.NamespaceUris)));
                Assert.That(children.Select(child => child.NodeId), Does.Not.Contain(statusId));
            });
        }

        /// <summary>
        /// Resolves one test contract by its generator name.
        /// </summary>
        private static CompanionModel GetModel(string name)
        {
            return (CompanionModels.All.ToArray() ?? []).Single(model => model.Name == name);
        }

        /// <summary>
        /// Gets a previously loaded immutable XML input.
        /// </summary>
        private XElement GetRoot(string name)
        {
            return m_sources[name].Root!;
        }

        /// <summary>
        /// Rebinds NodeSet namespace indexes to the hosting namespace table.
        /// </summary>
        private static NodeId Remap(
            string text,
            List<string> sourceUris,
            Dictionary<string, string> aliases,
            NamespaceTable namespaces)
        {
            var original = NodeId.Parse(aliases.TryGetValue(text, out string? alias) ? alias : text);
            return original.WithNamespaceIndex(namespaces.GetIndexOrAppend(sourceUris[original.NamespaceIndex]));
        }

        /// <summary>
        /// Traverses compiled nodes, deduplicating inherited declarations by their NodeIds.
        /// </summary>
        private static Dictionary<NodeId, NodeState> Flatten(NodeStateCollection roots, ISystemContext context)
        {
            var result = new Dictionary<NodeId, NodeState>();
            var pending = new Stack<NodeState>(roots);
            var traversed = new HashSet<NodeState>();
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                if (!traversed.Add(node))
                {
                    continue;
                }
                if (result.TryGetValue(node.NodeId, out NodeState? prior))
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(node.NodeClass, Is.EqualTo(prior.NodeClass), node.NodeId.ToString());
                        Assert.That(node.BrowseName, Is.EqualTo(prior.BrowseName), node.NodeId.ToString());
                    });
                }
                else
                {
                    result.Add(node.NodeId, node);
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
            return result;
        }

        /// <summary>The source documents, keyed by the pinned generator name.</summary>
        private readonly Dictionary<string, XDocument> m_sources = [];

        /// <summary>The OPC UA NodeSet XML namespace.</summary>
        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
