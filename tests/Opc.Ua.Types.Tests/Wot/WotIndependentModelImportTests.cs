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

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed partial class WotIndependentModelImportTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task IndependentReadableModelsNormalizeSourceNamespacesAsync(bool reverse)
        {
            WotDocumentSetEntry first = new("a", CreateModel("urn:test:a"));
            WotDocumentSetEntry second = new("b", CreateModel("urn:test:b"));
            using var documents = new WotDocumentSet(
                "a", reverse ? [second, first] : [first, second]);
            byte[][] original = documents.Entries.ToList()
                .Select(entry => entry.Document.Utf8Json.ToArray()).ToArray();
            var options = new WotNodeSetConverterOptions
            {
                DocumentSetMode = WotDocumentSetMode.IndependentReadableModels
            };

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, options).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.NamespaceUris, Is.EqualTo(s_namespaceUris));
            Assert.That(result.Value.Models!.Select(model => model.ModelUri),
                Is.EqualTo(s_namespaceUris));
            Assert.That(result.Value.Items!.Select(node => node.NodeId),
                Is.EquivalentTo(s_rootNodeIds));
            Assert.That(result.Value.Items!.Single(node => node.NodeId == "ns=1;i=1").BrowseName,
                Is.EqualTo("1:Root"));
            Assert.That(result.Value.Items!.Single(node => node.NodeId == "ns=2;i=1").BrowseName,
                Is.EqualTo("2:Root"));
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                Assert.That(documents.Entries[index].Document.Utf8Json.ToArray(), Is.EqualTo(original[index]));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndependentPartitionsNormalizeIdentitiesWithoutMutatingInputs(bool reverse)
        {
            WotDocumentSetEntry first = new("a", CreateModel("urn:test:a"));
            WotDocumentSetEntry second = new("b", CreateModel("urn:test:b"));
            using var documents = new WotDocumentSet(
                "a", reverse ? [second, first] : [first, second]);
            UANodeSet a = CreatePartition("urn:test:a");
            UANodeSet b = CreatePartition("urn:test:b");
            byte[] originalA = Serialize(a);
            byte[] originalB = Serialize(b);
            var options = new WotNodeSetConverterOptions
            {
                DocumentSetMode = WotDocumentSetMode.IndependentReadableModels
            };

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, reverse ? [b, a] : [a, b], options);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.NamespaceUris, Is.EqualTo(s_namespaceUris));
            Assert.That(result.Value.Items!.Select(node => (node.NodeId, node.BrowseName)),
                Is.EquivalentTo(s_roots));
            Assert.That(Serialize(a), Is.EqualTo(originalA));
            Assert.That(Serialize(b), Is.EqualTo(originalB));
        }

        [Test]
        public void IndependentPartitionsRebaseEveryIdentityField()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet a = CreatePartition("urn:test:a");
            UANodeSet b = CreatePartition("urn:test:b");
            b.NamespaceUris = ["urn:test:b", "urn:test:a"];
            b.Aliases =
            [
                new NodeIdAlias { Alias = "Root", Value = "ns=1;i=1" },
                new NodeIdAlias { Alias = "OwnType", Value = "ns=1;i=20" },
                new NodeIdAlias { Alias = "OtherType", Value = "ns=2;i=30" },
                new NodeIdAlias { Alias = "OwnReference", Value = "ns=1;i=40" }
            ];
            b.Models![0].RolePermissions = [new RolePermission { Value = "ns=2;i=7", Permissions = 5 }];
            b.Items =
            [
                b.Items![0],
                new UAVariable
                {
                    NodeId = "ns=1;s=literal;ns=1;i=23",
                    BrowseName = "1:Value",
                    ParentNodeId = "Root",
                    DataType = "OwnType",
                    SymbolicName = "ns=1;i=23",
                    RolePermissions = [new RolePermission { Value = "ns=1;i=7", Permissions = 3 }],
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "OwnReference",
                            IsForward = false,
                            Value = "ns=2;i=99"
                        }
                    ]
                },
                new UAMethod
                {
                    NodeId = "ns=1;i=3",
                    BrowseName = "2:Run",
                    ParentNodeId = "Root",
                    MethodDeclarationId = "ns=2;i=4"
                },
                new UADataType
                {
                    NodeId = "OwnType",
                    BrowseName = "1:Sample",
                    Definition = new Export.DataTypeDefinition
                    {
                        Name = "1:Sample",
                        BaseType = "2:Base",
                        Field =
                        [
                            new DataTypeField
                            {
                                Name = "ns=1;Field",
                                DataType = "OtherType",
                                ValueRank = 1,
                                ArrayDimensions = "3"
                            }
                        ]
                    }
                },
                new UAVariableType
                {
                    NodeId = "ns=1;i=5",
                    BrowseName = "1:ValueType",
                    DataType = "OwnType"
                }
            ];
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [a, b], new WotNodeSetConverterOptions
                {
                    DocumentSetMode = WotDocumentSetMode.IndependentReadableModels
                });

            Assert.That(result.Success, Is.True, Describe(result));
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.NodeId, Is.EqualTo("ns=2;s=literal;ns=1;i=23"));
            Assert.That(variable.BrowseName, Is.EqualTo("2:Value"));
            Assert.That(variable.ParentNodeId, Is.EqualTo("ns=2;i=1"));
            Assert.That(variable.DataType, Is.EqualTo("ns=2;i=20"));
            Assert.That(variable.SymbolicName, Is.EqualTo("ns=1;i=23"));
            Assert.That(variable.RolePermissions![0].Value, Is.EqualTo("ns=2;i=7"));
            Assert.That(variable.RolePermissions[0].Permissions, Is.EqualTo(3));
            Assert.That(variable.References![0].ReferenceType, Is.EqualTo("ns=2;i=40"));
            Assert.That(variable.References[0].Value, Is.EqualTo("ns=1;i=99"));
            Assert.That(variable.References[0].IsForward, Is.False);
            UAMethod method = result.Value.Items!.OfType<UAMethod>().Single();
            Assert.That(method.NodeId, Is.EqualTo("ns=2;i=3"));
            Assert.That(method.BrowseName, Is.EqualTo("1:Run"));
            Assert.That(method.ParentNodeId, Is.EqualTo("ns=2;i=1"));
            Assert.That(method.MethodDeclarationId, Is.EqualTo("ns=1;i=4"));
            UADataType dataType = result.Value.Items!.OfType<UADataType>().Single();
            Assert.That(dataType.NodeId, Is.EqualTo("ns=2;i=20"));
            Assert.That(dataType.Definition!.Name, Is.EqualTo("2:Sample"));
            Assert.That(dataType.Definition.BaseType, Is.EqualTo("1:Base"));
            Assert.That(dataType.Definition.Field![0].Name, Is.EqualTo("ns=1;Field"));
            Assert.That(dataType.Definition.Field[0].DataType, Is.EqualTo("ns=1;i=30"));
            Assert.That(dataType.Definition.Field[0].ValueRank, Is.EqualTo(1));
            Assert.That(dataType.Definition.Field[0].ArrayDimensions, Is.EqualTo("3"));
            Assert.That(result.Value.Items!.OfType<UAVariableType>().Single().DataType, Is.EqualTo("ns=2;i=20"));
            Assert.That(result.Value.Models!.Single(model => model.ModelUri == "urn:test:b")
                .RolePermissions![0].Value, Is.EqualTo("ns=1;i=7"));
            Assert.That(result.Value.Aliases!.Select(alias => alias.Value),
                Does.Contain("ns=1;i=1").And.Contain("ns=2;i=1").And.Contain("ns=2;i=20"));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [Test]
        public async Task DefaultReconstructionRejectsIndependentTablesAsync()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(new WotNodeSetConverterOptions().DocumentSetMode,
                Is.EqualTo(WotDocumentSetMode.PartitionReconstruction));
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
        }

        [Test]
        public void InvalidDocumentSetModeIsRejected()
        {
            var options = new WotNodeSetConverterOptions { DocumentSetMode = (WotDocumentSetMode)(-1) };

            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo("DocumentSetMode"));
        }

        private static UANodeSet CreatePartition(string namespaceUri)
        {
            return new UANodeSet
            {
                NamespaceUris = [namespaceUri],
                Models = [new ModelTableEntry { ModelUri = namespaceUri }],
                Aliases = [new NodeIdAlias { Alias = "Root", Value = "ns=1;i=1" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "Root",
                        BrowseName = "1:Root",
                        DisplayName = [new Export.LocalizedText { Value = "Root" }]
                    }
                ]
            };
        }

        private static byte[] Serialize(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static WotDocument CreateModel(string namespaceUri, uint identifier = 1)
        {
            var root = new JsonObject
            {
                ["@context"] = new JsonObject
                {
                    ["uav"] = WotNodeSetConverter.VocabularyNamespace,
                    ["ns1"] = namespaceUri
                },
                ["@type"] = "tm:ThingModel",
                ["title"] = "Root",
                ["uav:id"] = "nsu=" + namespaceUri + ";i=" + identifier.ToString(CultureInfo.InvariantCulture),
                ["uav:browseName"] = "ns1:Root"
            };
            return WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        private static string Describe<T>(WotConversionResult<T> result)
            where T : class
        {
            return string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }

        private static readonly string[] s_namespaceUris = ["urn:test:a", "urn:test:b"];
        private static readonly string[] s_rootNodeIds = ["ns=1;i=1", "ns=2;i=1"];
        private static readonly (string NodeId, string BrowseName)[] s_roots =
            [("ns=1;i=1", "1:Root"), ("ns=2;i=1", "2:Root")];
    }
}
