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
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Pins the released GMS publication independently of generated output.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class ModelAssetTests
    {
        /// <summary>
        /// Official filenames and content are preserved apart from repository line endings.
        /// </summary>
        [TestCase("opc.ua.gms.nodeset2.xml",
            "9E510C01A2671B732E5EB27561B06A5BC040BB219412884D0A3AFF3C55B88EE0")]
        [TestCase("opc.ua.gms.nodesids.csv",
            "CB2F8533CAC25659CF46DF8621F7C3B462933241AC9F6B615F7B6289754D6203")]
        [TestCase("opc.ua.gms.nodeset2.documentation.csv",
            "D0D2E53A991EFA4649AB0DC0442F7608D2B5904FA848CB203655AD1967ECBF2B")]
        public void PublishedFilesMatchPinnedHashes(string file, string expectedHash)
        {
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(ModelPath(file)))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
#if NET8_0_OR_GREATER
            string actualHash = CoreUtils.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
#else
            using var hash = SHA256.Create();
            string actualHash = CoreUtils.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
#endif
            using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(ModelPath("provenance.json")));
            JsonElement source = provenance.RootElement.GetProperty("sources").EnumerateArray()
                .Single(entry => entry.GetProperty("file").GetString() == file);
            Assert.Multiple(() =>
            {
                Assert.That(actualHash, Is.EqualTo(expectedHash));
                Assert.That(source.GetProperty("normalizedSha256").GetString(),
                    Is.EqualTo(expectedHash).IgnoreCase);
                Assert.That(provenance.RootElement.GetProperty("revision").GetString(),
                    Is.EqualTo("96a08945425d0295edb3607d2a38f6841ef44046"));
                Assert.That(source.GetProperty("url").GetString(), Is.EqualTo(
                    "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/" +
                    $"96a08945425d0295edb3607d2a38f6841ef44046/GMS/{file}"));
            });
        }

        /// <summary>
        /// The reference edition does not overwrite the released model version or dependency minimums.
        /// </summary>
        [Test]
        public void ReleasedModelIdentityAndRequiredEditionsRemainUnchanged()
        {
            XElement root = XDocument.Load(ModelPath("opc.ua.gms.nodeset2.xml")).Root!;
            XElement model = root.Element(s_ns + "Models")!.Elements().Single();
            string[] requirements =
            [
                .. model.Elements(s_ns + "RequiredModel").Select(dependency =>
                    $"{(string?)dependency.Attribute("ModelUri")}|{(string?)dependency.Attribute("Version")}|" +
                    $"{(string?)dependency.Attribute("PublicationDate")}")
            ];
            using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(ModelPath("provenance.json")));
            Assert.Multiple(() =>
            {
                Assert.That((string?)model.Attribute("ModelUri"), Is.EqualTo("http://opcfoundation.org/UA/GMS/"));
                Assert.That((string?)model.Attribute("Version"), Is.EqualTo("1.0.0"));
                Assert.That((string?)model.Attribute("PublicationDate"), Is.EqualTo("2023-07-01T00:00:00Z"));
                Assert.That(provenance.RootElement.GetProperty("referenceEdition").GetString(), Is.EqualTo("1.0.1"));
                Assert.That(requirements, Is.EqualTo(s_requiredModels));
            });
        }

        /// <summary>
        /// Every canonical numeric identifier and class is guarded, without inventing missing abstract encodings.
        /// </summary>
        [Test]
        public void DriftGuardCoversAllCanonicalDeclarationsAndDocumentsSurplusPublishedRows()
        {
            XElement root = XDocument.Load(ModelPath("opc.ua.gms.nodeset2.xml")).Root!;
            Dictionary<uint, string> declarations = root.Elements()
                .Where(node => node.Attribute("NodeId") != null)
                .ToDictionary(
                    node => uint.Parse(((string)node.Attribute("NodeId")!)[7..], CultureInfo.InvariantCulture),
                    node => node.Name.LocalName[2..]);
            Dictionary<uint, string> upstream = ReadIdentifiers("opc.ua.gms.nodesids.csv");
            Dictionary<uint, string> derived = ReadIdentifiers("Opc.Ua.GMS.Derived.NodeIds.csv");
            Assert.Multiple(() =>
            {
                Assert.That(declarations, Has.Count.EqualTo(273));
                Assert.That(derived, Is.EquivalentTo(declarations));
                Assert.That(upstream.Where(entry => declarations.ContainsKey(entry.Key)),
                    Is.EquivalentTo(declarations));
                Assert.That(upstream.Keys.Except(declarations.Keys),
                    Is.EquivalentTo(new uint[] { 5002, 5003, 5004, 6061, 6062 }));
                Assert.That(declarations.Values.GroupBy(value => value).ToDictionary(group => group.Key,
                    group => group.Count()), Is.EquivalentTo(new Dictionary<string, int>
                    {
                        ["DataType"] = 7,
                        ["Variable"] = 194,
                        ["VariableType"] = 3,
                        ["ObjectType"] = 19,
                        ["Object"] = 43,
                        ["Method"] = 7
                    }));
            });
        }

        private static Dictionary<uint, string> ReadIdentifiers(string file)
        {
            return File.ReadLines(ModelPath(file))
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith("SymbolicName,", StringComparison.Ordinal))
                .Select(line => line.Split(','))
                .ToDictionary(
                    fields => uint.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields => fields[2]);
        }

        private static string ModelPath(string file)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", file);
        }

        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
        private static readonly string[] s_requiredModels =
        [
            "http://opcfoundation.org/UA/|1.05.02|2022-11-01T00:00:00Z",
            "http://opcfoundation.org/UA/DI/|1.04.0|2022-11-03T00:00:00Z",
            "http://opcfoundation.org/UA/IA/|1.01.0|2021-07-31T00:00:00Z",
            "http://opcfoundation.org/UA/Machinery/|1.02.0|2021-12-01T00:00:00Z",
            "http://opcfoundation.org/UA/MachineTool/|1.01.1|2022-07-04T12:00:00Z",
            "http://opcfoundation.org/UA/Machinery/Result/|1.00.0|2023-01-01T00:00:00Z"
        ];
    }
}
