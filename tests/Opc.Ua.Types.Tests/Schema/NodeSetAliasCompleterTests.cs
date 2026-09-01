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
 *
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

using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Types.Tests.Schema
{
    /// <summary>
    /// Completing a NodeSet2 document's <c>&lt;Aliases&gt;</c> table is what
    /// makes a document that writes readable names importable, and it is a
    /// property of NodeSet2 rather than of any producer that writes one.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public class NodeSetAliasCompleterTests
    {
        /// <summary>
        /// Every standard ReferenceType and DataType name the document uses is
        /// declared, and nothing else is.
        /// </summary>
        [Test]
        public void CompletionDeclaresTheStandardNamesTheDocumentUses()
        {
            UANodeSet nodeSet = CreateNodeSetUsingStandardNames();

            NodeSetAliasCompleter.Complete(nodeSet);

            Assert.That(
                nodeSet.Aliases!.Select(alias => alias.Alias),
                Is.EqualTo(new[] { "Double", "HasComponent", "HasSubtype", "HasTypeDefinition" }),
                "Declarations are appended in ascending ordinal order of the alias.");
            Assert.That(
                nodeSet.Aliases!.Select(alias => alias.Value),
                Is.EqualTo(new[] { "i=11", "i=47", "i=45", "i=40" }));
        }

        /// <summary>
        /// A second pass adds nothing, which is what keeps a byte-exact
        /// restore byte-exact.
        /// </summary>
        [Test]
        public void CompletionIsIdempotent()
        {
            UANodeSet nodeSet = CreateNodeSetUsingStandardNames();

            NodeSetAliasCompleter.Complete(nodeSet);
            NodeIdAlias[] afterFirstPass = nodeSet.Aliases!;
            NodeSetAliasCompleter.Complete(nodeSet);

            Assert.That(nodeSet.Aliases, Is.SameAs(afterFirstPass));
            Assert.That(nodeSet.Aliases!, Has.Length.EqualTo(4));
        }

        /// <summary>
        /// Two node sets with the same content are completed the same way, so
        /// the result depends on content and never on enumeration order.
        /// </summary>
        [Test]
        public void CompletionIsDeterministic()
        {
            UANodeSet first = CreateNodeSetUsingStandardNames();
            UANodeSet second = CreateNodeSetUsingStandardNames();

            NodeSetAliasCompleter.Complete(first);
            NodeSetAliasCompleter.Complete(second);

            Assert.That(
                second.Aliases!.Select(alias => $"{alias.Alias}={alias.Value}"),
                Is.EqualTo(first.Aliases!.Select(alias => $"{alias.Alias}={alias.Value}")));
        }

        /// <summary>
        /// What the document brought keeps its place; only the missing
        /// declarations are appended.
        /// </summary>
        [Test]
        public void CompletionKeepsTheDeclarationsTheDocumentBrought()
        {
            UANodeSet nodeSet = CreateNodeSetUsingStandardNames();
            nodeSet.Aliases =
            [
                new NodeIdAlias { Alias = "MachineTypeAlias", Value = "ns=1;i=1001" },
                new NodeIdAlias { Alias = "HasComponent", Value = "i=47" }
            ];

            NodeSetAliasCompleter.Complete(nodeSet);

            Assert.That(
                nodeSet.Aliases!.Select(alias => alias.Alias),
                Is.EqualTo(new[]
                {
                    "MachineTypeAlias",
                    "HasComponent",
                    "Double",
                    "HasSubtype",
                    "HasTypeDefinition"
                }));
        }

        /// <summary>
        /// A completed node set is one a Server can load, which is the point
        /// of completing it.
        /// </summary>
        [Test]
        public void ACompletedNodeSetCanBeImported()
        {
            UANodeSet nodeSet = CreateNodeSetUsingStandardNames();

            NodeSetAliasCompleter.Complete(nodeSet);

            Assert.DoesNotThrow(() => Import(Reread(nodeSet)));
        }

        /// <summary>
        /// A name this library cannot resolve is left exactly as it was, so a
        /// vendor alias the document never declared still fails the import
        /// with the message that names it rather than being quietly declared
        /// as something it is not.
        /// </summary>
        [Test]
        public void AVendorNameIsLeftUndeclaredAndStillFailsTheImport()
        {
            UANodeSet nodeSet = CreateNodeSetUsingStandardNames();
            nodeSet.Items!.OfType<UAObjectType>().Single().References =
            [
                new Reference
                {
                    ReferenceType = "VendorSpecificReference",
                    IsForward = true,
                    Value = "i=58"
                }
            ];

            NodeSetAliasCompleter.Complete(nodeSet);

            Assert.That(
                nodeSet.Aliases!.Select(alias => alias.Alias),
                Has.No.Member("VendorSpecificReference"));
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => Import(Reread(nodeSet)))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(exception.Message, Does.Contain("VendorSpecificReference"));
        }

        /// <summary>
        /// An empty node set is returned unchanged rather than gaining an
        /// empty table.
        /// </summary>
        [Test]
        public void CompletionLeavesANodeSetWithoutNodesAlone()
        {
            var nodeSet = new UANodeSet();

            Assert.That(NodeSetAliasCompleter.Complete(nodeSet), Is.SameAs(nodeSet));
            Assert.That(nodeSet.Aliases, Is.Null);
            Assert.That(NodeSetAliasCompleter.Complete(null), Is.Null);
        }

        private static UANodeSet CreateNodeSetUsingStandardNames()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:aliases"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:aliases" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:MachineType",
                        DisplayName = [new Export.LocalizedText { Value = "MachineType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=6001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:Speed",
                        DisplayName = [new Export.LocalizedText { Value = "Speed" }],
                        DataType = "Double",
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasTypeDefinition",
                                IsForward = true,
                                Value = "i=63"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// Serializes and re-reads a node set, which is what a document that
        /// is handed to a Server goes through.
        /// </summary>
        private static UANodeSet Reread(UANodeSet nodeSet)
        {
            using var buffer = new MemoryStream();
            nodeSet.Write(buffer);
            using var source = new MemoryStream(buffer.ToArray(), writable: false);
            return UANodeSet.Read(source)!;
        }

        private static NodeStateCollection Import(UANodeSet nodeSet)
        {
            var namespaces = new NamespaceTable();
            foreach (string namespaceUri in nodeSet.NamespaceUris ?? [])
            {
                namespaces.GetIndexOrAppend(namespaceUri);
            }
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();
            nodeSet.Import(context, nodes);
            return nodes;
        }
    }
}
