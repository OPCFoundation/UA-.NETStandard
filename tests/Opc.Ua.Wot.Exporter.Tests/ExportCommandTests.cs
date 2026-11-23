// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            m_command = new ExportCommand(m_output, m_error, getEnvironmentVariable: _ => null);
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
        /// Rejects missing option values, duplicate options and registry URLs with ambiguous components.
        /// </summary>
        [TestCase("--registry-base-url")]
        [TestCase("--group-id")]
        [TestCase("--registry-base-url", "urn:registry")]
        [TestCase("--registry-base-url", "https://example.test?query")]
        [TestCase("--registry-base-url", "https://example.test/#fragment")]
        [TestCase("--registry-base-url", "https://user@example.test")]
        [TestCase("--registry-base-url", "https://one.test", "--registry-base-url", "https://two.test")]
        [TestCase("--group-id", "..")]
        [TestCase("--group-id", "")]
        public async Task InvalidRegistryOptionsReturnUsageErrorAsync(params string[] options)
        {
            int code = await m_command.RunAsync([m_folder, .. options]).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(2));
            Assert.That(Directory.EnumerateFileSystemEntries(m_folder), Is.Empty);
        }

        /// <summary>
        /// Resolves cross-file inheritance and instance type links using the target's actual namespace version.
        /// </summary>
        [TestCase("A", "Z", "42")]
        [TestCase("Z", "A", "42")]
        [TestCase("A", "Z", null)]
        public async Task RegistryLinksUseTargetNamespaceVersionAsync(
            string childPrefix, string parentPrefix, string? parentVersion)
        {
            const string child = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:export:child</Uri><Uri>http://opcfoundation.org/UA/ADI/</Uri>
              </NamespaceUris>
              <Models><Model ModelUri="urn:export:child" Version="1.0">
                <RequiredModel ModelUri="http://opcfoundation.org/UA/ADI/" Version="41" />
              </Model></Models>
              <UAObjectType NodeId="ns=1;i=1" BrowseName="1:ChildType">
                <References><Reference ReferenceType="i=45" IsForward="false">ns=2;i=1</Reference></References>
              </UAObjectType>
              <UAObject NodeId="ns=1;i=2" BrowseName="1:Instance">
                <References><Reference ReferenceType="i=40">ns=2;i=1</Reference></References>
              </UAObject>
            </UANodeSet>
            """;
            string parent = k_nodeSet
                .Replace("urn:export:test", "http://opcfoundation.org/UA/ADI/", StringComparison.Ordinal)
                .Replace("PumpType", "AccessorySlotStateMachineType", StringComparison.Ordinal)
                .Replace("Version=\"1.0\"", parentVersion is null ? string.Empty : $"Version=\"{parentVersion}\"",
                    StringComparison.Ordinal);
            string childName = childPrefix + ".Child.NodeSet2.xml";
            string parentName = parentPrefix + ".Parent.NodeSet2.xml";
            await File.WriteAllTextAsync(Path.Combine(m_folder, childName), child).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(m_folder, parentName), parent).ConfigureAwait(false);

            int code = await m_command.RunAsync([m_folder]).ConfigureAwait(false);

            if (parentVersion is null)
            {
                Assert.That(code, Is.EqualTo(1));
                Assert.That(m_error.ToString(), Does.Contain("No valid Model Version")
                    .And.Contain("http://opcfoundation.org/UA/ADI/"));
                Assert.That(Directory.Exists(Path.Combine(m_folder, "out")), Is.False);
                return;
            }
            Assert.That(code, Is.Zero, m_error.ToString());
            const string expectedHref = "https://opcfoundation.org/thingmodelgroups/opcua/thingmodels/" +
                "aacc8e0db1e57bb41a53019c162369a4f63680be6ee5c292d67f70f613274d26/versions/42";
            string[] childPaths = Directory.GetFiles(Path.Combine(m_folder, "out", childName));
            Assert.That(childPaths, Has.Length.EqualTo(2));
            foreach (string path in childPaths)
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
                JsonElement root = document.RootElement;
                JsonElement link = root.GetProperty("links")[0];
                Assert.That(link.GetProperty("href").GetString(), Is.EqualTo(expectedHref));
                Assert.That(link.GetProperty("type").GetString(), Is.EqualTo("application/tm+json"));
                bool type = root.GetProperty("uav:id").GetString() == "nsu=urn:export:child;i=1";
                Assert.That(link.GetProperty("rel").GetString(),
                    Is.EqualTo(type ? "tm:extends" : "ua:HasTypeDefinition"));
                if (type)
                {
                    Assert.That(root.GetProperty("version").GetProperty("model").GetString(), Is.EqualTo("1.0"));
                }
            }
            using var target = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(m_folder, "out", parentName, "model.TM.json")).ConfigureAwait(false));
            Assert.That(target.RootElement.GetProperty("id").GetString(),
                Is.EqualTo("urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType"));
            Assert.That(target.RootElement.GetProperty("version").GetProperty("model").GetString(), Is.EqualTo("42"));
            using var exported = new WotDocumentSet("model.TM.json", [
                new WotDocumentSetEntry("model.TM.json", WotDocument.Parse(await File.ReadAllBytesAsync(
                    Path.Combine(m_folder, "out", childName, "model.TM.json")).ConfigureAwait(false)))
            ]);
            WotConversionResult<UANodeSet> restored = await WotNodeSetConverter.ToNodeSetAsync(exported)
                .ConfigureAwait(false);
            Assert.That(restored.Success, Is.True,
                string.Join(Environment.NewLine, restored.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            UAObjectType restoredType = restored.Value!.Items!.OfType<UAObjectType>().Single();
            Assert.That(restoredType.References!.Any(reference => !reference.IsForward &&
                reference.ReferenceType is "HasSubtype" or "i=45" &&
                reference.Value == "ns=2;i=1"), Is.True);
        }

        /// <summary>
        /// Applies environment configuration and CLI overrides to sibling models and encodes path components.
        /// </summary>
        [TestCase("https://example.test/registry/", false)]
        [TestCase("https://ignored.test", true)]
        [TestCase("not a URL", true)]
        public async Task RegistryOptionsApplyToLocalModelLinksAsync(string environmentUrl, bool useCli)
        {
            string source = Path.Combine(m_folder, "Test.NodeSet2.xml");
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(k_nodeSet)))
            {
                var nodeSet = UANodeSet.Read(input);
                nodeSet.Items![1].References![0].Value = "ns=1;i=1";
                using var buffer = new MemoryStream();
                nodeSet.Write(buffer);
                await File.WriteAllBytesAsync(source, buffer.ToArray()).ConfigureAwait(false);
            }

            var command = new ExportCommand(m_output, m_error, getEnvironmentVariable:
                name => name == "OPCUA_WOT_REGISTRY_BASE_URL" ? environmentUrl : null);
            string[] args = useCli
                ? ["--registry-base-url", "https://example.test/registry/", source, "--group-id", "OPC UA/models"]
                : [source, "--group-id", "OPC UA/models"];
            int code = await command.RunAsync(args).ConfigureAwait(false);

            Assert.That(code, Is.Zero, m_error.ToString());
            string target = Path.Combine(m_folder, "out", "Test.NodeSet2.xml", "model.TM.json-valvetype");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(target).ConfigureAwait(false));
            JsonElement link = document.RootElement.GetProperty("links")[0];
            Assert.That(link.GetProperty("href").GetString(),
                Does.StartWith("https://example.test/registry/thingmodelgroups/OPC%20UA%2Fmodels/thingmodels/")
                    .And.EndsWith("/versions/1.0"));
            Assert.That(link.GetProperty("rel").GetString(), Is.EqualTo("tm:extends"));
        }

        /// <summary>
        /// Rejects an invalid environment URL when no CLI override is supplied.
        /// </summary>
        [Test]
        public async Task InvalidRegistryEnvironmentReturnsUsageErrorAsync()
        {
            var command = new ExportCommand(m_output, m_error, getEnvironmentVariable: _ => "not a URL");

            int code = await command.RunAsync([m_folder]).ConfigureAwait(false);

            Assert.That(code, Is.EqualTo(2));
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
        [TestCase("i=2041", "uav:eventType", false)]
        [TestCase("i=2782", "uav:eventType", false)]
        [TestCase("i=2782", "uav:eventType", true)]
        [TestCase("i=2915", "uav:eventType", true)]
        [TestCase("i=58", "uav:objectType", false)]
        [TestCase("i=999999", "uav:objectType", false)]
        [TestCase("ns=1;i=1", "uav:objectType", false)]
        public async Task ExportResolvesRequiredModelEventAncestryAsync(
            string ancestorId,
            string expectedType,
            bool additionalRoot)
        {
            string child = """
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
            if (additionalRoot)
            {
                child = child.Replace("</UANodeSet>", """
                  <UAObjectType NodeId="ns=1;i=2" BrowseName="1:OtherType">
                    <DisplayName>OtherType</DisplayName>
                    <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
                  </UAObjectType>
                </UANodeSet>
                """, StringComparison.Ordinal);
            }
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
            Assert.That(paths, Has.Length.EqualTo(additionalRoot ? 2 : 1),
                "Dependency nodes must not become source documents.");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(target, "model.TM.json")).ConfigureAwait(false));
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("@type").EnumerateArray().Select(t => t.GetString()),
                Does.Contain(expectedType));
            if (expectedType == "uav:eventType")
            {
                JsonElement data = root.GetProperty("data");
                Assert.That(data.GetProperty("properties").TryGetProperty("EventId", out _), Is.True);
                if (ancestorId is "i=2782" or "i=2915")
                {
                    Assert.That(data.GetProperty("properties").TryGetProperty("ConditionId", out _), Is.True);
                    Assert.That(data.GetProperty("properties").TryGetProperty("Retain", out _), Is.True);
                    Assert.That(data.GetProperty("required").EnumerateArray().Select(field => field.GetString()),
                        Does.Contain("ConditionId").And.Contain("Retain"));
                }
            }
            else
            {
                Assert.That(root.TryGetProperty("data", out _), Is.False);
            }
        }

        /// <summary>
        /// Resolves shared argument properties through forward references without importing dependency nodes.
        /// </summary>
        [TestCase("A", "Z", "valid")]
        [TestCase("Z", "A", "valid")]
        [TestCase("A", "Z", "missing")]
        [TestCase("A", "Z", "invalid")]
        [TestCase("A", "Z", "empty")]
        public async Task ExportResolvesRequiredModelMethodArgumentsAsync(
            string sourcePrefix,
            string dependencyPrefix,
            string metadata)
        {
            const string source = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:export:child</Uri><Uri>urn:export:arguments</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:export:child" Version="1.0">
                <RequiredModel ModelUri="urn:export:arguments" Version="1.0" />
              </Model></Models>
              <UAObjectType NodeId="ns=1;i=1" BrowseName="1:AssetType">
                <DisplayName>AssetType</DisplayName>
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=7000</Reference>
                </References>
              </UAObjectType>
              <UAMethod NodeId="ns=1;i=7000" BrowseName="2:VerifyAsset" ParentNodeId="ns=1;i=1">
                <DisplayName>VerifyAsset</DisplayName>
                <References>
                  <Reference ReferenceType="i=46">ns=2;i=1422</Reference>
                  <Reference ReferenceType="i=46">ns=2;i=1423</Reference>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1</Reference>
                </References>
              </UAMethod>
            </UANodeSet>
            """;
            const string dependency = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                       xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <NamespaceUris><Uri>urn:export:arguments</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:export:arguments" Version="1.0" /></Models>
              <UAObjectType NodeId="ns=1;i=1" BrowseName="1:SharedAssetType">
                <DisplayName>SharedAssetType</DisplayName>
                <References><Reference ReferenceType="i=47">ns=1;i=1476</Reference></References>
              </UAObjectType>
              <UAMethod NodeId="ns=1;i=1476" BrowseName="1:VerifyAsset" ParentNodeId="ns=1;i=1">
                <References>
                  <Reference ReferenceType="i=46">ns=1;i=1422</Reference>
                  <Reference ReferenceType="i=46">ns=1;i=1423</Reference>
                </References>
              </UAMethod>
              <UAVariable NodeId="ns=1;i=1422" BrowseName="InputArguments" ParentNodeId="ns=1;i=1476"
                          DataType="i=296" ValueRank="1" ArrayDimensions="1">
                <References><Reference ReferenceType="i=40">i=68</Reference></References>
                <Value><uax:ListOfExtensionObject><uax:ExtensionObject>
                  <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                  <uax:Body><uax:Argument>
                    <uax:Name>VerificationMode</uax:Name>
                    <uax:DataType><uax:Identifier>ns=1;i=3001</uax:Identifier></uax:DataType>
                    <uax:ValueRank>-1</uax:ValueRank><uax:ArrayDimensions />
                  </uax:Argument></uax:Body>
                </uax:ExtensionObject></uax:ListOfExtensionObject></Value>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=1423" BrowseName="OutputArguments" ParentNodeId="ns=1;i=1476"
                          DataType="i=296" ValueRank="1" ArrayDimensions="1">
                <References><Reference ReferenceType="i=40">i=68</Reference></References>
                <Value><uax:ListOfExtensionObject><uax:ExtensionObject>
                  <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                  <uax:Body><uax:Argument>
                    <uax:Name>VerificationErrors</uax:Name>
                    <uax:DataType><uax:Identifier>i=19</uax:Identifier></uax:DataType>
                    <uax:ValueRank>1</uax:ValueRank>
                    <uax:ArrayDimensions><uax:UInt32>0</uax:UInt32></uax:ArrayDimensions>
                  </uax:Argument></uax:Body>
                </uax:ExtensionObject></uax:ListOfExtensionObject></Value>
              </UAVariable>
              <UADataType NodeId="ns=1;i=3001" BrowseName="1:VerificationModeEnum">
                <References><Reference ReferenceType="i=45" IsForward="false">i=29</Reference></References>
                <Definition Name="1:VerificationModeEnum"><Field Name="Identity" Value="0" /></Definition>
              </UADataType>
            </UANodeSet>
            """;
            string sourceName = sourcePrefix + ".Child.NodeSet2.xml";
            await File.WriteAllTextAsync(Path.Combine(m_folder, sourceName), source).ConfigureAwait(false);
            if (metadata != "missing")
            {
                string dependencyText = metadata == "invalid"
                    ? dependency.Replace(
                        "<uax:Identifier>i=297</uax:Identifier>",
                        "<uax:Identifier>i=999</uax:Identifier>", StringComparison.Ordinal)
                    : dependency;
                if (metadata == "empty")
                {
                    var xml = new System.Xml.XmlDocument();
                    xml.LoadXml(dependency);
                    foreach (System.Xml.XmlElement arguments in xml.GetElementsByTagName(
                        "ListOfExtensionObject", "http://opcfoundation.org/UA/2008/02/Types.xsd"))
                    {
                        arguments.RemoveAll();
                    }
                    dependencyText = xml.OuterXml;
                }
                await File.WriteAllTextAsync(
                    Path.Combine(m_folder, dependencyPrefix + ".Arguments.NodeSet2.xml"), dependencyText)
                    .ConfigureAwait(false);
            }

            int code = await m_command.RunAsync([m_folder]).ConfigureAwait(false);
            if (metadata is "missing" or "invalid")
            {
                Assert.That(code, Is.EqualTo(1));
                Assert.That(m_error.ToString(),
                    Does.Contain("argument metadata").And.Contain("nsu=urn:export:arguments;i=1422"));
                Assert.That(Directory.Exists(Path.Combine(m_folder, "out", sourceName)), Is.False);
                return;
            }
            Assert.That(code, Is.Zero, m_error.ToString());

            string[] paths = Directory.GetFiles(Path.Combine(m_folder, "out", sourceName));
            Assert.That(paths, Has.Length.EqualTo(1), "Dependency nodes must remain in their own source partition.");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths[0]).ConfigureAwait(false));
            JsonElement action = document.RootElement.GetProperty("actions").GetProperty("VerifyAsset");
            if (metadata == "empty")
            {
                Assert.That(action.TryGetProperty("input", out _), Is.False);
                Assert.That(action.TryGetProperty("output", out _), Is.False);
                return;
            }
            Assert.That(action.TryGetProperty("input", out JsonElement input), Is.True);
            Assert.That(action.TryGetProperty("output", out JsonElement output), Is.True);
            Assert.That(input.GetProperty("uav:argumentLayout").GetString(), Is.EqualTo("named"));
            Assert.That(input.GetProperty("uav:fieldOrder")[0].GetString(), Is.EqualTo("VerificationMode"));
            JsonElement mode = input.GetProperty("properties").GetProperty("VerificationMode");
            Assert.That(mode.GetProperty("type").GetString(), Is.EqualTo("integer"));
            Assert.That(mode.GetProperty("uav:mapToType").GetString(),
                Is.EqualTo("nsu=urn:export:arguments;i=3001"));
            Assert.That(output.GetProperty("uav:argumentLayout").GetString(), Is.EqualTo("named"));
            Assert.That(output.GetProperty("uav:fieldOrder")[0].GetString(), Is.EqualTo("VerificationErrors"));
            JsonElement errors = output.GetProperty("properties").GetProperty("VerificationErrors");
            Assert.That(errors.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(errors.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(1));
            Assert.That(errors.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=19"));

            using var dependencyXml = new MemoryStream(Encoding.UTF8.GetBytes(dependency));
            var resolver = WotDocumentNodeResolver.FromNodeSets([UANodeSet.Read(dependencyXml)]);
            foreach (string argumentName in (string[])["VerificationMode", "VerificationErrors"])
            {
                string changed = document.RootElement.GetRawText().Replace(
                    "\"" + argumentName + "\"", "\"ChangedArgument\"", StringComparison.Ordinal);
                using var tampered = WotDocument.Parse(Encoding.UTF8.GetBytes(changed));
                WotConversionResult<UANodeSet> restored = await WotNodeSetConverter.ToNodeSetResultAsync(
                    tampered, null, null, null, resolver).ConfigureAwait(false);
                Assert.That(restored.Success, Is.False);
                Assert.That(restored.Diagnostics.Any(
                    diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict), Is.True);
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
              <Models><Model ModelUri="http://opcfoundation.org/UA/AMLLibs/" Version="1.0" /></Models>
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
            nodeSet.Models = [new ModelTableEntry { ModelUri = "urn:export:test", Version = "2.3" }];
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
