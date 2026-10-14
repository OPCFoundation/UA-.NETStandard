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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodeSetNodesOnlyPump;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// With <c>ModelSourceGeneratorEmitNodeSetNodesOnly</c>, the predefined
    /// nodes generated for a NodeSet2 are exactly the nodes of the NodeSet2.
    /// Without it, a DeviceType subtype got a copy of every Mandatory
    /// InstanceDeclaration of DeviceType, each with a NodeId the NodeSet2
    /// never assigned.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class NodeSetNodesOnlyAddressSpaceTests
    {
        private const string kResource = "Opc.Ua.Di.Tests.Assets.NodeSetNodesOnlyPump.NodeSet2.xml";

        [Test]
        public void ExportedAddressSpaceHasTheNodesOfTheNodeSet()
        {
            UANodeSet input = ReadInput();
            UANodeSet exported = ExportGeneratedNodes();

            Assert.That(
                Nodes(exported),
                Is.EquivalentTo(Nodes(input)));
        }

        [Test]
        public void ExportedAddressSpaceHasTheReferencesOfTheNodeSet()
        {
            UANodeSet input = ReadInput();
            UANodeSet exported = ExportGeneratedNodes();

            Assert.That(
                References(exported),
                Is.EquivalentTo(References(input)));
        }

        [Test]
        public void InstancesCreatedAtRunTimeStillGetTheInheritedMandatoryChildren()
        {
            SystemContext context = CreateContext();

            PumpState pump = context.CreateInstanceOfPumpType(
                browseName: new QualifiedName("Pump2", 1));

            Assert.Multiple(() =>
            {
                Assert.That(pump.SerialNumber, Is.Not.Null);
                Assert.That(pump.RevisionCounter, Is.Not.Null);
                Assert.That(pump.RatedFlow, Is.Not.Null);
            });
        }

        private static UANodeSet ExportGeneratedNodes()
        {
            SystemContext context = CreateContext();
            var nodes = new NodeStateCollection();
            nodes.AddNodeSetNodesOnlyPump(context);

            using var stream = new MemoryStream();
            nodes.SaveAsNodeSet2(context, stream);
            stream.Position = 0;
            return UANodeSet.Read(stream)!;
        }

        private static SystemContext CreateContext()
        {
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.GetIndexOrAppend("http://test.org/UA/NodeSetNodesOnlyPump/");
            context.NamespaceUris.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            return context;
        }

        private static UANodeSet ReadInput()
        {
            using Stream stream = typeof(NodeSetNodesOnlyAddressSpaceTests).Assembly
                .GetManifestResourceStream(kResource)!;
            return UANodeSet.Read(stream)!;
        }

        /// <summary>
        /// Every node as its NodeClass, namespace qualified NodeId and
        /// namespace qualified BrowseName.
        /// </summary>
        private static string[] Nodes(UANodeSet nodeSet)
        {
            return [.. nodeSet.Items!.Select(node =>
                node.GetType().Name + " " +
                Normalize(nodeSet, node.NodeId!) + " " +
                NormalizeBrowseName(nodeSet, node.BrowseName!))];
        }

        /// <summary>
        /// Every reference, wherever it is declared, as its forward form:
        /// source, reference type and target, namespace qualified.
        /// </summary>
        private static string[] References(UANodeSet nodeSet)
        {
            var references = new HashSet<string>();
            foreach (UANode node in nodeSet.Items!)
            {
                string source = Normalize(nodeSet, node.NodeId!);
                foreach (Reference reference in node.References ?? [])
                {
                    string type = Normalize(nodeSet, reference.ReferenceType!);
                    string target = Normalize(nodeSet, reference.Value!);
                    references.Add(reference.IsForward
                        ? source + " " + type + " " + target
                        : target + " " + type + " " + source);
                }
            }
            return [.. references];
        }

        private static string Normalize(UANodeSet nodeSet, string nodeId)
        {
            NodeIdAlias? alias = nodeSet.Aliases?.FirstOrDefault(a => a.Alias == nodeId);
            var parsed = NodeId.Parse(alias?.Value ?? nodeId);
            return NamespaceUri(nodeSet, parsed.NamespaceIndex) + ";" +
                parsed.WithNamespaceIndex(0);
        }

        private static string NormalizeBrowseName(UANodeSet nodeSet, string browseName)
        {
            var parsed = QualifiedName.Parse(browseName);
            return NamespaceUri(nodeSet, parsed.NamespaceIndex) + ";" + parsed.Name;
        }

        private static string NamespaceUri(UANodeSet nodeSet, ushort namespaceIndex)
        {
            return namespaceIndex == 0
                ? global::Opc.Ua.Namespaces.OpcUa
                : nodeSet.NamespaceUris![namespaceIndex - 1];
        }
    }
}
