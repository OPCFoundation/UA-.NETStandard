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
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.LADS.Tests.Model
{
    /// <summary>
    /// Pins the released LADS publication and the generated identifiers independently of each other.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    public sealed class LadsModelAssetTests
    {
        /// <summary>
        /// The vendored files are byte-identical to the pinned UA-Nodeset revision.
        /// </summary>
        [TestCase("Opc.Ua.LADS.NodeSet2.xml",
            "15FA32B4952F12A2D696FDDD2466A4C764AFDAC2125D198223547D787AAB6ADB")]
        [TestCase("Opc.Ua.LADS.NodeIds.csv",
            "836D55A7716C4852D1E94390BBF6996551FCC31381EBD7166D1D9A2007E4E3B0")]
        public void PublishedFilesMatchPinnedHashes(string file, string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(LadsTestContext.ModelPath(file));
#if NET8_0_OR_GREATER
            string actualHash = CoreUtils.ToHexString(SHA256.HashData(bytes));
#else
            using var hash = SHA256.Create();
            string actualHash = CoreUtils.ToHexString(hash.ComputeHash(bytes));
#endif
            using var provenance = JsonDocument.Parse(
                File.ReadAllText(LadsTestContext.ModelPath("provenance.json")));
            JsonElement source = provenance.RootElement.GetProperty("sources").EnumerateArray()
                .Single(entry => entry.GetProperty("file").GetString() == file);
            Assert.Multiple(() =>
            {
                Assert.That(actualHash, Is.EqualTo(expectedHash).IgnoreCase);
                Assert.That(source.GetProperty("sha256").GetString(), Is.EqualTo(expectedHash).IgnoreCase);
                Assert.That(provenance.RootElement.GetProperty("revision").GetString(),
                    Is.EqualTo(LadsModel.SourceCommit));
                Assert.That(source.GetProperty("url").GetString(), Is.EqualTo(
                    "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/" +
                    $"{LadsModel.SourceCommit}/LADS/{file}"));
            });
            if (file.EndsWith(".xml", StringComparison.Ordinal))
            {
                Assert.That(actualHash, Is.EqualTo(LadsModel.NodeSetSha256).IgnoreCase);
            }
        }

        /// <summary>
        /// The model identity and the lower-bound dependencies are those of the publication.
        /// </summary>
        [Test]
        public void ReleasedModelIdentityAndRequiredModelsRemainUnchanged()
        {
            XElement model = LadsTestContext.ReadNodeSet()
                .Element(LadsTestContext.NodeSetNamespace + "Models")!.Elements().Single();
            string[] requirements =
            [
                .. model.Elements(LadsTestContext.NodeSetNamespace + "RequiredModel").Select(dependency =>
                    $"{(string?)dependency.Attribute("ModelUri")}|{(string?)dependency.Attribute("Version")}|" +
                    $"{(string?)dependency.Attribute("PublicationDate")}")
            ];
            Assert.Multiple(() =>
            {
                Assert.That((string?)model.Attribute("ModelUri"), Is.EqualTo(LadsModel.NamespaceUri));
                Assert.That((string?)model.Attribute("Version"), Is.EqualTo(LadsModel.Version));
                Assert.That((string?)model.Attribute("PublicationDate"), Is.EqualTo("2023-11-30T00:00:00Z"));
                Assert.That(requirements, Is.EquivalentTo(s_requiredModels));
                Assert.That(Namespaces.LADS, Is.EqualTo(LadsModel.NamespaceUri));
                Assert.That(LadsModel.GetNamespaceUris().ToArray(), Is.EqualTo(LadsTestContext.ExpectedNamespaceUris));
            });
        }

        /// <summary>
        /// The published table agrees with the NodeSet on every row and the node class histogram is pinned.
        /// </summary>
        [Test]
        public void PublishedTableAgreesWithNodeSet()
        {
            Dictionary<uint, string> declarations = LadsTestContext.ReadDeclarations();
            Dictionary<uint, (string Symbol, string NodeClass)> published = ReadIdentifiers("Opc.Ua.LADS.NodeIds.csv");
            Dictionary<uint, (string Symbol, string NodeClass)> derived =
                ReadIdentifiers("Opc.Ua.LADS.Derived.NodeIds.csv");
            Assert.Multiple(() =>
            {
                Assert.That(declarations, Has.Count.EqualTo(650));
                Assert.That(published, Has.Count.EqualTo(650));
                Assert.That(derived.Keys, Is.EquivalentTo(published.Keys));
                foreach (KeyValuePair<uint, (string Symbol, string NodeClass)> row in published)
                {
                    Assert.That(declarations.TryGetValue(row.Key, out string? nodeClass), Is.True,
                        $"Published row {row.Key} is not declared.");
                    Assert.That(nodeClass, Is.EqualTo(row.Value.NodeClass), $"NodeClass of {row.Key}");
                    Assert.That(derived[row.Key].NodeClass, Is.EqualTo(row.Value.NodeClass));
                }
                Assert.That(declarations.Values.GroupBy(value => value).ToDictionary(
                    group => group.Key,
                    group => group.Count()), Is.EquivalentTo(new Dictionary<string, int>
                    {
                        ["DataType"] = 3,
                        ["Variable"] = 374,
                        ["ObjectType"] = 53,
                        ["Object"] = 160,
                        ["Method"] = 60
                    }));
            });
        }

        /// <summary>
        /// Every generated identifier constant names a NodeSet declaration of the matching node class
        /// and all types and methods have a constant.
        /// </summary>
        [Test]
        public void GeneratedIdentifiersMatchTheNodeSetAndTheIdentifierTable()
        {
            Dictionary<uint, string> declarations = LadsTestContext.ReadDeclarations();
            Dictionary<uint, (string Symbol, string NodeClass)> derived =
                ReadIdentifiers("Opc.Ua.LADS.Derived.NodeIds.csv");
            var generated = new Dictionary<string, Dictionary<string, uint>>
            {
                ["DataType"] = ReadConstants(typeof(DataTypes)),
                ["Method"] = ReadConstants(typeof(Methods)),
                ["Object"] = ReadConstants(typeof(Objects)),
                ["ObjectType"] = ReadConstants(typeof(ObjectTypes)),
                ["Variable"] = ReadConstants(typeof(Variables))
            };
            int matched = 0;
            Assert.Multiple(() =>
            {
                foreach (KeyValuePair<string, Dictionary<string, uint>> nodeClass in generated)
                {
                    foreach (KeyValuePair<string, uint> constant in nodeClass.Value)
                    {
                        Assert.That(declarations.TryGetValue(constant.Value, out string? declared), Is.True,
                            $"{nodeClass.Key}s.{constant.Key} = {constant.Value} is not declared.");
                        Assert.That(declared, Is.EqualTo(nodeClass.Key), $"{nodeClass.Key}s.{constant.Key}");
                    }
                }
                foreach (KeyValuePair<uint, (string Symbol, string NodeClass)> row in derived)
                {
                    if (generated[row.Value.NodeClass].TryGetValue(row.Value.Symbol, out uint value))
                    {
                        Assert.That(value, Is.EqualTo(row.Key), row.Value.Symbol);
                        matched++;
                    }
                }
                foreach (string nodeClass in s_typeNodeClasses)
                {
                    Assert.That(generated[nodeClass].Values, Is.SupersetOf(
                        declarations.Where(entry => entry.Value == nodeClass)
                            .Select(entry => entry.Key)), nodeClass);
                }
            });
            Assert.That(matched, Is.GreaterThan(400));
            Assert.Multiple(() =>
            {
                Assert.That(ObjectTypes.LADSComponentsType, Is.EqualTo(1025u));
                Assert.That(ObjectTypes.LADSOperationCountersType, Is.EqualTo(1034u));
                Assert.That(ObjectTypes.LADSOperationModeStateMachineType, Is.EqualTo(1050u));
                Assert.That(ObjectTypeIds.LADSDeviceType, Is.EqualTo(
                    new ExpandedNodeId(ObjectTypes.LADSDeviceType, LadsModel.NamespaceUri)));
            });
        }

        /// <summary>
        /// The LADS types that derive from Machinery reference the Machinery NodeIds 1006, 1008 and 1009,
        /// and the Machinery assembly the package loads still declares those identifiers.
        /// </summary>
        [TestCase(ObjectTypes.LADSComponentsType, Machinery.ObjectTypes.MachineComponentsType, 1006u)]
        [TestCase(
            ObjectTypes.LADSOperationModeStateMachineType,
            Machinery.ObjectTypes.MachineryOperationModeStateMachineType,
            1008u)]
        [TestCase(ObjectTypes.LADSOperationCountersType, Machinery.ObjectTypes.MachineryOperationCounterType, 1009u)]
        public void MachineryBaseTypesKeepTheirNodeIds(uint ladsType, uint machineryType, uint expected)
        {
            string machinery = "ns=" + LadsTestContext.NodeSetIndexOf(Machinery.Namespaces.Machinery)
                .ToString(CultureInfo.InvariantCulture) + ";i=" + expected.ToString(CultureInfo.InvariantCulture);
            XElement declaration = LadsTestContext.ReadDeclaration(ladsType);
            string?[] supertypes =
            [
                .. declaration.Descendants(LadsTestContext.NodeSetNamespace + "Reference")
                    .Where(reference => (string?)reference.Attribute("ReferenceType") == "HasSubtype" &&
                        (string?)reference.Attribute("IsForward") == "false")
                    .Select(reference => reference.Value)
            ];
            Assert.Multiple(() =>
            {
                Assert.That(machineryType, Is.EqualTo(expected));
                Assert.That(supertypes, Is.EqualTo(new[] { machinery }));
            });

            SystemContext context = LadsTestContext.CreateSystemContext();
            Dictionary<NodeId, NodeState> nodes = LadsTestContext.IndexNodes(
                context,
                new NodeStateCollection().AddLadsTypeSystem(context));
            NodeId baseId = NodeId.Create(expected, Machinery.Namespaces.Machinery, context.NamespaceUris);
            NodeId typeId = NodeId.Create(ladsType, LadsModel.NamespaceUri, context.NamespaceUris);
            Assert.Multiple(() =>
            {
                Assert.That(nodes.TryGetValue(baseId, out NodeState? baseType), Is.True);
                Assert.That(baseType, Is.InstanceOf<BaseObjectTypeState>());
                Assert.That(nodes.TryGetValue(typeId, out NodeState? type), Is.True);
                Assert.That(((BaseObjectTypeState)type!).SuperTypeId, Is.EqualTo(baseId));
            });
        }

        /// <summary>
        /// Reads the numeric identifier constants of a generated class. The classes also hold string
        /// constants for instance declarations below placeholders, which have no NodeSet declaration.
        /// </summary>
        private static Dictionary<string, uint> ReadConstants(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(uint))
                .ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue()!);
        }

        private static Dictionary<uint, (string Symbol, string NodeClass)> ReadIdentifiers(string file)
        {
            return File.ReadLines(LadsTestContext.ModelPath(file))
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith("SymbolicName,", StringComparison.Ordinal))
                .Select(line => line.Split(','))
                .ToDictionary(
                    fields => uint.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields => (fields[0], fields[2]));
        }

        private static readonly string[] s_typeNodeClasses = ["DataType", "Method", "ObjectType"];

        private static readonly string[] s_requiredModels =
        [
            "http://opcfoundation.org/UA/|1.05.02|2022-11-01T00:00:00Z",
            "http://opcfoundation.org/UA/DI/|1.04.0|2022-11-03T00:00:00Z",
            "http://opcfoundation.org/UA/AMB/|1.01.0|2022-11-01T00:00:00Z",
            "http://opcfoundation.org/UA/Machinery/|1.03.0|2023-08-01T00:00:00Z"
        ];
    }
}
