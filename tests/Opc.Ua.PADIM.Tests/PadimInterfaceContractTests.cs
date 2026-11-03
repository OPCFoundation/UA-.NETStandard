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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.PADIM.Server;

namespace Opc.Ua.PADIM.Tests
{
    public sealed partial class PadimRuntimeTests
    {
        /// <summary>
        /// Selects every published signal condition and calibration member, including inherited interfaces.
        /// </summary>
        [Test]
        public async Task EverySignalInterfaceRetainsItsPublishedMembers([Values] PadimSignalKind kind)
        {
            PadimDevice device = await CreateDeviceAsync($"SignalInterfaces{kind}");
            var samples = new PadimPublishedSampleSet();
            SignalState signal = await device.AddSignalAsync(Name("Signal"), kind, "AT-001");
            List<XElement> conditions = GroupDeclarations(signal.TypeDefinitionId, "SignalConditionSet");
            if (conditions.Count == 0)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await device.ConfigureSignalConditionsAsync(signal, _ => { }));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
            else
            {
                BaseObjectState group = await device.ConfigureSignalConditionsAsync(
                    signal, builder => SelectPublishedMembers(device, builder, conditions, samples));
                AssertInterfaces(device, group, conditions);
            }
            List<XElement> calibrations = GroupDeclarations(signal.TypeDefinitionId, "<SignalCalibrationIdentifier>");
            if (signal is AnalogSignalObjectState analog)
            {
                Assert.That(calibrations, Is.Not.Empty);
                BaseObjectState group = await device.AddCalibrationAsync(
                    analog, Name("Calibration"), builder =>
                        SelectPublishedMembers(device, builder, calibrations, samples));
                AssertInterfaces(device, group, calibrations);
            }
            else
            {
                Assert.That(calibrations, Is.Empty);
            }
            await device.RefreshAsync();
            samples.AssertPublished();
        }

        /// <summary>
        /// Selects every published device-condition member for each analyser family.
        /// </summary>
        [Test]
        public async Task EveryDeviceConditionInterfaceRetainsItsPublishedMembers([Values] PadimDeviceKind kind)
        {
            var builder = await m_manager.CreatePadimDeviceAsync(Name($"DeviceInterfaces{kind}"), kind);
            PadimDevice device = builder.BindPadim();
            var samples = new PadimPublishedSampleSet();
            List<XElement> declarations = GroupDeclarations(device.State.TypeDefinitionId, "DeviceConditionSet");
            Assert.That(declarations, Is.Not.Empty);
            BaseObjectState group = await device.ConfigureDeviceConditionsAsync(
                selected => SelectPublishedMembers(device, selected, declarations, samples));
            AssertInterfaces(device, group, declarations);
            await device.RefreshAsync();
            samples.AssertPublished();
        }

