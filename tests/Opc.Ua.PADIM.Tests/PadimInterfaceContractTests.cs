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
                    signal, builder => SelectPublishedMembers(device, builder, conditions));
                AssertInterfaces(device, group, conditions);
            }
            List<XElement> calibrations = GroupDeclarations(signal.TypeDefinitionId, "<SignalCalibrationIdentifier>");
            if (signal is AnalogSignalObjectState analog)
            {
                Assert.That(calibrations, Is.Not.Empty);
                BaseObjectState group = await device.AddCalibrationAsync(
                    analog, Name("Calibration"), builder => SelectPublishedMembers(device, builder, calibrations));
                AssertInterfaces(device, group, calibrations);
            }
            else
            {
                Assert.That(calibrations, Is.Empty);
            }
        }

        /// <summary>
        /// Selects every published device-condition member for each analyser family.
        /// </summary>
        [Test]
        public async Task EveryDeviceConditionInterfaceRetainsItsPublishedMembers([Values] PadimDeviceKind kind)
        {
            var builder = await m_manager.CreatePadimDeviceAsync(Name($"DeviceInterfaces{kind}"), kind);
            PadimDevice device = builder.BindPadim();
            List<XElement> declarations = GroupDeclarations(device.State.TypeDefinitionId, "DeviceConditionSet");
            Assert.That(declarations, Is.Not.Empty);
            BaseObjectState group = await device.ConfigureDeviceConditionsAsync(
                selected => SelectPublishedMembers(device, selected, declarations));
            AssertInterfaces(device, group, declarations);
        }

        /// <summary>
        /// Configures members from the original XML rather than from runtime factory output.
        /// </summary>
        private void SelectPublishedMembers(
            PadimDevice device,
            PadimGroupBuilder group,
            List<XElement> declarations)
        {
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            foreach (KeyValuePair<string, List<XElement>> member in PublishedChildren(declarations))
            {
                XElement source = member.Value[member.Value.Count - 1];
                QualifiedName name = PublishedName(source);
                if (source.Name.LocalName == "UAVariable")
                {
                    PadimValueBinding binding = group.Bind(name, $"{group.Node.BrowseName}/{name}", provider.Object);
                    BaseVariableState actual = binding.Variable;
                    string dataType = source.Attribute("DataType")?.Value ?? "i=24";
                    int rank = int.Parse(source.Attribute("ValueRank")?.Value ?? "-1", CultureInfo.InvariantCulture);
                    Assert.Multiple(() =>
                    {
                        Assert.That(actual.DataType, Is.EqualTo(PublishedNodeId(dataType)), source.ToString());
                        Assert.That(actual.ValueRank, Is.EqualTo(rank), source.ToString());
                        Assert.That(actual.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                        Assert.That(actual.NodeId.NamespaceIndex, Is.EqualTo(m_manager.InstanceNamespaceIndex));
                        Assert.That(actual.BrowseName, Is.EqualTo(name));
                    });
                    foreach (XElement reference in References(source, "HasDictionaryEntry"))
                    {
                        NodeId target = PublishedNodeId(reference.Value);
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
                    QualifiedName instanceName = placeholder ? Name("FixtureItem") : QualifiedName.Null;
                    group.Group(name, nested => SelectPublishedMembers(device, nested, member.Value), instanceName);
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
                NodeId target = PublishedNodeId(expected.Value);
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
        private List<XElement> GroupDeclarations(NodeId typeId, string name)
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
        private NodeId PublishedNodeId(string text)
        {
            if (s_aliases.TryGetValue(text, out string? expanded))
            {
                text = expanded;
            }
            NodeId id = NodeId.Parse(text);
            return id.WithNamespaceIndex(PublishedNamespace(id.NamespaceIndex));
        }

        /// <summary>
        /// Maps a source BrowseName into the live namespace table.
        /// </summary>
        private QualifiedName PublishedName(XElement node)
        {
            QualifiedName name = QualifiedName.Parse(node.Attribute("BrowseName")!.Value);
            return name.WithNamespaceIndex(PublishedNamespace(name.NamespaceIndex));
        }

        /// <summary>
        /// Resolves the source namespace URI rather than reusing its numeric index.
        /// </summary>
        private ushort PublishedNamespace(ushort index)
        {
            if (index == 0)
            {
                return 0;
            }
            string uri = s_publishedRoot.Element(s_xml + "NamespaceUris")!.Elements(s_xml + "Uri")
                .ElementAt(index - 1).Value;
            int mapped = m_manager.SystemContext.NamespaceUris.GetIndex(uri);
            Assert.That(mapped, Is.GreaterThanOrEqualTo(0), $"Missing published namespace {uri}.");
            return (ushort)mapped;
        }

        /// <summary>
        /// Selects source references by their declared direction.
        /// </summary>
        private static IEnumerable<XElement> References(XElement node, string type, bool inverse = false)
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
}
