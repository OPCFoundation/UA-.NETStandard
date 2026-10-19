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
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Pins the released AutoID publication and the generated identifiers independently of each other.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdModelAssetTests
    {
        /// <summary>
        /// The vendored files are byte-identical to the pinned UA-Nodeset revision.
        /// </summary>
        [TestCase("Opc.Ua.AutoID.NodeSet2.xml",
            "C3D5E77AAEEED03862620E307CDF4974E5998267E8FAA992C1F1A24F9BE6A4BA")]
        [TestCase("Opc.Ua.AutoID.NodeIds.csv",
            "E8F1D80FBCE4F76E9D42F3A652C6CC8C6DAF68C289B2629BCCE68C5879746CD6")]
        public void PublishedFilesMatchPinnedHashes(string file, string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(AutoIdTestContext.ModelPath(file));
#if NET8_0_OR_GREATER
            string actualHash = CoreUtils.ToHexString(SHA256.HashData(bytes));
#else
            using var hash = SHA256.Create();
            string actualHash = CoreUtils.ToHexString(hash.ComputeHash(bytes));
#endif
            using var provenance = JsonDocument.Parse(
                File.ReadAllText(AutoIdTestContext.ModelPath("provenance.json")));
            JsonElement source = provenance.RootElement.GetProperty("sources").EnumerateArray()
                .Single(entry => entry.GetProperty("file").GetString() == file);
            Assert.Multiple(() =>
            {
                Assert.That(actualHash, Is.EqualTo(expectedHash).IgnoreCase);
                Assert.That(source.GetProperty("sha256").GetString(), Is.EqualTo(expectedHash).IgnoreCase);
                Assert.That(provenance.RootElement.GetProperty("revision").GetString(),
                    Is.EqualTo(AutoIdModel.SourceCommit));
                Assert.That(source.GetProperty("url").GetString(), Is.EqualTo(
                    "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/" +
                    $"{AutoIdModel.SourceCommit}/AutoID/{file}"));
            });
            if (file.EndsWith(".xml", StringComparison.Ordinal))
            {
                Assert.That(actualHash, Is.EqualTo(AutoIdModel.NodeSetSha256).IgnoreCase);
            }
        }

        /// <summary>
        /// The model identity and the lower-bound dependencies are those of the publication.
        /// </summary>
        [Test]
        public void ReleasedModelIdentityAndRequiredModelsRemainUnchanged()
        {
            XElement model = AutoIdTestContext.ReadNodeSet()
                .Element(AutoIdTestContext.NodeSetNamespace + "Models")!.Elements().Single();
            string[] requirements =
            [
                .. model.Elements(AutoIdTestContext.NodeSetNamespace + "RequiredModel").Select(dependency =>
                    $"{(string?)dependency.Attribute("ModelUri")}|{(string?)dependency.Attribute("Version")}|" +
                    $"{(string?)dependency.Attribute("PublicationDate")}")
            ];
            Assert.Multiple(() =>
            {
                Assert.That((string?)model.Attribute("ModelUri"), Is.EqualTo(AutoIdModel.NamespaceUri));
                Assert.That((string?)model.Attribute("Version"), Is.EqualTo(AutoIdModel.Version));
                Assert.That((string?)model.Attribute("PublicationDate"), Is.EqualTo("2020-06-18T13:52:03Z"));
                Assert.That(requirements, Is.EqualTo(s_requiredModels));
                Assert.That(Namespaces.AutoID, Is.EqualTo(AutoIdModel.NamespaceUri));
                Assert.That(AutoIdModel.GetNamespaceUris(), Is.EqualTo(AutoIdTestContext.ExpectedNamespaceUris));
            });
        }

        /// <summary>
        /// The published table agrees with the NodeSet on every row it has, and the declarations it
        /// lacks are exactly the ones added after it was published.
        /// </summary>
        [Test]
        public void PublishedTableAgreesWithNodeSetAndOnlyLacksLaterDeclarations()
        {
            Dictionary<uint, string> declarations = AutoIdTestContext.ReadDeclarations();
            Dictionary<uint, (string Symbol, string NodeClass)> published =
                ReadIdentifiers("Opc.Ua.AutoID.NodeIds.csv");
            Dictionary<uint, (string Symbol, string NodeClass)> derived =
                ReadIdentifiers("Opc.Ua.AutoID.Derived.NodeIds.csv");
            Assert.Multiple(() =>
            {
                Assert.That(declarations, Has.Count.EqualTo(305));
                Assert.That(published, Has.Count.EqualTo(238));
                Assert.That(derived.Keys, Is.EquivalentTo(published.Keys));
                foreach (KeyValuePair<uint, (string Symbol, string NodeClass)> row in published)
                {
                    Assert.That(declarations.TryGetValue(row.Key, out string? nodeClass), Is.True,
                        $"Published row {row.Key} is not declared.");
                    Assert.That(nodeClass, Is.EqualTo(row.Value.NodeClass), $"NodeClass of {row.Key}");
                    Assert.That(derived[row.Key].NodeClass, Is.EqualTo(row.Value.NodeClass));
                }
                Assert.That(declarations.Keys.Except(published.Keys).Count(), Is.EqualTo(67));
                Assert.That(declarations.Keys.Except(published.Keys), Is.SupersetOf(
                    new uint[] { 1010, 1015, 1016, 1017, 1018, 3017, 3018, 7023 }));
                Assert.That(declarations.Values.GroupBy(value => value).ToDictionary(
                    group => group.Key,
                    group => group.Count()), Is.EquivalentTo(new Dictionary<string, int>
                    {
                        ["DataType"] = 28,
                        ["Variable"] = 175,
                        ["VariableType"] = 1,
                        ["ObjectType"] = 17,
                        ["Object"] = 54,
                        ["Method"] = 30
                    }));
            });
        }

        /// <summary>
        /// Every generated identifier constant names a NodeSet declaration of the matching node class,
        /// every symbol of the drift guard that has a constant carries the published NodeId, and all
        /// types and methods of the NodeSet have a constant.
        /// </summary>
        [Test]
        public void GeneratedIdentifiersMatchTheNodeSetAndTheIdentifierTable()
        {
            Dictionary<uint, string> declarations = AutoIdTestContext.ReadDeclarations();
            Dictionary<uint, (string Symbol, string NodeClass)> derived =
                ReadIdentifiers("Opc.Ua.AutoID.Derived.NodeIds.csv");
            var generated = new Dictionary<string, Dictionary<string, uint>>
            {
                ["DataType"] = ReadConstants(DataTypes.Identifiers, DataTypes.GetBrowseName),
                ["Method"] = ReadConstants(Methods.Identifiers, Methods.GetBrowseName),
                ["Object"] = ReadConstants(Objects.Identifiers, Objects.GetBrowseName),
                ["ObjectType"] = ReadConstants(ObjectTypes.Identifiers, ObjectTypes.GetBrowseName),
                ["Variable"] = ReadConstants(Variables.Identifiers, Variables.GetBrowseName),
                ["VariableType"] = ReadConstants(VariableTypes.Identifiers, VariableTypes.GetBrowseName)
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
            Assert.That(matched, Is.GreaterThan(200));
            Assert.Multiple(() =>
            {
                Assert.That(ObjectTypes.AutoIdDeviceType, Is.EqualTo(1001u));
                Assert.That(ObjectTypes.RfidReaderDeviceType, Is.EqualTo(1003u));
                Assert.That(ObjectTypes.RfidAccessEventType, Is.EqualTo(1016u));
                Assert.That(DataTypes.ScanData, Is.EqualTo(3020u));
                Assert.That(DataTypes.RfidAccessResult, Is.EqualTo(3018u));
                Assert.That(Methods.AutoIdDeviceType_Scan, Is.EqualTo(7008u));
                Assert.That(ObjectTypeIds.RfidReaderDeviceType, Is.EqualTo(
                    new ExpandedNodeId(1003u, AutoIdModel.NamespaceUri)));
            });
        }

        private static Dictionary<string, uint> ReadConstants(
            IEnumerable<uint> identifiers,
            Func<uint, string> getBrowseName)
        {
            return identifiers.ToDictionary(getBrowseName, identifier => identifier);
        }

        private static Dictionary<uint, (string Symbol, string NodeClass)> ReadIdentifiers(string file)
        {
            return File.ReadLines(AutoIdTestContext.ModelPath(file))
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith("SymbolicName,", StringComparison.Ordinal))
                .Select(line => line.Split(','))
                .ToDictionary(
                    fields => uint.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields => (fields[0], fields[2]));
        }

        private static readonly string[] s_typeNodeClasses = ["DataType", "Method", "ObjectType", "VariableType"];

        private static readonly string[] s_requiredModels =
        [
            "http://opcfoundation.org/UA/|1.03|2019-09-09T00:00:00Z",
            "http://opcfoundation.org/UA/DI/|1.01|2019-05-01T00:00:00Z"
        ];
    }
}