        /// <summary>
        /// Configures members from the original XML rather than from runtime factory output.
        /// </summary>
        internal static void SelectPublishedMembers(
            PadimDevice device,
            PadimGroupBuilder group,
            List<XElement> declarations,
            PadimPublishedSampleSet samples)
        {
            foreach (KeyValuePair<string, List<XElement>> member in PublishedChildren(declarations))
            {
                XElement source = member.Value[member.Value.Count - 1];
                QualifiedName name = PublishedName(device, source);
                if (source.Name.LocalName == "UAVariable")
                {
                    PadimValueBinding binding = samples.Track(
                        device, group.Bind(name, samples.NextKey(), samples.Provider));
                    BaseVariableState actual = binding.Variable;
                    string dataType = source.Attribute("DataType")?.Value ?? "i=24";
                    int rank = int.Parse(source.Attribute("ValueRank")?.Value ?? "-1", CultureInfo.InvariantCulture);
                    Assert.Multiple(() =>
                    {
                        Assert.That(actual.DataType, Is.EqualTo(PublishedNodeId(device, dataType)), source.ToString());
                        Assert.That(actual.ValueRank, Is.EqualTo(rank), source.ToString());
                        Assert.That(actual.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                        Assert.That(actual.NodeId.NamespaceIndex, Is.EqualTo(device.Manager.InstanceNamespaceIndex));
                        Assert.That(actual.BrowseName, Is.EqualTo(name));
                    });
                    XElement? publishedType = References(source, "HasTypeDefinition").SingleOrDefault();
                    if (publishedType != null)
                    {
                        Assert.That(actual.TypeDefinitionId, Is.EqualTo(
                            PublishedNodeId(device, publishedType.Value)), source.ToString());
                    }
                    foreach (XElement reference in References(source, "HasDictionaryEntry"))
                    {
                        NodeId target = PublishedNodeId(device, reference.Value);
                        var actualReferences = new List<IReference>();
                        actual.GetReferences(device.Context, actualReferences);
                        Assert.That(actualReferences.Any(item => !item.IsInverse &&
                            item.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasDictionaryEntry &&
                            ExpandedNodeId.ToNodeId(item.TargetId, device.Context.NamespaceUris) == target), Is.True,
                            $"Missing IRDI reference {reference.Value} on {name}.");
                    }
                }
                else if (source.Name.LocalName == "UAObject")
                {
                    bool placeholder = References(source, "HasModellingRule")
                        .Any(rule => rule.Value is "i=11508" or "i=11510");
                    QualifiedName instanceName = placeholder
                        ? new QualifiedName("FixtureItem", device.Manager.InstanceNamespaceIndex)
                        : QualifiedName.Null;
                    group.Group(name, nested => SelectPublishedMembers(
                        device, nested, member.Value, samples), instanceName);
                }
            }
        }

        /// <summary>
        /// Checks that inherited and specialised interfaces identify the original type nodes.
        /// </summary>
        private void AssertInterfaces(PadimDevice device, BaseObjectState group, List<XElement> declarations)
        {
            var actual = new List<IReference>();
            group.GetReferences(device.Context, actual);
            foreach (XElement expected in declarations.SelectMany(node => References(node, "HasInterface")))
            {
                NodeId target = PublishedNodeId(device, expected.Value);
                Assert.That(actual.Any(reference => !reference.IsInverse &&
                    reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasInterface &&
                    ExpandedNodeId.ToNodeId(reference.TargetId, device.Context.NamespaceUris) == target), Is.True,
                    $"Missing published interface {expected.Value}.");
            }
            Assert.That(m_manager.FindPredefinedNode<NodeState>(group.NodeId), Is.SameAs(group));
        }

        /// <summary>
        /// Collects base declarations before family overrides using the original subtype references.
        /// </summary>
        internal static List<XElement> GroupDeclarations(NodeId typeId, string name)
        {
            Assert.That(typeId.TryGetValue(out uint identifier), Is.True);
            string sourceId = $"ns=3;i={identifier}";
            var result = new List<XElement>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (visited.Add(sourceId) && s_sourceNodes.TryGetValue(sourceId, out XElement? type))
            {
                foreach (XElement reference in References(type, "HasComponent"))
                {
                    if (s_sourceNodes.TryGetValue(reference.Value, out XElement? child) &&
                        QualifiedName.Parse(child.Attribute("BrowseName")!.Value).Name == name)
                    {
                        result.Insert(0, child);
                    }
                }
                XElement? supertype = References(type, "HasSubtype", inverse: true).SingleOrDefault();
                if (supertype == null)
                {
                    break;
                }
                sourceId = supertype.Value;
            }
            return result;
        }

        /// <summary>
        /// Merges declared children with the children supplied by their published object type.
        /// </summary>
        private static Dictionary<string, List<XElement>> PublishedChildren(List<XElement> declarations)
        {
            var children = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement declaration in declarations)
            {
                Include(declaration);
            }
            return children;

            void Include(XElement declaration)
            {
                if (!visited.Add(declaration.Attribute("NodeId")!.Value))
                {
                    return;
                }
                XElement? parentType = References(declaration, "HasTypeDefinition").SingleOrDefault() ??
                    References(declaration, "HasSubtype", inverse: true).SingleOrDefault();
                if (parentType != null && s_sourceNodes.TryGetValue(parentType.Value, out XElement? type))
                {
                    Include(type);
                }
                foreach (XElement reference in References(declaration, "HasComponent")
                    .Concat(References(declaration, "HasProperty")))
                {
                    XElement child = s_sourceNodes[reference.Value];
                    string name = child.Attribute("BrowseName")!.Value;
                    if (!children.TryGetValue(name, out List<XElement>? definitions))
                    {
                        definitions = [];
                        children.Add(name, definitions);
                    }
                    definitions.Add(child);
                }
            }
        }

