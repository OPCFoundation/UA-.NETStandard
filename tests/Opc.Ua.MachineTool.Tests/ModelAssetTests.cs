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

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Pins the released publications independently of source-generator output.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class ModelAssetTests
    {
        /// <summary>
        /// Every upstream file keeps its original name and contents except for line endings.
        /// </summary>
        [TestCase("MachineTool", "Opc.Ua.MachineTool.NodeSet2.xml",
            MachineToolModels.MachineToolNodeSetSha256)]
        [TestCase("MachineTool", "Opc.Ua.MachineTool.NodeIds.csv",
            "32708D4914C41AB06B2EE9B464769FD621ECFA10D4D33263C55B0E7026791C0C")]
        [TestCase("MachineTool", "Opc.Ua.MachineTool.Nodeset2.documentation.csv",
            "D23A4C0F44F5C5020C775CFF3D202C83E507AD8EDF70C8CDA19F6FD615A9C011")]
        [TestCase("WMTP", "Opc.Ua.WMTP.Nodeset2.xml",
            MachineToolModels.WmtpNodeSetSha256)]
        [TestCase("WMTP", "Opc.Ua.WMTP.NodeIds.csv",
            "B12B5D8DC05EE99A441D233B295273417A8104C17F0E5F743E5FA0A56B09925D")]
        [TestCase("WMTP", "Opc.Ua.WMTP.NodeSet2.documentation.csv",
            "B0604486584F067F9B3E380CAB809177B648303C4BCB59B2D0445A289A887A2C")]
        [TestCase("WMTP", "Opc.Ua.WMTP.NodeSet2.profiles.xml",
            "536546EA79D46F4E240DC50770DA83F661168A030F865A0C2434596ADDF5386F")]
        public void PublishedFilesMatchPinnedHashes(string family, string file, string expectedHash)
        {
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(ModelPath(family, file)))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
#if NET8_0_OR_GREATER
            string actualHash = CoreUtils.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
#else
            using var hash = SHA256.Create();
            string actualHash = CoreUtils.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
#endif

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(ModelPath("provenance.json")));
            JsonElement source = manifest.RootElement.GetProperty("families").GetProperty(family)
                .GetProperty("sources").EnumerateArray()
                .Single(entry => entry.GetProperty("file").GetString() == file);
            Assert.Multiple(() =>
            {
                Assert.That(actualHash, Is.EqualTo(expectedHash));
                Assert.That(source.GetProperty("normalizedSha256").GetString(),
                    Is.EqualTo(expectedHash).IgnoreCase);
                Assert.That(manifest.RootElement.GetProperty("revision").GetString(),
                    Is.EqualTo("96a08945425d0295edb3607d2a38f6841ef44046"));
                Assert.That(manifest.RootElement.GetProperty("revision").GetString(),
                    Is.EqualTo(MachineToolModels.SourceCommit));
                Assert.That(source.GetProperty("url").GetString(), Is.EqualTo(
                    "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/" +
                    $"96a08945425d0295edb3607d2a38f6841ef44046/{family}/{file}"));
            });
        }

        /// <summary>
        /// The published version, namespace, date and every local declaration remain intact.
        /// </summary>
        [TestCase("MachineTool", "Opc.Ua.MachineTool.NodeSet2.xml",
            "http://opcfoundation.org/UA/MachineTool/", "1.02.0", "2024-11-01T00:00:00Z", 589)]
        [TestCase("WMTP", "Opc.Ua.WMTP.Nodeset2.xml",
            "http://opcfoundation.org/UA/WMTP/", "1.0.0", "2025-11-01T00:00:00Z", 332)]
        public void ReleasedModelIdentityIsPreserved(
            string family,
            string file,
            string namespaceUri,
            string version,
            string publicationDate,
            int declarations)
        {
            XElement root = XDocument.Load(ModelPath(family, file)).Root!;
            XElement model = root.Element(s_ns + "Models")!.Elements(s_ns + "Model").Single();
            Assert.Multiple(() =>
            {
                Assert.That((string?)model.Attribute("ModelUri"), Is.EqualTo(namespaceUri));
                Assert.That((string?)model.Attribute("Version"), Is.EqualTo(version));
                Assert.That((string?)model.Attribute("PublicationDate"), Is.EqualTo(publicationDate));
                Assert.That(root.Elements().Count(node => node.Attribute("NodeId") != null),
                    Is.EqualTo(declarations));
            });
        }

        /// <summary>
        /// Dependency identities and minimum editions do not change when the models share an assembly.
        /// </summary>
        [TestCase("MachineTool", "Opc.Ua.MachineTool.NodeSet2.xml")]
        [TestCase("WMTP", "Opc.Ua.WMTP.Nodeset2.xml")]
        public void ReleasedRequiredModelsArePreserved(string family, string file)
        {
            XDocument document = XDocument.Load(ModelPath(family, file));
            string[] requirements =
            [
                .. document.Descendants(s_ns + "RequiredModel").Select(model =>
                    $"{(string?)model.Attribute("ModelUri")}|{(string?)model.Attribute("Version")}|" +
                    $"{(string?)model.Attribute("PublicationDate")}")
            ];
            string[] expected = family == "MachineTool"
                ?
                [
                    "http://opcfoundation.org/UA/|1.05.02|2022-11-01T00:00:00Z",
                    "http://opcfoundation.org/UA/DI/|1.04.0|2022-11-03T00:00:00Z",
                    "http://opcfoundation.org/UA/Machinery/|1.03.0|2023-08-01T00:00:00Z",
                    "http://opcfoundation.org/UA/IA/|1.01.2|2024-02-28T00:00:00Z",
                    "http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/|2.0.0|2024-01-31T00:00:00Z",
                    "http://opcfoundation.org/UA/Machinery/Jobs/|1.0.1|2024-05-01T00:00:00Z"
                ]
                :
                [
                    "http://opcfoundation.org/UA/|1.05.05|2025-06-30T00:00:00Z",
                    "http://opcfoundation.org/UA/DI/|1.04.0|2022-11-03T00:00:00Z",
                    "http://opcfoundation.org/UA/Machinery/|1.03.0|2023-08-01T00:00:00Z",
                    "http://opcfoundation.org/UA/Dictionary/IRDI|1.01.0|2023-10-27T00:00:00Z",
                    "http://opcfoundation.org/UA/PADIM/|1.01.0|2023-10-27T00:00:00Z",
                    "http://opcfoundation.org/UA/Machinery/ProcessValues/|1.00.0|2023-05-01T00:00:00Z"
                ];
            Assert.That(requirements, Is.EqualTo(expected));
        }

        /// <summary>
        /// Derived symbolic names never change or drop upstream numeric identifiers or node classes.
        /// </summary>
        [TestCase("MachineTool", "Opc.Ua.MachineTool.NodeSet2.xml", "Opc.Ua.MachineTool")]
        [TestCase("WMTP", "Opc.Ua.WMTP.Nodeset2.xml", "Opc.Ua.WMTP")]
        public void DriftTablesPreserveEveryPublishedNumericIdentity(string family, string xmlFile, string prefix)
        {
            XElement root = XDocument.Load(ModelPath(family, xmlFile)).Root!;
            Dictionary<uint, string> declarations = root.Elements()
                .Where(node => node.Attribute("NodeId") != null)
                .ToDictionary(
                    node => uint.Parse(((string)node.Attribute("NodeId")!)[7..], CultureInfo.InvariantCulture),
                    node => node.Name.LocalName[2..]);
            Dictionary<uint, string> upstream = ReadIdentifiers(ModelPath(family, $"{prefix}.NodeIds.csv"));
            Dictionary<uint, string> derived = ReadIdentifiers(ModelPath(family, $"{prefix}.Derived.NodeIds.csv"));
            Assert.Multiple(() =>
            {
                Assert.That(upstream, Is.EquivalentTo(declarations));
                Assert.That(derived, Is.EquivalentTo(declarations));
            });
        }

        private static Dictionary<uint, string> ReadIdentifiers(string path)
        {
            return File.ReadLines(path)
                .Where(line => !line.StartsWith("SymbolicName,", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(','))
                .ToDictionary(
                    fields => uint.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields => fields[2]);
        }

        private static string ModelPath(params string[] segments)
        {
            return Path.Combine(
                [TestContext.CurrentContext.TestDirectory, "Model", .. segments]);
        }

        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
