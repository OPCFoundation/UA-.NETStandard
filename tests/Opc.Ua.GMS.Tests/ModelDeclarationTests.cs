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
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Compares generated public model behavior to the independently pinned normative XML.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class ModelDeclarationTests
    {
        /// <summary>
        /// All 273 canonical declarations retain their identity, class and exact browse-name namespace.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoaderPreservesEveryCanonicalIdentityAndQualifiedBrowseName(bool reverseNamespaces)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: reverseNamespaces);
            Dictionary<NodeId, NodeState> nodes = LoadNodes(context);
            XElement root = ReadModel();
            string[] namespaces = ReadNamespaces(root);
            XElement[] declarations = [.. root.Elements().Where(node => node.Attribute("NodeId") != null)];
            NodeId[] expectedIds =
            [
                .. declarations.Select(node => Resolve((string)node.Attribute("NodeId")!, namespaces, context))
            ];
            NodeId[] numericIds =
            [
                .. nodes.Keys.Where(id => id.NamespaceIndex == context.NamespaceUris.GetIndex(GmsModel.NamespaceUri) &&
                    id.TryGetValue(out uint _))
            ];
            Assert.That(numericIds, Is.EquivalentTo(expectedIds));
            Assert.That(numericIds, Has.Length.EqualTo(273));

            foreach (XElement declaration in declarations)
            {
                NodeId id = Resolve((string)declaration.Attribute("NodeId")!, namespaces, context);
                NodeState node = nodes[id];
                QualifiedName browseName = ResolveBrowseName(
                    (string)declaration.Attribute("BrowseName")!, namespaces, context);
                Assert.Multiple(() =>
                {
                    Assert.That(node.NodeClass.ToString(), Is.EqualTo(declaration.Name.LocalName[2..]),
                        $"NodeClass of {id}");
                    Assert.That(node.BrowseName, Is.EqualTo(browseName), $"BrowseName of {id}");
                });
            }
        }

        /// <summary>
        /// Browsing every declaration preserves each published reference, including direction and external targets.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoaderPreservesAllCanonicalReferences(bool reverseNamespaces)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: reverseNamespaces);
            Dictionary<NodeId, NodeState> nodes = LoadNodes(context);
            XElement root = ReadModel();
            string[] namespaces = ReadNamespaces(root);
            Dictionary<string, string> aliases = root.Element(s_ns + "Aliases")!.Elements()
                .ToDictionary(alias => (string)alias.Attribute("Alias")!, alias => alias.Value);
            var missing = new List<string>();

            foreach (XElement declaration in root.Elements().Where(node => node.Attribute("NodeId") != null))
            {
                NodeId id = Resolve((string)declaration.Attribute("NodeId")!, namespaces, context);
                Assert.That(nodes.TryGetValue(id, out NodeState? node), Is.True, $"Missing declaration {id}");
                using INodeBrowser browser = node!.CreateBrowser(
                    context, null, NodeId.Null, false, BrowseDirection.Both, QualifiedName.Null, null, true);
                var actual = new HashSet<(NodeId ReferenceType, bool Inverse, NodeId Target)>();
                for (IReference? reference = browser.Next(); reference != null; reference = browser.Next())
                {
                    actual.Add((reference.ReferenceTypeId, reference.IsInverse,
                        ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris)));
                }
                foreach (XElement reference in declaration.Element(s_ns + "References")!.Elements())
                {
                    string referenceType = (string)reference.Attribute("ReferenceType")!;
                    NodeId typeId = Resolve(aliases.TryGetValue(referenceType, out string? alias)
                        ? alias : referenceType, namespaces, context);
                    NodeId target = Resolve(reference.Value, namespaces, context);
                    bool inverse = (string?)reference.Attribute("IsForward") == "false";
                    if (!actual.Contains((typeId, inverse, target)))
                    {
                        missing.Add($"{id}: {typeId} inverse={inverse} target={target}");
                    }
                }
            }
            Assert.That(missing, Is.Empty, "All published references must be browseable without changing direction.");
        }

        /// <summary>
        /// Variable declarations keep their wire data type and rank, including abstract Number values.
        /// </summary>
        [Test]
        public void LoaderPreservesAllVariableDataTypesAndRanks()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            Dictionary<NodeId, NodeState> nodes = LoadNodes(context);
            XElement root = ReadModel();
            string[] namespaces = ReadNamespaces(root);
            Dictionary<string, string> aliases = root.Element(s_ns + "Aliases")!.Elements()
                .ToDictionary(alias => (string)alias.Attribute("Alias")!, alias => alias.Value);

            foreach (XElement declaration in root.Elements().Where(node =>
                node.Name == s_ns + "UAVariable" || node.Name == s_ns + "UAVariableType"))
            {
                NodeId id = Resolve((string)declaration.Attribute("NodeId")!, namespaces, context);
                NodeState node = nodes[id];
                string originalType = (string?)declaration.Attribute("DataType") ?? "i=24";
                NodeId typeId = Resolve(aliases.TryGetValue(originalType, out string? alias)
                    ? alias : originalType, namespaces, context);
                (NodeId actualType, int actualRank) = node switch
                {
                    BaseVariableState variable => (variable.DataType, variable.ValueRank),
                    BaseVariableTypeState variableType => (variableType.DataType, variableType.ValueRank),
                    _ => throw new InvalidOperationException($"Expected a variable at {id}.")
                };
                Assert.Multiple(() =>
                {
                    Assert.That(actualType, Is.EqualTo(typeId), $"DataType of {id}");
                    Assert.That(actualRank, Is.EqualTo((int?)declaration.Attribute("ValueRank") ?? ValueRanks.Scalar),
                        $"ValueRank of {id}");
                });
            }
        }

        /// <summary>
        /// Redeclared GMS methods retain their original Machinery Result or core declaration identity.
        /// </summary>
        [TestCase(7001u, 7008u, "http://opcfoundation.org/UA/Machinery/Result/")]
        [TestCase(7002u, 7005u, "http://opcfoundation.org/UA/Machinery/Result/")]
        [TestCase(7003u, 7006u, "http://opcfoundation.org/UA/Machinery/Result/")]
        [TestCase(7004u, 7007u, "http://opcfoundation.org/UA/Machinery/Result/")]
        [TestCase(7005u, 15751u, "http://opcfoundation.org/UA/")]
        [TestCase(7006u, 7001u, "http://opcfoundation.org/UA/Machinery/Result/")]
        [TestCase(7007u, 15749u, "http://opcfoundation.org/UA/")]
        public void MethodDeclarationsRetainInheritedIdentity(uint identifier, uint declarationId, string namespaceUri)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            Dictionary<NodeId, NodeState> nodes = LoadNodes(context);
            var method = (MethodState)nodes[NodeId.Create(identifier, GmsModel.NamespaceUri, context.NamespaceUris)];
            Assert.That(method.MethodDeclarationId,
                Is.EqualTo(NodeId.Create(declarationId, namespaceUri, context.NamespaceUris)));
        }

        /// <summary>
        /// All seven methods preserve argument names, order, data types and ranks from the released NodeSet.
        /// </summary>
        [Test]
        public void MethodArgumentsMatchTheReleasedContracts()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            Dictionary<NodeId, NodeState> nodes = LoadNodes(context);
            XElement root = ReadModel();
            string[] namespaces = ReadNamespaces(root);
            foreach (XElement declaration in root.Elements(s_ns + "UAVariable")
                .Where(node => (string?)node.Attribute("DataType") == "Argument"))
            {
                NodeId id = Resolve((string)declaration.Attribute("NodeId")!, namespaces, context);
                var property = (PropertyState<ArrayOf<Argument>>)nodes[id];
                XElement[] expected = [.. declaration.Descendants(s_uax + "Argument")];
                Assert.That(property.Value.Count, Is.EqualTo(expected.Length), $"Argument count of {id}");
                for (int index = 0; index < expected.Length; index++)
                {
                    XElement argument = expected[index];
                    Argument actual = property.Value[index];
                    Assert.Multiple(() =>
                    {
                        Assert.That(actual.Name, Is.EqualTo(argument.Element(s_uax + "Name")!.Value),
                            $"Argument {index} name of {id}");
                        Assert.That(actual.DataType,
                            Is.EqualTo(Resolve(argument.Element(s_uax + "DataType")!
                                .Element(s_uax + "Identifier")!.Value, namespaces, context)),
                            $"Argument {index} data type of {id}");
                        Assert.That(actual.ValueRank,
                            Is.EqualTo((int)argument.Element(s_uax + "ValueRank")!),
                            $"Argument {index} rank of {id}");
                    });
                }
            }
        }

        private static Dictionary<NodeId, NodeState> LoadNodes(SystemContext context)
        {
            return ModelTestContext.IndexNodes(context, new NodeStateCollection().AddGmsTypeSystem(context));
        }

        private static XElement ReadModel()
        {
            return XDocument.Load(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Model", "opc.ua.gms.nodeset2.xml")).Root!;
        }

        private static string[] ReadNamespaces(XElement root)
        {
            return
            [
                Opc.Ua.Namespaces.OpcUa,
                .. root.Element(s_ns + "NamespaceUris")!.Elements().Select(uri => uri.Value)
            ];
        }

        private static NodeId Resolve(string value, string[] namespaces, SystemContext context)
        {
            var original = NodeId.Parse(value);
            Assert.That(original.TryGetValue(out uint identifier), Is.True,
                $"Expected a numeric publication id: {value}");
            return NodeId.Create(identifier, namespaces[original.NamespaceIndex], context.NamespaceUris);
        }

        private static QualifiedName ResolveBrowseName(
            string value,
            string[] namespaces,
            SystemContext context)
        {
            int colon = value.AsSpan().IndexOf(':');
            int index = colon < 0 ? 0 : int.Parse(value[..colon], CultureInfo.InvariantCulture);
            string name = colon < 0 ? value : value[(colon + 1)..];
            return new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend(namespaces[index]));
        }

        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
        private static readonly XNamespace s_uax = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
}