        /// <summary>
        /// Resolves published aliases and namespace indexes without assuming the server's namespace ordering.
        /// </summary>
        internal static NodeId PublishedNodeId(PadimDevice device, string text)
        {
            if (s_aliases.TryGetValue(text, out string? expanded))
            {
                text = expanded;
            }
            NodeId id = NodeId.Parse(text);
            return id.WithNamespaceIndex(PublishedNamespace(device, id.NamespaceIndex));
        }

        /// <summary>Resolves a published model type by its source browse name and namespace URI.</summary>
        internal static NodeId PublishedTypeId(PadimDevice device, string browseName)
        {
            XElement source = s_sourceNodes.Values.Single(node =>
                (node.Name.LocalName is "UAObjectType" or "UAVariableType") &&
                QualifiedName.Parse(node.Attribute("BrowseName")!.Value).Name == browseName);
            return PublishedNodeId(device, source.Attribute("NodeId")!.Value);
        }

        /// <summary>Resolves a published model type directly in a client's live namespace table.</summary>
        internal static NodeId PublishedTypeId(NamespaceTable namespaceUris, string browseName)
        {
            XElement source = s_sourceNodes.Values.Single(node =>
                (node.Name.LocalName is "UAObjectType" or "UAVariableType") &&
                QualifiedName.Parse(node.Attribute("BrowseName")!.Value).Name == browseName);
            return PublishedNodeId(namespaceUris, source.Attribute("NodeId")!.Value);
        }

        /// <summary>Maps a published numeric or string node id through an arbitrary live namespace table.</summary>
        internal static NodeId PublishedNodeId(NamespaceTable namespaceUris, string text)
        {
            if (s_aliases.TryGetValue(text, out string? expanded))
            {
                text = expanded;
            }
            NodeId id = NodeId.Parse(text);
            if (id.NamespaceIndex == 0)
            {
                return id;
            }
            string uri = s_publishedRoot.Element(s_xml + "NamespaceUris")!.Elements(s_xml + "Uri")
                .ElementAt(id.NamespaceIndex - 1).Value;
            int mapped = namespaceUris.GetIndex(uri);
            Assert.That(mapped, Is.GreaterThanOrEqualTo(0), $"Missing published namespace {uri}.");
            return id.WithNamespaceIndex((ushort)mapped);
        }

        /// <summary>Resolves the declared variable type from a published object's inherited declarations.</summary>
        internal static NodeId PublishedVariableTypeId(
            PadimDevice device,
            NodeId ownerTypeId,
            string variableBrowseName)
        {
            XElement owner = s_sourceNodes.Values.Single(node =>
                node.Name.LocalName == "UAObjectType" &&
                PublishedNodeId(device, node.Attribute("NodeId")!.Value) == ownerTypeId);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            XElement? current = owner;
            while (current != null && visited.Add(current.Attribute("NodeId")!.Value))
            {
                XElement? variable = References(current, "HasComponent")
                    .Concat(References(current, "HasProperty"))
                    .Select(reference => s_sourceNodes[reference.Value])
                    .FirstOrDefault(candidate =>
                        candidate.Name.LocalName == "UAVariable" &&
                        QualifiedName.Parse(candidate.Attribute("BrowseName")!.Value).Name == variableBrowseName);
                if (variable != null)
                {
                    XElement type = References(variable, "HasTypeDefinition").Single();
                    return PublishedNodeId(device, type.Value);
                }
                XElement? parent = References(current, "HasSubtype", inverse: true).SingleOrDefault();
                current = parent != null && s_sourceNodes.TryGetValue(parent.Value, out XElement? supertype)
                    ? supertype
                    : null;
            }
            throw new InvalidOperationException(
                $"Published variable '{variableBrowseName}' is absent from {ownerTypeId} and its supertypes.");
        }

