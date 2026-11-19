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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter.Tests
{
    /// <summary>
    /// Tests command input, file output, and fault reports.
    /// </summary>
    [TestFixture]
    public sealed class ExportCommandTests
    {
        /// <summary>
        /// Creates isolated input and output folders.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_folder = Path.Combine(Path.GetTempPath(), "wot-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_folder);
            m_output = new StringWriter(CultureInfo.InvariantCulture);
            m_error = new StringWriter(CultureInfo.InvariantCulture);
            m_command = new ExportCommand(m_output, m_error);
        }

        /// <summary>
        /// Removes files made by this test only.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_output.Dispose();
            m_error.Dispose();
            Directory.Delete(m_folder, recursive: true);
        }

        /// <summary>
        /// Rejects missing or extra arguments.
        /// </summary>
        [Test]
        public async Task InvalidArgumentsReturnUsageErrorAsync()
        {
            foreach (string[] args in (string[][])[[], ["one", "two", "three"], ["--unknown"]])
            {
                int code = await m_command.RunAsync(args).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2));
            }
            Assert.That(m_error.ToString(), Does.Contain("ERROR:"));
        }

        /// <summary>
        /// Shows help without a required source.
        /// </summary>
        [Test]
        public async Task HelpReturnsSuccessAsync()
        {
            int code = await m_command.RunAsync(["--help"]).ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            Assert.That(m_output.ToString(), Does.Contain("Usage:"));
            Assert.That(Directory.EnumerateFileSystemEntries(m_folder), Is.Empty);
        }

        /// <summary>
        /// Rejects a missing path and a folder with no input files.
        /// </summary>
        [Test]
        public async Task MissingSourceAndEmptySourceReturnInputErrorAsync()
        {
            int missing = await m_command.RunAsync([Path.Combine(m_folder, "missing")]).ConfigureAwait(false);
            Assert.That(missing, Is.EqualTo(2));
            Assert.That(await m_command.RunAsync([m_folder]).ConfigureAwait(false), Is.EqualTo(2));
        }

        /// <summary>
        /// Writes each model, reports progress, and does not read the output tree.
        /// </summary>
        [Test]
        public async Task RecursiveExportUsesDefaultOutputAndIsStableAsync()
        {
            string child = Path.Combine(m_folder, "child");
            Directory.CreateDirectory(child);
            await File.WriteAllTextAsync(Path.Combine(child, "Test.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            string excluded = Path.Combine(m_folder, "out");
            Directory.CreateDirectory(excluded);
            await File.WriteAllTextAsync(Path.Combine(excluded, "Bad.NodeSet2.xml"), "invalid").ConfigureAwait(false);

            Assert.That(await m_command.RunAsync([m_folder]).ConfigureAwait(false), Is.Zero, m_error.ToString());
            string target = Path.Combine(excluded, "child", "Test.NodeSet2.xml");
            string[] paths = Directory.GetFiles(target);
            Assert.That(paths, Has.Length.EqualTo(2));
            string[] first = await Task.WhenAll(paths.Select(path => File.ReadAllTextAsync(path)))
                .ConfigureAwait(false);
            foreach (string json in first)
            {
                using var document = JsonDocument.Parse(json);
                Assert.That(document.RootElement.GetProperty("@type").EnumerateArray()
                    .Any(type => type.GetString() == "tm:ThingModel"), Is.True);
            }
            Assert.That(m_output.ToString(), Does.Contain("[1/1]").And.Contain("Wrote 2 Thing Models"));
            Assert.That(await m_command.RunAsync([m_folder]).ConfigureAwait(false), Is.Zero, m_error.ToString());
            string[] second = await Task.WhenAll(paths.Select(path => File.ReadAllTextAsync(path)))
                .ConfigureAwait(false);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(Directory.GetFiles(target, "*.tmp"), Is.Empty);
        }

        /// <summary>
        /// Resolves event ancestry across required models regardless of file order.
        /// </summary>
        [TestCase("i=2041", "uav:eventType")]
        [TestCase("i=58", "uav:objectType")]
        [TestCase("i=999999", "uav:objectType")]
        [TestCase("ns=1;i=1", "uav:objectType")]
        public async Task ExportResolvesRequiredModelEventAncestryAsync(string ancestorId, string expectedType)
        {
            const string child = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:export:child</Uri><Uri>urn:export:parent</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:export:child" Version="1.0">
                <RequiredModel ModelUri="urn:export:parent" Version="1.0" />
              </Model></Models>
              <UAObjectType NodeId="ns=1;i=1" BrowseName="1:ChildType">
                <DisplayName>ChildType</DisplayName>
                <References><Reference ReferenceType="i=45" IsForward="false">ns=2;i=1</Reference></References>
              </UAObjectType>
            </UANodeSet>
            """;
            string parent = $$"""
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:export:parent</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:export:parent" Version="1.0" /></Models>
              <UAObjectType NodeId="ns=1;i=1" BrowseName="1:ParentType">
                <DisplayName>ParentType</DisplayName>
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">{{ancestorId}}</Reference>
                </References>
              </UAObjectType>
            </UANodeSet>
            """;
            await File.WriteAllTextAsync(Path.Combine(m_folder, "A.Child.NodeSet2.xml"), child).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(m_folder, "Z.Parent.NodeSet2.xml"), parent).ConfigureAwait(false);

            int code = await m_command.RunAsync([m_folder]).ConfigureAwait(false);
            Assert.That(code, Is.Zero, m_error.ToString());

            string target = Path.Combine(m_folder, "out", "A.Child.NodeSet2.xml");
            string[] paths = Directory.GetFiles(target);
            Assert.That(paths, Has.Length.EqualTo(1), "Dependency nodes must not become source documents.");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths[0]).ConfigureAwait(false));
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("@type").EnumerateArray().Select(t => t.GetString()),
                Does.Contain(expectedType));
            if (expectedType == "uav:eventType")
            {
                JsonElement data = root.GetProperty("data");
                Assert.That(data.GetProperty("properties").TryGetProperty("EventId", out _), Is.True);
            }
            else
            {
                Assert.That(root.TryGetProperty("data", out _), Is.False);
            }
        }

        /// <summary>
        /// Matches catalogue URNs without deriving identity from display text or the BrowseName namespace.
        /// </summary>
        [TestCase("http://opcfoundation.org/UA/ADI/", "1:PumpType", "urn:org.opcfoundation.UA.ADI.PumpType")]
        [TestCase("http://opcfoundation.org/UA/ADI/", "PumpType", "urn:org.opcfoundation.UA.ADI.PumpType")]
        [TestCase("http://opcfoundation.org/UA/ADI/", "1:AccessorySlotStateMachineType",
            "urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType")]
        [TestCase("http://opcfoundation.org/UA/", "PumpType", "urn:org.opcfoundation.UA.PumpType")]
        [TestCase("https://opcfoundation.org/UA/MTConnect/v2/", "1:PumpType",
            "urn:org.opcfoundation.UA.MTConnect.v2.PumpType")]
        [TestCase("http://www.opcfoundation.org/UA/2013/01/ISA95", "1:PumpType",
            "urn:org.opcfoundation.www.UA.2013.01.ISA95.PumpType")]
        [TestCase("http://fdi-cooperation.com/OPCUA/FDI5/", "1:PumpType",
            "urn:com.fdi-cooperation.OPCUA.FDI5.PumpType")]
        [TestCase("urn:export:test", "1:PumpType", "urn:export:test.PumpType")]
        public async Task ExportGeneratesBaselineModelIdsAsync(
            string namespaceUri, string browseName, string expectedId)
        {
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            string xml = k_nodeSet.Replace("urn:export:test", namespaceUri, StringComparison.Ordinal)
                .Replace("BrowseName=\"1:PumpType\"", $"BrowseName=\"{browseName}\"", StringComparison.Ordinal)
                .Replace("<DisplayName>PumpType</DisplayName>", "<DisplayName>A pump</DisplayName>",
                    StringComparison.Ordinal);
            if (namespaceUri == Types.Namespaces.OpcUa)
            {
                xml = xml.Replace("ns=1;i=", "i=100000", StringComparison.Ordinal)
                    .Replace("BrowseName=\"1:", "BrowseName=\"0:", StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(source, xml).ConfigureAwait(false);

            Assert.That(await m_command.RunAsync([source]).ConfigureAwait(false), Is.Zero, m_error.ToString());

            string target = Path.Combine(m_folder, "out", "Test.NodeSet2.xml", "model.TM.json");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(target).ConfigureAwait(false));
            Assert.That(document.RootElement.GetProperty("id").GetString(), Is.EqualTo(expectedId));
            Assert.That(document.RootElement.GetProperty("title").GetString(), Is.EqualTo("A pump"));
            string nativeId = namespaceUri == Types.Namespaces.OpcUa ? "i=1000001" : $"nsu={namespaceUri};i=1";
            Assert.That(document.RootElement.GetProperty("uav:id").GetString(), Is.EqualTo(nativeId));
        }

        /// <summary>
        /// Gives every supported type-root kind its own document identity.
        /// </summary>
        [TestCase("UAVariableType", "i=62")]
        [TestCase("UADataType", "i=12")]
        [TestCase("UAReferenceType", "i=31")]
        public async Task ExportGeneratesIdsForOtherTypeKindsAsync(string nodeClass, string parentId)
        {
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            string xml = k_nodeSet.Replace("UAObjectType", nodeClass, StringComparison.Ordinal)
                .Replace(">i=58<", $">{parentId}<", StringComparison.Ordinal);
            await File.WriteAllTextAsync(source, xml).ConfigureAwait(false);

            Assert.That(await m_command.RunAsync([source]).ConfigureAwait(false), Is.Zero, m_error.ToString());

            string target = Path.Combine(m_folder, "out", "Test.NodeSet2.xml", "model.TM.json");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(target).ConfigureAwait(false));
            Assert.That(document.RootElement.GetProperty("id").GetString(),
                Is.EqualTo("urn:export:test.PumpType"));
        }

        /// <summary>
        /// Disambiguates duplicate native names in source order, including instance names.
        /// </summary>
        [Test]
        public async Task DuplicateNamesReceiveBaselineSuffixesAsync()
        {
            const string xml = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>http://opcfoundation.org/UA/AMLLibs/</Uri></NamespaceUris>
              <UAObject NodeId="ns=1;i=100" BrowseName="Communication">
                <DisplayName>Communication</DisplayName>
                <References><Reference ReferenceType="i=40">i=58</Reference></References>
              </UAObject>
              <UAObjectType NodeId="ns=1;i=40" BrowseName="Communication">
                <DisplayName>Communication</DisplayName>
                <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
              </UAObjectType>
              <UAObjectType NodeId="ns=1;i=179" BrowseName="Communication">
                <DisplayName>Communication</DisplayName>
                <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
              </UAObjectType>
            </UANodeSet>
            """;
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            await File.WriteAllTextAsync(source, xml).ConfigureAwait(false);

            Assert.That(await m_command.RunAsync([source]).ConfigureAwait(false), Is.Zero, m_error.ToString());

            var ids = new Dictionary<string, string?>(StringComparer.Ordinal);
            string target = Path.Combine(m_folder, "out", "Test.NodeSet2.xml");
            foreach (string path in Directory.GetFiles(target))
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("id", out JsonElement id))
                {
                    ids.Add(root.GetProperty("uav:id").GetString()!, id.GetString());
                }
            }
            Assert.That(ids, Has.Count.EqualTo(2));
            Assert.That(ids["nsu=http://opcfoundation.org/UA/AMLLibs/;i=40"],
                Is.EqualTo("urn:org.opcfoundation.UA.AMLLibs.Communication_2"));
            Assert.That(ids["nsu=http://opcfoundation.org/UA/AMLLibs/;i=179"],
                Is.EqualTo("urn:org.opcfoundation.UA.AMLLibs.Communication_3"));
        }

        /// <summary>
        /// Keeps an authored document identity carried through native residue.
        /// </summary>
        [Test]
        public async Task ExportPreservesAuthoredModelIdAsync()
        {
            using var original = WotDocument.Parse(Encoding.UTF8.GetBytes(
                /*lang=json,strict*/
                """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "@type": ["tm:ThingModel", "uav:objectType"],
              "id": "urn:catalogue:authored:pump",
              "title": "Pump",
              "uav:id": "nsu=urn:export:test;i=1",
              "uav:browseName": "nsu=urn:export:test;PumpType"
            }
            """));
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            using (var buffer = new MemoryStream())
            {
                nodeSet.Write(buffer);
                await File.WriteAllBytesAsync(source, buffer.ToArray()).ConfigureAwait(false);
            }

            Assert.That(await m_command.RunAsync([source]).ConfigureAwait(false), Is.Zero, m_error.ToString());

            string target = Path.Combine(m_folder, "out", "Test.NodeSet2.xml", "model.TM.json");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(target).ConfigureAwait(false));
            Assert.That(document.RootElement.GetProperty("id").GetString(), Is.EqualTo(original.Id));
        }

        /// <summary>
        /// Keeps processing after a malformed source and returns a failing exit code.
        /// </summary>
        [Test]
        public async Task InvalidFileDoesNotStopOtherFilesAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(m_folder, "A.NodeSet2.xml"), "<broken>").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(m_folder, "B.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            Assert.That(await m_command.RunAsync(
                [Path.Combine(m_folder, "A.NodeSet2.xml"), Path.GetPathRoot(m_folder)!]).ConfigureAwait(false),
                Is.EqualTo(2));
            string target = Path.Combine(m_folder, "result");
            int code = await m_command.RunAsync([m_folder, target]).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(m_error.ToString(), Does.Contain("A.NodeSet2.xml").And.Contain("ERROR:"));
            Assert.That(Directory.GetFiles(target, "*", SearchOption.AllDirectories), Has.Length.EqualTo(2));
        }

        /// <summary>
        /// Excludes an invalid native type context without preventing valid models from being exported.
        /// </summary>
        [Test]
        public async Task InvalidTypeContextDoesNotStopOtherFilesAsync()
        {
            string duplicate = k_nodeSet.Replace(
                "NodeId=\"ns=1;i=2\"", "NodeId=\"ns=1;i=1\"", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(m_folder, "A.NodeSet2.xml"), duplicate).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(m_folder, "B.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);

            int code = await m_command.RunAsync([m_folder]).ConfigureAwait(false);

            Assert.That(code, Is.EqualTo(1));
            Assert.That(m_error.ToString(), Does.Contain("A.NodeSet2.xml").And.Contain("ERROR:"));
            Assert.That(Directory.GetFiles(Path.Combine(m_folder, "out"), "*", SearchOption.AllDirectories),
                Has.Length.EqualTo(2));
        }

        /// <summary>
        /// Accepts an individual file and rejects output that contains the source.
        /// </summary>
        [Test]
        public async Task FileSourceUsesItsParentForDefaultOutputAsync()
        {
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            await File.WriteAllTextAsync(source, k_nodeSet).ConfigureAwait(false);
            Assert.That(await m_command.RunAsync([source, m_folder]).ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await m_command.RunAsync([source]).ConfigureAwait(false), Is.Zero, m_error.ToString());
            Assert.That(Directory.GetFiles(Path.Combine(m_folder, "out"), "*", SearchOption.AllDirectories),
                Has.Length.EqualTo(2));
        }

        /// <summary>
        /// Stops before output when the caller cancels.
        /// </summary>
        [Test]
        public async Task CancellationReturnsDistinctExitCodeAsync()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            int code = await m_command.RunAsync([m_folder], cancellation.Token).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(130));
            Assert.That(m_error.ToString(), Does.Contain("stopped"));
            Assert.That(Directory.EnumerateFileSystemEntries(m_folder), Is.Empty);
        }

        /// <summary>
        /// Stops discovery when cancellation arrives after enumeration has begun.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationInterruptsDiscoveryAsync(bool nested)
        {
            await File.WriteAllTextAsync(Path.Combine(m_folder, "A.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            string remaining = nested ? Path.Combine(m_folder, "child") : m_folder;
            Directory.CreateDirectory(remaining);
            await File.WriteAllTextAsync(Path.Combine(remaining, "B.NodeSet2.xml"), k_nodeSet).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            using IEnumerator<string> files = ExportCommand.FindNodeSets(
                m_folder, Path.Combine(m_folder, "out"), cancellation.Token).GetEnumerator();
            Assert.That(files.MoveNext(), Is.True);

            await cancellation.CancelAsync().ConfigureAwait(false);

            Assert.That(files.MoveNext, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.Exists(Path.Combine(m_folder, "out")), Is.False);
        }

        /// <summary>
        /// Contains two independent type roots.
        /// </summary>
        private const string k_nodeSet = """
        <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
          <NamespaceUris><Uri>urn:export:test</Uri></NamespaceUris>
          <Models><Model ModelUri="urn:export:test" Version="1.0" /></Models>
          <UAObjectType NodeId="ns=1;i=1" BrowseName="1:PumpType">
            <DisplayName>PumpType</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
          </UAObjectType>
          <UAObjectType NodeId="ns=1;i=2" BrowseName="1:ValveType">
            <DisplayName>ValveType</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
          </UAObjectType>
        </UANodeSet>
        """;

        /// <summary>Stores this test's files.</summary>
        private string m_folder = null!;

        /// <summary>Captures progress messages.</summary>
        private StringWriter m_output = null!;

        /// <summary>Captures diagnostics.</summary>
        private StringWriter m_error = null!;

        /// <summary>Runs this test's command.</summary>
        private ExportCommand m_command = null!;
    }
}
