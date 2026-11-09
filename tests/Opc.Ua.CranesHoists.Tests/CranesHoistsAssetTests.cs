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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Pins the official publication and its complete local identifier drift guard.
    /// </summary>
    [TestFixture]
    [Category("CranesHoists")]
    public sealed class CranesHoistsAssetTests
    {
        /// <summary>
        /// Both published files retain their original contents apart from permitted line-ending normalization.
        /// </summary>
        [TestCase("Opc.Ua.CranesHoists.NodeSet2.xml", CranesHoistsModel.NormalizedNodeSetSha256)]
        [TestCase("Opc.Ua.CranesHoists.NodeSet2.documentation.csv",
            "C395846FFC6265E868D7893F7E35F67D2F7985143939F54B84E9ED0CD2EFEEF0")]
        public void PublishedSourcesMatchPinnedHashes(string file, string expectedHash)
        {
            byte[] bytes = File.ReadAllBytes(CranesHoistsTestModel.AssetPath(file));
            string normalized = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
#if NET8_0_OR_GREATER
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
#else
            using var hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(normalized));
#endif
            string actualHash = CoreUtils.ToHexString(digest);
            Assert.That(actualHash, Is.EqualTo(expectedHash));

            using JsonDocument provenance = JsonDocument.Parse(
                File.ReadAllText(CranesHoistsTestModel.AssetPath("provenance.json")));
            JsonElement source = provenance.RootElement.GetProperty("sources").EnumerateArray()
                .Single(entry => entry.GetProperty("file").GetString() == file);
            Assert.That(
                source.GetProperty("normalizedSha256").GetString(),
                Is.EqualTo(actualHash).IgnoreCase);
            Assert.That(
                source.GetProperty("url").GetString(),
                Is.EqualTo(
                    "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/" +
                    CranesHoistsModel.SourceCommit + "/CranesHoists/" + file));
        }

        /// <summary>
        /// The model URI, release and exact dependency versions are kept, including the source's date discrepancy.
        /// </summary>
        [Test]
        public void PublishedModelMetadataAndDependenciesAreUnmodified()
        {
            XDocument document = CranesHoistsTestModel.ReadPublishedModel();
            XNamespace schema = CranesHoistsTestModel.Schema;
            XElement model = document.Descendants(schema + "Model").Single();
            Assert.Multiple(() =>
            {
                Assert.That((string?)model.Attribute("ModelUri"), Is.EqualTo(CranesHoistsModel.NamespaceUri));
                Assert.That((string?)model.Attribute("Version"), Is.EqualTo(CranesHoistsModel.Version));
                Assert.That((string?)model.Attribute("PublicationDate"), Is.EqualTo("2023-09-01T00:00:00Z"));
                Assert.That(
                    model.Elements(schema + "RequiredModel").Select(required => (
                        (string?)required.Attribute("ModelUri"),
                        (string?)required.Attribute("Version"),
                        (string?)required.Attribute("PublicationDate"))),
                    Is.EqualTo(new[]
                    {
                        ("http://opcfoundation.org/UA/", "1.05.01", "2022-02-24T00:00:00Z"),
                        ("http://opcfoundation.org/UA/DI/", "1.03.1", "2021-09-07T00:00:00Z"),
                        ("http://opcfoundation.org/UA/Machinery/", "1.02.0", "2022-05-01T00:00:00Z"),
                        ("http://opcfoundation.org/UA/Robotics/", "1.0.0", "2019-05-06T00:00:00Z")
                    }));
                Assert.That(
                    document.Descendants(schema + "NamespaceUris").Single().Elements().Select(uri => uri.Value),
                    Is.EqualTo(new[]
                    {
                        Di.Namespaces.OpcUaDi,
                        Machinery.Namespaces.Machinery,
                        Robotics.Namespaces.Robotics,
                        CranesHoistsModel.NamespaceUri
                    }));
                Assert.That(
                    document.Root!.Elements(schema + "UAVariable").Single(
                        node => (string?)node.Attribute("NodeId") == "ns=4;i=1002")
                        .Element(schema + "Value")!.Elements().Single().Value,
                    Is.EqualTo("2023-05-01T00:00:00Z"));
            });
        }

        /// <summary>
        /// All 89 published declarations are represented once, with their authoritative IDs and node classes.
        /// </summary>
        [Test]
        public void IdentifierDriftTableCoversEveryPublishedDeclaration()
        {
            XElement[] declarations = CranesHoistsTestModel.ReadPublishedModel().Root!.Elements()
                .Where(element => element.Attribute("NodeId") != null).ToArray();
            string[] rows = File.ReadAllLines(
                CranesHoistsTestModel.AssetPath("Opc.Ua.CranesHoists.NodeIds.csv"));
            string[][] fields = rows.Skip(1).Select(row => row.Split(',')).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(rows[0], Is.EqualTo("SymbolicName,NodeId,NodeClass"));
                Assert.That(declarations, Has.Length.EqualTo(89));
                Assert.That(fields, Has.Length.EqualTo(89));
                Assert.That(fields.Select(row => row[0]), Is.Unique);
                Assert.That(fields.Select(row => row[1]), Is.Unique);
                Assert.That(
                    fields.Select(row => (uint.Parse(row[1], CultureInfo.InvariantCulture), row[2])),
                    Is.EquivalentTo(declarations.Select(node => (
                        CranesHoistsTestModel.PublishedNumericId(node),
                        node.Name.LocalName.Substring(2)))));
                Assert.That(declarations.Count(node => node.Name.LocalName == "UAObjectType"), Is.EqualTo(6));
                Assert.That(declarations.Count(node => node.Name.LocalName == "UADataType"), Is.EqualTo(4));
                Assert.That(declarations.Count(node => node.Name.LocalName == "UAReferenceType"), Is.EqualTo(1));
                Assert.That(declarations.Count(node => node.Name.LocalName == "UAObject"), Is.EqualTo(31));
                Assert.That(declarations.Count(node => node.Name.LocalName == "UAVariable"), Is.EqualTo(47));
                Assert.That(declarations.Where(node => node.Name.LocalName == "UAMethod"), Is.Empty);
            });
        }

        /// <summary>
        /// The drift table is explicitly local and no fabricated upstream identifier table accompanies it.
        /// </summary>
        [Test]
        public void ProvenanceDistinguishesDerivedInventoryFromPublishedFiles()
        {
            using JsonDocument provenance = JsonDocument.Parse(
                File.ReadAllText(CranesHoistsTestModel.AssetPath("provenance.json")));
            JsonElement root = provenance.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("revision").GetString(), Is.EqualTo(CranesHoistsModel.SourceCommit));
                Assert.That(root.GetProperty("modelUri").GetString(), Is.EqualTo(Namespaces.CranesHoists));
                Assert.That(root.GetProperty("sources").GetArrayLength(), Is.EqualTo(2));
                Assert.That(
                    root.GetProperty("derivedIdentifierTable").GetProperty("declarationCount").GetInt32(),
                    Is.EqualTo(89));
                Assert.That(
                    root.GetProperty("derivedIdentifierTable").GetProperty("upstreamIdentifierTablePublished")
                        .GetBoolean(),
                    Is.False);
                Assert.That(
                    Directory.GetFiles(CranesHoistsTestModel.AssetPath(string.Empty), "*.Upstream.NodeIds.csv"),
                    Is.Empty);
            });
        }
    }
}