        /// <summary>
        /// Maps a source BrowseName into the live namespace table.
        /// </summary>
        private static QualifiedName PublishedName(PadimDevice device, XElement node)
        {
            QualifiedName name = QualifiedName.Parse(node.Attribute("BrowseName")!.Value);
            return name.WithNamespaceIndex(PublishedNamespace(device, name.NamespaceIndex));
        }

        /// <summary>
        /// Resolves the source namespace URI rather than reusing its numeric index.
        /// </summary>
        private static ushort PublishedNamespace(PadimDevice device, ushort index)
        {
            if (index == 0)
            {
                return 0;
            }
            string uri = s_publishedRoot.Element(s_xml + "NamespaceUris")!.Elements(s_xml + "Uri")
                .ElementAt(index - 1).Value;
            int mapped = device.Context.NamespaceUris.GetIndex(uri);
            Assert.That(mapped, Is.GreaterThanOrEqualTo(0), $"Missing published namespace {uri}.");
            return (ushort)mapped;
        }

        /// <summary>
        /// Selects source references by their declared direction.
        /// </summary>
        internal static IEnumerable<XElement> References(XElement node, string type, bool inverse = false)
        {
            return node.Element(s_xml + "References")!.Elements(s_xml + "Reference")
                .Where(reference => reference.Attribute("ReferenceType")!.Value == type &&
                    (reference.Attribute("IsForward")?.Value == "false") == inverse);
        }

