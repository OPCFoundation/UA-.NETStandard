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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Export;
using UaLens.NodeSets.Loading;

namespace UaLens.Tests.NodeSets.Loading
{
    [TestFixture]
    internal sealed class NodeSetLoaderTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine("tests", "Opc.Ua.Lens.Tests", "NodeSets", "Loading", ".test-work",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
            m_resolver = new Mock<INodeSetDependencyResolver>(MockBehavior.Strict);
            m_loader = new NodeSetLoader(() => new MemoryStream(Encoding.UTF8.GetBytes(Core)));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_directory, true);
        }

        [Test]
        public async Task LoadsLocalTransitiveDependenciesAndCyclesWithoutInspectingUnrelatedBodies()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:a")).ConfigureAwait(false);
            string first = await WriteAsync("a.xml", Model("urn:a", "urn:b")).ConfigureAwait(false);
            string second = await WriteAsync("b.xml", Model("urn:b", "urn:a")).ConfigureAwait(false);
            await WriteAsync("aaa-unrelated.xml", "<unrelated><broken").ConfigureAwait(false);
            await WriteAsync("aab-unrelated.xml", Model("urn:unrelated").Replace(
                "</UANodeSet>", "<broken", StringComparison.Ordinal)).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.ToArray()!.Select(static document => document.Source),
                Is.EquivalentTo([ Path.GetFullPath(app), Path.GetFullPath(first), Path.GetFullPath(second),
                    NodeSetLoader.CoreResourceName ]));
            m_resolver.VerifyNoOtherCalls();
        }

        [Test]
        public async Task SelectedMultiModelDocumentSuppliesBothDependenciesAndDuplicatePathsAreIgnored()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:a")).ConfigureAwait(false);
            string both = await WriteAsync("both.xml", Xml(
                """<Model ModelUri="urn:a"><RequiredModel ModelUri="urn:b"/></Model><Model ModelUri="urn:b"/>"""))
                .ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync(
                [app, both, Path.GetFullPath(both)], m_resolver.Object).ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
            m_resolver.VerifyNoOtherCalls();
        }

        [Test]
        public async Task SuppliedCoreReplacesEmbeddedCore()
        {
            string core = await WriteAsync("core.xml", Core).ConfigureAwait(false);
            var loader = new NodeSetLoader(() => throw new AssertionException("Embedded core must not be read."));
            ArrayOf<NodeSetDocument> documents = await loader.LoadAsync([core], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(1));
            Assert.That(documents[0].Source, Is.EqualTo(Path.GetFullPath(core)));
        }

        [Test]
        public async Task LegacyDocumentsResolveOnlyActuallyReferencedNamespacesIncludingAliasesAndValues()
        {
            string app = await WriteAsync("legacy.xml", """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                       xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <NamespaceUris><Uri>urn:app</Uri><Uri>urn:dependency</Uri><Uri>urn:unused</Uri></NamespaceUris>
              <Aliases><Alias Alias="CustomType">ns=2;i=1</Alias></Aliases>
              <UAVariable NodeId="ns=1;i=1" BrowseName="1:Variable" DataType="CustomType">
                <Value><uax:NodeId><uax:Identifier>ns=2;i=2</uax:Identifier></uax:NodeId></Value>
              </UAVariable>
            </UANodeSet>
            """).ConfigureAwait(false);
            await WriteAsync("dependency.xml", Model("urn:dependency")).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
            m_resolver.VerifyNoOtherCalls();
        }

        [Test]
        public async Task UnusedNamespaceUrisAreIgnoredWhenModelsArePresent()
        {
            string app = await WriteAsync("app.xml", Xml(
                """<Model ModelUri="urn:app"/>""", "<NamespaceUris><Uri>urn:unused</Uri></NamespaceUris>"))
                .ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(2));
        }

        [TestCase("HasProperty")]
        [TestCase("VendorReference")]
        public async Task ReferenceNamesRequireDeclarationsAndPreserveAuthoredAliases(string alias)
        {
            string xml = $$"""
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:app</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:app" /></Models>
              <UAObject NodeId="ns=1;i=1" BrowseName="1:Root">
                <References><Reference ReferenceType="{{alias}}">ns=1;i=2</Reference></References>
              </UAObject>
            </UANodeSet>
            """;
            string path = await WriteAsync("alias.xml", xml).ConfigureAwait(false);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([path], m_resolver.Object).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain(alias).And.Contain("<Aliases>"));

            await WriteAsync("alias.xml", xml.Replace("<UAObject",
                $"""<Aliases><Alias Alias="{alias}">i=47</Alias></Aliases><UAObject""",
                StringComparison.Ordinal)).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([path], m_resolver.Object)
                .ConfigureAwait(false);
            NodeSetDocument document = documents.ToList().Single(value => value.Source == Path.GetFullPath(path));
            Assert.That(document.NodeSet.Aliases, Has.Length.EqualTo(1));
            Assert.That(document.NodeSet.Aliases![0].Alias, Is.EqualTo(alias));
            Assert.That(document.NodeSet.Aliases[0].Value, Is.EqualTo("i=47"));
            m_resolver.VerifyNoOtherCalls();
        }

        [TestCase("Reference")]
        [TestCase("ReferenceType")]
        [TestCase("DataType")]
        [TestCase("Alias")]
        [TestCase("BrowseName")]
        [TestCase("ExpandedNodeId")]
        [TestCase("Definition")]
        [TestCase("Value")]
        public async Task UsedNamespacesSupplementIncompleteModelRequirements(string usage)
        {
            string node = usage switch
            {
                "Reference" => """
                    <UAObject NodeId="ns=1;i=1" BrowseName="1:App">
                      <References><Reference ReferenceType="i=35">ns=2;i=10</Reference></References>
                    </UAObject>
                    """,
                "ReferenceType" => """
                    <UAObject NodeId="ns=1;i=1" BrowseName="1:App">
                      <References><Reference ReferenceType="ns=2;i=10">i=85</Reference></References>
                    </UAObject>
                    """,
                "DataType" => """
                    <UAVariable NodeId="ns=1;i=1" BrowseName="1:App" DataType="ns=2;i=10"/>
                    """,
                "Alias" => """
                    <Aliases><Alias Alias="ForeignType">ns=2;i=10</Alias></Aliases>
                    <UAVariable NodeId="ns=1;i=1" BrowseName="1:App" DataType="ForeignType"/>
                    """,
                "BrowseName" => """
                    <UAObject NodeId="ns=1;i=1" BrowseName="2:App"/>
                    """,
                "ExpandedNodeId" => """
                    <UAObject NodeId="ns=1;i=1" BrowseName="1:App">
                      <References><Reference ReferenceType="i=35">nsu=urn:foreign;i=10</Reference></References>
                    </UAObject>
                    """,
                "Definition" => """
                    <UADataType NodeId="ns=1;i=1" BrowseName="1:App">
                      <Definition Name="App"><Field Name="Value" DataType="ns=2;i=10"/></Definition>
                    </UADataType>
                    """,
                "Value" => """
                    <UAVariable NodeId="ns=1;i=1" BrowseName="1:App" DataType="i=17">
                      <Value><NodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                        <Identifier>ns=2;i=10</Identifier>
                      </NodeId></Value>
                    </UAVariable>
                    """,
                _ => throw new AssertionException($"Unknown namespace usage '{usage}'.")
            };
            string namespaces = usage == "ExpandedNodeId" ?
                "<NamespaceUris><Uri>urn:app</Uri><Uri>urn:unused</Uri></NamespaceUris>" :
                "<NamespaceUris><Uri>urn:app</Uri><Uri>urn:foreign</Uri><Uri>urn:unused</Uri></NamespaceUris>";
            string app = await WriteAsync("app.xml", Xml(
                """<Model ModelUri="urn:app"/>""", namespaces, node)).ConfigureAwait(false);
            string dependency = await WriteAsync("foreign.xml", Model("urn:foreign")).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
            Assert.That(documents.ToArray()!.Select(static document => document.Source),
                Does.Contain(Path.GetFullPath(dependency)));
            m_resolver.VerifyNoOtherCalls();
        }

        [TestCase("svr=1;ns=2;i=10")]
        [TestCase("svr=1;nsu=urn:remote;i=10")]
        public async Task RemoteServerReferenceTargetsDoNotRequireLocalModels(string target)
        {
            string app = await WriteAsync("app.xml", Xml(
                """<Model ModelUri="urn:app"/>""",
                """
                <NamespaceUris><Uri>urn:app</Uri><Uri>urn:remote</Uri></NamespaceUris>
                <ServerUris><Uri>urn:remote-server</Uri></ServerUris>
                """,
                $"""
                <UAObject NodeId="ns=1;i=1" BrowseName="1:App">
                  <References><Reference ReferenceType="i=35">{target}</Reference></References>
                </UAObject>
                """)).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(2));
            m_resolver.VerifyNoOtherCalls();
        }

        [Test]
        public async Task MalformedSelectedAndMatchingLocalFilesFailExplicitly()
        {
            string broken = await WriteAsync("broken.xml", Model("urn:broken").Replace(
                "</UANodeSet>", "<broken", StringComparison.Ordinal)).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([broken], m_resolver.Object).ConfigureAwait(false));
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:broken")).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([app], m_resolver.Object).ConfigureAwait(false));
        }

        [TestCase("""ModelVersion="1.0.0" """, """ModelVersion="2.0.0" """)]
        [TestCase("""PublicationDate="2024-01-01T00:00:00Z" """, """PublicationDate="2025-01-01T00:00:00Z" """)]
        [TestCase("""Version="first release" """, """Version="second release" """)]
        public async Task ConflictingSelectedModelsFailBeforeDependencyResolution(
            string firstRevision, string secondRevision)
        {
            string first = await WriteAsync("first.xml",
                Xml($"""<Model ModelUri="urn:model" {firstRevision}/>""")).ConfigureAwait(false);
            string second = await WriteAsync("second.xml",
                Xml($"""<Model ModelUri="urn:model" {secondRevision}/>""")).ConfigureAwait(false);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([first, second], m_resolver.Object).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("conflicting revisions").And.Contain("urn:model"));
        }

        [Test]
        public async Task SameRevisionPartitionsAreRetainedAndEachContributesDependencies()
        {
            const string revision = """ModelVersion="1.0.0" PublicationDate="2025-01-01T00:00:00Z" """;
            const string namespaces = "<NamespaceUris><Uri>urn:partitioned</Uri></NamespaceUris>";
            string first = await WriteAsync("first.xml", Xml(
                $"""<Model ModelUri="urn:partitioned" {revision}><RequiredModel ModelUri="urn:first"/></Model>""",
                namespaces, """<UAObject NodeId="ns=1;i=1" BrowseName="1:First"/>""")).ConfigureAwait(false);
            string second = await WriteAsync("second.xml", Xml(
                $"""<Model ModelUri="urn:partitioned" {revision}><RequiredModel ModelUri="urn:second"/></Model>""",
                namespaces, """<UAObject NodeId="ns=1;i=2" BrowseName="1:Second"/>""")).ConfigureAwait(false);
            await WriteAsync("first-dependency.xml", Model("urn:first")).ConfigureAwait(false);
            await WriteAsync("second-dependency.xml", Model("urn:second")).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([first, second], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(5));
            Assert.That(documents[0].Source, Is.EqualTo(Path.GetFullPath(first)));
            Assert.That(documents[1].Source, Is.EqualTo(Path.GetFullPath(second)));
            m_resolver.VerifyNoOtherCalls();
        }

        [Test]
        public async Task LegacyModelPartitionsWithTheSameUnspecifiedRevisionAreRetained()
        {
            const string namespaces = "<NamespaceUris><Uri>urn:partitioned</Uri></NamespaceUris>";
            string first = await WriteAsync("first.xml", Xml(
                string.Empty, namespaces, """<UAObject NodeId="ns=1;i=1" BrowseName="1:First"/>"""))
                .ConfigureAwait(false);
            string second = await WriteAsync("second.xml", Xml(
                string.Empty, namespaces, """<UAObject NodeId="ns=1;i=2" BrowseName="1:Second"/>"""))
                .ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([first, second], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task NullResolverResultCancelsTheWholeOpen()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:missing")).ConfigureAwait(false);
            m_resolver.Setup(value => value.ResolveAsync(
                It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>())).ReturnsAsync((NodeSetDocument?)null);
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_loader.LoadAsync([app], m_resolver.Object).ConfigureAwait(false));
        }

        [Test]
        public void CancellationBeforeOpenDoesNotReadFiles()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_loader.LoadAsync(["missing.xml"], m_resolver.Object, cancellation.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task ResolverCancellationPropagates()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:missing")).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            m_resolver.Setup(value => value.ResolveAsync(
                It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>()))
                .Returns<NodeSetRequirement, CancellationToken>(async (_, token) =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return null;
                });
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_loader.LoadAsync([app], m_resolver.Object, cancellation.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task ResolverCannotSubstituteAnotherModel()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:missing")).ConfigureAwait(false);
            NodeSetDocument wrong = await ReadAsync(Model("urn:wrong")).ConfigureAwait(false);
            m_resolver.Setup(value => value.ResolveAsync(
                It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>())).ReturnsAsync(wrong);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([app], m_resolver.Object).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("urn:missing"));
        }

        [Test]
        public async Task RejectsAnOlderSelectedDependencyRatherThanSilentlyReplacingIt()
        {
            string app = await WriteAsync("app.xml", Xml("""
            <Model ModelUri="urn:app">
              <RequiredModel ModelUri="urn:dependency" PublicationDate="2025-01-01T00:00:00Z"/>
            </Model>
            """,
                "<NamespaceUris><Uri>urn:app</Uri><Uri>urn:dependency</Uri></NamespaceUris>",
                """<UAVariable NodeId="ns=1;i=1" BrowseName="1:App" DataType="ns=2;i=10"/>"""))
                .ConfigureAwait(false);
            string old = await WriteAsync("old.xml", Xml(
                """<Model ModelUri="urn:dependency" PublicationDate="2024-01-01T00:00:00Z"/>""")).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([app, old], m_resolver.Object).ConfigureAwait(false));
            m_resolver.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DependencyDepthIsBounded()
        {
            string app = await WriteAsync("app.xml", Model("urn:0", "urn:1")).ConfigureAwait(false);
            m_resolver.Setup(value => value.ResolveAsync(
                It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>()))
                .Returns<NodeSetRequirement, CancellationToken>(async (requirement, token) =>
                {
                    int index = int.Parse(requirement.ModelUri.AsSpan(4), CultureInfo.InvariantCulture);
                    NodeSetDocument document = await ReadAsync(Model(requirement.ModelUri, $"urn:{index + 1}"))
                        .ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return document;
                });
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([app], m_resolver.Object).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("depth limit"));
        }

        [Test]
        public async Task ResolverDocumentsAlsoRespectTheTotalNodeLimit()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:huge")).ConfigureAwait(false);
            var huge = new NodeSetDocument("huge", new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = "urn:huge" }],
                Items = new UANode[NodeSetDocumentReader.MaxNodes + 1]
            });
            m_resolver.Setup(value => value.ResolveAsync(
                It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>())).ReturnsAsync(huge);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await m_loader.LoadAsync([app], m_resolver.Object).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("nodes"));
        }

        [Test]
        public async Task ReaderRejectsDtdAndExcessiveXmlDepth()
        {
            string dtd = await WriteAsync("dtd.xml", """
            <!DOCTYPE UANodeSet [<!ENTITY hostile SYSTEM "file:///not-accessed">]>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">&hostile;</UANodeSet>
            """).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await NodeSetDocument.ReadAsync(dtd).ConfigureAwait(false));
            string nested = string.Concat(Enumerable.Repeat("<x>", NodeSetDocumentReader.MaxXmlDepth + 1)) +
                string.Concat(Enumerable.Repeat("</x>", NodeSetDocumentReader.MaxXmlDepth + 1));
            string deep = await WriteAsync("deep.xml", Xml(string.Empty, nodes: nested)).ConfigureAwait(false);
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await NodeSetDocument.ReadAsync(deep).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("depth limit"));
        }

        [Test]
        public async Task DefaultEmbeddedCoreIsUsable()
        {
            string app = await WriteAsync("app.xml", Model("urn:app")).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await new NodeSetLoader().LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            NodeSetDocument core = documents.ToArray()!
                .Single(static document => document.Source == NodeSetLoader.CoreResourceName);
            Assert.That(core.NodeSet.Items!, Has.Length.GreaterThan(100));
        }

        [Test]
        public void ReaderRejectsOversizedFilesWithoutParsingOrModifyingThem()
        {
            string path = Path.Combine(m_directory, "oversized.xml");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                file.SetLength(NodeSetDocumentReader.MaxDocumentBytes + 1);
            }
            InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await NodeSetDocument.ReadAsync(path).ConfigureAwait(false));
            Assert.That(exception!.Message, Does.Contain("byte input limit"));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(NodeSetDocumentReader.MaxDocumentBytes + 1));
        }

        [Test]
        public void SelectedDocumentCountIsBounded()
        {
            string[] paths = [.. Enumerable.Repeat("not-read.xml", NodeSetLoader.MaxDocuments + 1)];
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_loader.LoadAsync(paths, m_resolver.Object).ConfigureAwait(false));
        }

        [Test]
        public async Task LegacyDataTypeBaseTypeIsAQualifiedNameNotANodeId()
        {
            string app = await WriteAsync("legacy.xml", """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:app</Uri><Uri>urn:base</Uri></NamespaceUris>
              <UADataType NodeId="ns=1;i=1" BrowseName="1:Derived">
                <Definition Name="Derived" BaseType="2:Base"/>
              </UADataType>
            </UANodeSet>
            """).ConfigureAwait(false);
            await WriteAsync("base.xml", Model("urn:base")).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task LegacySiblingWithEmptyModelsTableCanProvideAReferencedNamespace()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:dependency")).ConfigureAwait(false);
            await WriteAsync("dependency.xml", Xml(
                string.Empty,
                "<NamespaceUris><Uri>urn:dependency</Uri></NamespaceUris>",
                """<UAObject NodeId="ns=1;i=1" BrowseName="1:Node"/>""")).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task LegacyMultiNamespaceSiblingUsesActualNodeOwnershipNotNamespaceTableMembership()
        {
            string app = await WriteAsync("app.xml", Model("urn:app", "urn:second")).ConfigureAwait(false);
            await WriteAsync("aaa-reference-only.xml", Xml(
                string.Empty,
                "<NamespaceUris><Uri>urn:other</Uri><Uri>urn:second</Uri></NamespaceUris>",
                """<UAObject NodeId="ns=1;i=1" BrowseName="1:Other"/>""")).ConfigureAwait(false);
            string multi = await WriteAsync("multi.xml", Xml(
                string.Empty,
                "<NamespaceUris><Uri>urn:first</Uri><Uri>urn:second</Uri></NamespaceUris>",
                """
                <UAObject NodeId="ns=1;i=1" BrowseName="1:First"/>
                <UAObject NodeId="ns=2;i=1" BrowseName="2:Second"/>
                """)).ConfigureAwait(false);
            ArrayOf<NodeSetDocument> documents = await m_loader.LoadAsync([app], m_resolver.Object)
                .ConfigureAwait(false);
            Assert.That(documents.Count, Is.EqualTo(3));
            Assert.That(documents.ToArray()!.Select(static document => document.Source),
                Does.Contain(Path.GetFullPath(multi)));
        }

        internal static string Model(string uri, string? required = null)
        {
            string dependency = required is null ? string.Empty : $"""<RequiredModel ModelUri="{required}"/>""";
            return Xml($"""<Model ModelUri="{uri}">{dependency}</Model>""");
        }

        internal static string Xml(string models, string namespaces = "", string nodes = "")
        {
            return $"""
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              {namespaces}
              <Models>{models}</Models>
              {nodes}
            </UANodeSet>
            """;
        }

        internal static async Task<NodeSetDocument> ReadAsync(string xml)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return await NodeSetDocumentReader.ReadAsync(stream, "resolved.xml", default).ConfigureAwait(false);
        }

        private async Task<string> WriteAsync(string name, string xml)
        {
            string path = Path.Combine(m_directory, name);
            await File.WriteAllTextAsync(path, xml).ConfigureAwait(false);
            return path;
        }

        private const string Core = """
        <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
          <Models><Model ModelUri="http://opcfoundation.org/UA/"/></Models>
        </UANodeSet>
        """;

        private string m_directory = null!;
        private Mock<INodeSetDependencyResolver> m_resolver = null!;
        private NodeSetLoader m_loader = null!;
    }
}