        /// <summary>The published NodeSet namespace.</summary>
        private static readonly XNamespace s_xml = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        /// <summary>The unmodified published source used independently of generated runtime declarations.</summary>
        private static readonly XElement s_publishedRoot = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory, "Model", "Opc.Ua.PADIM.NodeSet2.xml")).Root!;

        /// <summary>The source nodes by their original identifiers.</summary>
        private static readonly Dictionary<string, XElement> s_sourceNodes = s_publishedRoot.Elements()
            .Where(node => node.Attribute("NodeId") != null)
            .ToDictionary(node => node.Attribute("NodeId")!.Value, StringComparer.Ordinal);

        /// <summary>The source's declared aliases.</summary>
        private static readonly Dictionary<string, string> s_aliases = s_publishedRoot.Element(s_xml + "Aliases")!
            .Elements(s_xml + "Alias").ToDictionary(
                alias => alias.Attribute("Alias")!.Value, alias => alias.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Supplies declared-type-correct samples and verifies their publication through the provider pipeline.
    /// </summary>
    internal sealed class PadimPublishedSampleSet
    {
        /// <summary>Gets the strict provider shared by this bounded fixture.</summary>
        internal IPadimValueProvider Provider => m_provider.Object;

        /// <summary>Gets the bindings and exact samples for hosted client reads.</summary>
        internal IReadOnlyList<PadimPublishedSample> Samples => m_samples;

        /// <summary>Returns a unique provider key for the next fixture binding.</summary>
        internal string NextKey()
        {
            return $"published-{++m_nextKey:D5}";
        }

        /// <summary>Binds a primary family variable with a sample matching its generated declaration.</summary>
        internal PadimValueBinding Bind(PadimDevice device, BaseVariableState variable)
        {
            return Track(device, device.BindValue(variable, NextKey(), Provider));
        }

        /// <summary>Tracks a binding created by a published group selector.</summary>
        internal PadimValueBinding Track(PadimDevice device, PadimValueBinding binding)
        {
            Variant value = CreateValue(device, binding.Variable, binding.Key);
            var sample = new DataValue(
                value,
                StatusCodes.UncertainSensorNotAccurate,
                new DateTimeUtc(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(m_samples.Count + 1)));
            m_provider.Setup(provider => provider.ReadAsync(binding.Key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sample);
            m_samples.Add(new PadimPublishedSample(
                binding,
                binding.Key,
                binding.Variable.NodeId,
                binding.Variable.Parent?.NodeId ?? NodeId.Null,
                binding.Variable.BrowseName,
                binding.Variable.TypeDefinitionId,
                sample,
                GetExpectedEnumText(device, binding.Variable, value)));
            return binding;
        }

        /// <summary>Asserts every selected binding published its exact value, quality and source time once.</summary>
        internal void AssertPublished()
        {
            foreach (PadimPublishedSample expected in m_samples)
            {
                AssertSample(expected.Sample.WrappedValue, expected.Binding.Variable.WrappedValue, expected.Key);
                Assert.Multiple(() =>
                {
                    Assert.That(expected.Binding.Variable.StatusCode, Is.EqualTo(expected.Sample.StatusCode),
                        expected.Key);
                    Assert.That(expected.Binding.Variable.Timestamp, Is.EqualTo(expected.Sample.SourceTimestamp),
                        expected.Key);
                    if (expected.ExpectedEnumText != null)
                    {
                        Assert.That(
                            ((MultiStateValueDiscreteState)expected.Binding.Variable).ValueAsText!.Value.Text,
                            Is.EqualTo(expected.ExpectedEnumText),
                            expected.Key);
                    }
                });
                m_provider.Verify(
                    provider => provider.ReadAsync(expected.Key, It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            m_provider.VerifyNoOtherCalls();
        }

        /// <summary>Asserts an exact Variant raw type and value, including PAT structure members.</summary>
        internal static void AssertSample(Variant expected, Variant actual, string context)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.TypeInfo.BuiltInType, Is.EqualTo(expected.TypeInfo.BuiltInType), context);
                Assert.That(actual.TypeInfo.ValueRank, Is.EqualTo(expected.TypeInfo.ValueRank), context);
            });
            if (expected.TryGetStructure(out ChemicalSubstance? expectedSubstance))
            {
                Assert.That(actual.TryGetStructure(out ChemicalSubstance? actualSubstance), Is.True, context);
                AssertChemicalSubstance(expectedSubstance!, actualSubstance!, context);
                return;
            }
            if (expected.TryGetValue(out ArrayOf<ExtensionObject> expectedStructures))
            {
                Assert.That(actual.TryGetValue(out ArrayOf<ExtensionObject> actualStructures), Is.True, context);
                Assert.That(actualStructures.Count, Is.EqualTo(expectedStructures.Count), context);
                for (int ii = 0; ii < expectedStructures.Count; ii++)
                {
                    Assert.That(expectedStructures[ii].TryGetValue(out ChemicalSubstance? expectedItem),
                        Is.True, context);
                    Assert.That(actualStructures[ii].TryGetValue(out ChemicalSubstance? actualItem),
                        Is.True, context);
                    AssertChemicalSubstance(expectedItem!, actualItem!, context);
                }
                return;
            }
            Assert.That(actual, Is.EqualTo(expected), context);
        }

        /// <summary>Checks every scalar field of the structured PAT test value.</summary>
        private static void AssertChemicalSubstance(
            ChemicalSubstance expected,
            ChemicalSubstance actual,
            string context)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.PatDictionary, Is.EqualTo(expected.PatDictionary), context);
                Assert.That(actual.Label, Is.EqualTo(expected.Label), context);
                Assert.That(actual.Id, Is.EqualTo(expected.Id), context);
            });
        }

        /// <summary>Builds a sample using only the variable's declared UA type and rank.</summary>
        private static Variant CreateValue(PadimDevice device, BaseVariableState variable, string key)
        {
            if (IsDeviceHealth(device, variable.DataType))
            {
                return RankValue(
                    variable,
                    Variant.From(Opc.Ua.Di.DeviceHealthEnumeration.NORMAL),
                    Variant.From((ArrayOf<Opc.Ua.Di.DeviceHealthEnumeration>)[
                        Opc.Ua.Di.DeviceHealthEnumeration.NORMAL]));
            }
            if (variable.DataType == Opc.Ua.DataTypeIds.Number)
            {
                return RankValue(variable, Variant.From(21.5f), Variant.From((ArrayOf<float>)[21.5f]));
            }
            if (variable.DataType == Opc.Ua.DataTypeIds.BaseDataType)
            {
                return RankValue(variable, Variant.From($"raw-{key}"), Variant.From((ArrayOf<string>)[$"raw-{key}"]));
            }

            if (variable is MultiStateValueDiscreteState enumeration)
            {
                EnsureMultiStateMetadata(device, enumeration);
                EnumValueType selected = enumeration.EnumValues!.Value[0];
                BuiltInType rawType = TypeInfo.GetBuiltInType(variable.DataType, device.Context.TypeTable);
                return rawType switch
                {
                    BuiltInType.UInt32 => RankValue(
                        variable,
                        Variant.From(checked((uint)selected.Value)),
                        Variant.From((ArrayOf<uint>)[checked((uint)selected.Value)])),
                    BuiltInType.Int32 => RankValue(
                        variable,
                        Variant.From(checked((int)selected.Value)),
                        Variant.From((ArrayOf<int>)[checked((int)selected.Value)])),
                    _ => throw Unsupported(variable, $"multi-state raw type {rawType}")
                };
            }

            BuiltInType builtInType = TypeInfo.GetBuiltInType(variable.DataType, device.Context.TypeTable);
            Variant scalar = builtInType switch
            {
                BuiltInType.Boolean => Variant.From(true),
                BuiltInType.SByte => Variant.From((sbyte)-7),
                BuiltInType.Byte => Variant.From((byte)7),
                BuiltInType.Int16 => Variant.From((short)-17),
                BuiltInType.UInt16 => Variant.From((ushort)17),
                BuiltInType.Int32 => Variant.From(17),
                BuiltInType.Enumeration => Variant.From(new EnumValue(17)),
                BuiltInType.UInt32 => Variant.From(17u),
                BuiltInType.Int64 => Variant.From(17L),
                BuiltInType.UInt64 => Variant.From(17UL),
                BuiltInType.Number => Variant.From(21.5f),
                BuiltInType.Integer => Variant.From(17),
                BuiltInType.UInteger => Variant.From(17u),
                BuiltInType.Float => Variant.From(21.5f),
                BuiltInType.Double => Variant.From(123.25d),
                BuiltInType.String => Variant.From($"sample-{key}"),
                BuiltInType.DateTime => Variant.From(
                    new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))),
                BuiltInType.Guid => Variant.From(new Uuid("2d711642-b726-4a2f-9f2d-2f157b6f3a01")),
                BuiltInType.ByteString => Variant.From(new ByteString(new byte[] { 7, 11, 13 })),
                BuiltInType.XmlElement => Variant.From(XmlElement.From("<sample />")),
                BuiltInType.NodeId => Variant.From(new NodeId($"sample-{key}", 0)),
                BuiltInType.QualifiedName => Variant.From(new QualifiedName($"sample-{key}")),
                BuiltInType.LocalizedText => Variant.From(new LocalizedText($"sample-{key}")),
                BuiltInType.StatusCode => Variant.From(StatusCodes.Good),
                BuiltInType.ExtensionObject when IsChemicalSubstance(device, variable.DataType) =>
                    Variant.From(new ExtensionObject(new ChemicalSubstance
                    {
                        PatDictionary = PatDictionaryEnum.CAS,
                        Label = new LocalizedText("fixture substance"),
                        Id = new LocalizedText("PADIM-TEST-001")
                    })),
                _ => throw Unsupported(variable, $"built-in type {builtInType}")
            };

            if (variable.ValueRank == ValueRanks.Scalar ||
                variable.ValueRank == ValueRanks.Any ||
                variable.ValueRank == ValueRanks.ScalarOrOneDimension)
            {
                return scalar;
            }
            if (variable.ValueRank == ValueRanks.OneDimension ||
                variable.ValueRank == ValueRanks.OneOrMoreDimensions)
            {
                return CreateArray(device, variable, key, builtInType);
            }
            throw Unsupported(variable, $"value rank {variable.ValueRank}");
        }

        /// <summary>Wraps the supported scalar sample in a strongly typed one-dimensional OPC UA array.</summary>
        private static Variant CreateArray(
            PadimDevice device,
            BaseVariableState variable,
            string key,
            BuiltInType builtInType)
        {
            return builtInType switch
            {
                BuiltInType.Boolean => Variant.From((ArrayOf<bool>)[true]),
                BuiltInType.SByte => Variant.From((ArrayOf<sbyte>)[-7]),
                BuiltInType.Byte => Variant.From((ArrayOf<byte>)[7]),
                BuiltInType.Int16 => Variant.From((ArrayOf<short>)[-17]),
                BuiltInType.UInt16 => Variant.From((ArrayOf<ushort>)[17]),
                BuiltInType.Int32 => Variant.From((ArrayOf<int>)[17]),
                BuiltInType.Enumeration => Variant.From((ArrayOf<EnumValue>)[new EnumValue(17)]),
                BuiltInType.UInt32 => Variant.From((ArrayOf<uint>)[17]),
                BuiltInType.Number => Variant.From((ArrayOf<float>)[21.5f]),
                BuiltInType.Integer => Variant.From((ArrayOf<int>)[17]),
                BuiltInType.UInteger => Variant.From((ArrayOf<uint>)[17]),
                BuiltInType.Int64 => Variant.From((ArrayOf<long>)[17]),
                BuiltInType.UInt64 => Variant.From((ArrayOf<ulong>)[17]),
                BuiltInType.Float => Variant.From((ArrayOf<float>)[21.5f]),
                BuiltInType.Double => Variant.From((ArrayOf<double>)[123.25d]),
                BuiltInType.String => Variant.From((ArrayOf<string>)[$"sample-{key}"]),
                BuiltInType.DateTime => Variant.From((ArrayOf<DateTimeUtc>)[
                    new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))]),
                BuiltInType.Guid => Variant.From((ArrayOf<Uuid>)[
                    new Uuid("2d711642-b726-4a2f-9f2d-2f157b6f3a01")]),
                BuiltInType.ByteString => Variant.From((ArrayOf<ByteString>)[
                    new ByteString(new byte[] { 7, 11, 13 })]),
                BuiltInType.NodeId => Variant.From((ArrayOf<NodeId>)[new NodeId($"sample-{key}", 0)]),
                BuiltInType.QualifiedName => Variant.From((ArrayOf<QualifiedName>)[
                    new QualifiedName($"sample-{key}")]),
                BuiltInType.LocalizedText => Variant.From((ArrayOf<LocalizedText>)[
                    new LocalizedText($"sample-{key}")]),
                BuiltInType.StatusCode => Variant.From((ArrayOf<StatusCode>)[StatusCodes.Good]),
                BuiltInType.ExtensionObject when IsChemicalSubstance(device, variable.DataType) =>
                    Variant.From((ArrayOf<ExtensionObject>)[new ExtensionObject(new ChemicalSubstance
                    {
                        PatDictionary = PatDictionaryEnum.CAS,
                        Label = new LocalizedText("fixture substance"),
                        Id = new LocalizedText("PADIM-TEST-001")
                    })]),
                _ => throw Unsupported(variable, $"array built-in type {builtInType} for {key}")
            };
        }

        /// <summary>Ensures a selected multi-state value has valid raw enum values and dictionary rows.</summary>
        private static void EnsureMultiStateMetadata(
            PadimDevice device,
            MultiStateValueDiscreteState enumeration)
        {
            if (enumeration.EnumValues == null)
            {
                throw Unsupported(enumeration, "missing EnumValues property");
            }
            if (enumeration.EnumValues.Value.Count == 0)
            {
                enumeration.EnumValues.Value =
                [
                    new EnumValueType { Value = 1, DisplayName = new LocalizedText("fixture-one") },
                    new EnumValueType { Value = 2, DisplayName = new LocalizedText("fixture-two") }
                ];
            }
            BaseVariableState dictionary = enumeration.FindChild(
                device.Context, new QualifiedName(Opc.Ua.BrowseNames.EnumDictionaryEntries)) as BaseVariableState ??
                throw Unsupported(enumeration, "missing EnumDictionaryEntries property");
            if (dictionary.WrappedValue.IsNull)
            {
                dictionary.WrappedValue = Variant.From((MatrixOf<NodeId>)new NodeId[,]
                {
                    { new("fixture-one", device.Manager.InstanceNamespaceIndex) },
                    { new("fixture-two", device.Manager.InstanceNamespaceIndex) }
                });
            }
        }

        /// <summary>Gets the display text associated with the selected raw multi-state enum value.</summary>
        private static string? GetExpectedEnumText(PadimDevice device, BaseVariableState variable, Variant value)
        {
            if (variable is not MultiStateValueDiscreteState enumeration)
            {
                return null;
            }
            EnsureMultiStateMetadata(device, enumeration);
            if (TypeInfo.GetBuiltInType(variable.DataType, device.Context.TypeTable) == BuiltInType.UInt32 &&
                value.TryGetValue(out uint raw))
            {
                return enumeration.EnumValues!.Value.ToList()
                    .Single(item => checked((uint)item.Value) == raw).DisplayName.Text;
            }
            if (value.TryGetValue(out int signed))
            {
                return enumeration.EnumValues!.Value.ToList()
                    .Single(item => checked((int)item.Value) == signed).DisplayName.Text;
            }
            throw Unsupported(variable, "multi-state sample did not retain its raw integer type");
        }

        /// <summary>Creates explicit typed array variants for wildcard or scalar declarations.</summary>
        private static Variant RankValue(BaseVariableState variable, Variant scalar, Variant array)
        {
            return variable.ValueRank switch
            {
                ValueRanks.Scalar or ValueRanks.Any or ValueRanks.ScalarOrOneDimension => scalar,
                ValueRanks.OneDimension or ValueRanks.OneOrMoreDimensions => array,
                _ => throw Unsupported(variable, $"value rank {variable.ValueRank}")
            };
        }

        /// <summary>Identifies the published structured PAT data type by namespace URI and numeric identifier.</summary>
        private static bool IsChemicalSubstance(PadimDevice device, NodeId dataType)
        {
            int padimNamespace = device.Context.NamespaceUris.GetIndex(Namespaces.PADIM);
            return dataType.NamespaceIndex == padimNamespace &&
                dataType.TryGetValue(out uint identifier) &&
                identifier == 1275;
        }

        /// <summary>Identifies the DI-defined device-health enumeration using its namespace URI.</summary>
        private static bool IsDeviceHealth(PadimDevice device, NodeId dataType)
        {
            int diNamespace = device.Context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);
            return dataType.NamespaceIndex == diNamespace &&
                dataType.TryGetValue(out uint identifier) &&
                identifier == 6244;
        }

        /// <summary>Creates a test failure instead of silently publishing a null or mismatched value.</summary>
        private static InvalidOperationException Unsupported(BaseVariableState variable, string detail)
        {
            return new InvalidOperationException(
                $"No deterministic PA-DIM facet sample exists for {variable.BrowseName} " +
                $"(DataType {variable.DataType}, ValueRank {variable.ValueRank}): {detail}.");
        }

        private readonly Mock<IPadimValueProvider> m_provider = new(MockBehavior.Strict);
        private readonly List<PadimPublishedSample> m_samples = [];
        private int m_nextKey;
    }

    /// <summary>The exact provider sample and server-side binding tracked by a PA-DIM fixture.</summary>
    internal sealed record PadimPublishedSample(
        PadimValueBinding Binding,
        string Key,
        NodeId NodeId,
        NodeId ParentId,
        QualifiedName BrowseName,
        NodeId TypeDefinitionId,
        DataValue Sample,
        string? ExpectedEnumText);
}
