/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for how <see cref="NodesetFileCollection"/> resolves several
    /// NodeSet2 inputs that declare the same model URI.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetFileCollectionTests
    {
        private const string ModelUri = "http://test.org/UA/Versioned/";

        private VirtualFileSystem m_fileSystem;

        [SetUp]
        public void SetUp()
        {
            m_fileSystem = new VirtualFileSystem();
        }

        [TearDown]
        public void TearDown()
        {
            m_fileSystem?.Dispose();
            m_fileSystem = null;
        }

        /// <summary>
        /// Regression: versions were compared as ordinal text, under which
        /// "1.05.9" sorts above "1.05.10", so the older NodeSet won.
        /// </summary>
        [TestCase("1.05.9", "1.05.10", "memory://second.NodeSet2.xml")]
        [TestCase("1.05.10", "1.05.9", "memory://first.NodeSet2.xml")]
        public void NewestVersionWinsRegardlessOfOrdinalOrdering(
            string firstVersion,
            string secondVersion,
            string expectedWinner)
        {
            const string first = "memory://first.NodeSet2.xml";
            const string second = "memory://second.NodeSet2.xml";

            m_fileSystem.Add(first, Encoding.UTF8.GetBytes(NodeSet(firstVersion)));
            m_fileSystem.Add(second, Encoding.UTF8.GetBytes(NodeSet(secondVersion)));

            NodesetFileCollection collection = Create(
                (first, firstVersion), (second, secondVersion));

            Assert.That(collection.Files[ModelUri], Is.EqualTo(expectedWinner));
        }

        /// <summary>
        /// Regression: a newer NodeSet read before an older one for the same
        /// model URI was overwritten by the older one unconditionally.
        /// </summary>
        [Test]
        public void NewestVersionWinsWhenItIsReadFirst()
        {
            const string newer = "memory://a-newer.NodeSet2.xml";
            const string older = "memory://b-older.NodeSet2.xml";

            m_fileSystem.Add(newer, Encoding.UTF8.GetBytes(NodeSet("2.0.0")));
            m_fileSystem.Add(older, Encoding.UTF8.GetBytes(NodeSet("1.0.0")));

            NodesetFileCollection collection = Create((newer, "2.0.0"), (older, "1.0.0"));

            Assert.That(collection.Files[ModelUri], Is.EqualTo(newer));
        }

        /// <summary>
        /// Regression: Files exposes one entry per model URI, so a superseded
        /// version's path was not recognised as a NodeSet2 input and the
        /// ModelDesign pass picked it up and generated the model a second time.
        /// AllFilePaths reports every loaded file.
        /// </summary>
        [Test]
        public void AllFilePathsIncludesSupersededVersions()
        {
            const string older = "memory://older.NodeSet2.xml";
            const string newer = "memory://newer.NodeSet2.xml";

            m_fileSystem.Add(older, Encoding.UTF8.GetBytes(NodeSet("1.0.0")));
            m_fileSystem.Add(newer, Encoding.UTF8.GetBytes(NodeSet("2.0.0")));

            NodesetFileCollection collection = Create((older, "1.0.0"), (newer, "2.0.0"));

            Assert.That(collection.Files.Values, Is.EquivalentTo(new[] { newer }));
            Assert.That(
                collection.AllFilePaths,
                Is.EquivalentTo(new[] { newer, older }),
                "a superseded NodeSet is still a NodeSet input");
        }

        /// <summary>
        /// Regression: the NodeSet's own &lt;Model Version="..."&gt; was never read -
        /// Info.Version came from the item metadata or fell straight through to
        /// the publication date. Two ordinary AdditionalFiles declaring 1.05.9
        /// and 1.05.10 were therefore selected by publication date instead of by
        /// version.
        /// </summary>
        [Test]
        public void DeclaredModelVersionIsUsedWhenTheItemMetadataHasNone()
        {
            const string older = "memory://a-older.NodeSet2.xml";
            const string newer = "memory://b-newer.NodeSet2.xml";

            // The 1.05.9 file is published later, so a date comparison would
            // pick it; the declared versions say otherwise.
            m_fileSystem.Add(
                older,
                Encoding.UTF8.GetBytes(NodeSet("1.05.9", "2026-09-01T00:00:00Z")));
            m_fileSystem.Add(
                newer,
                Encoding.UTF8.GetBytes(NodeSet("1.05.10", "2026-01-01T00:00:00Z")));

            // No options.Version - the version has to come from <Models>.
            NodesetFileCollection collection = Create((older, null), (newer, null));

            Assert.That(
                collection.Files[ModelUri],
                Is.EqualTo(newer),
                "1.05.10 is newer than 1.05.9 regardless of publication date");
        }

        /// <summary>
        /// Item metadata still wins over the NodeSet's own declaration.
        /// </summary>
        [Test]
        public void ItemMetadataVersionOverridesTheDeclaredModelVersion()
        {
            const string first = "memory://first.NodeSet2.xml";
            const string second = "memory://second.NodeSet2.xml";

            m_fileSystem.Add(first, Encoding.UTF8.GetBytes(NodeSet("9.0.0")));
            m_fileSystem.Add(second, Encoding.UTF8.GetBytes(NodeSet("1.0.0")));

            // The metadata reverses what the files declare.
            NodesetFileCollection collection = Create(
                (first, "1.0.0"), (second, "9.0.0"));

            Assert.That(collection.Files[ModelUri], Is.EqualTo(second));
        }

        /// <summary>
        /// Regression: the ISO-date guard only fired when both sides were dates,
        /// so a version-less NodeSet (whose version is its publication date) was
        /// run through the version parser against a real version. "2021-04-15"
        /// loses its month and day to the pre-release split and comes back as
        /// major 2021, which outranks every real version - the stale NodeSet won.
        /// </summary>
        [Test]
        public void ExplicitVersionIsNotBeatenByADateShapedVersion()
        {
            const string dated = "memory://a-dated.NodeSet2.xml";
            const string versioned = "memory://b-versioned.NodeSet2.xml";

            // The dated one is older by publication date, so it must lose.
            m_fileSystem.Add(
                dated,
                Encoding.UTF8.GetBytes(NodeSet(null, "2021-04-15T00:00:00Z")));
            m_fileSystem.Add(
                versioned,
                Encoding.UTF8.GetBytes(NodeSet("1.05.4", "2026-08-12T00:00:00Z")));

            NodesetFileCollection collection = Create(
                (dated, null), (versioned, "1.05.4"));

            Assert.That(
                collection.Files[ModelUri],
                Is.EqualTo(versioned),
                "a year parsed as a major version must not outrank a real version");
        }

        /// <summary>
        /// A version-less NodeSet falls back to its publication date, which must
        /// keep comparing as a date rather than going through the version parser.
        /// </summary>
        [Test]
        public void PublicationDateIsUsedWhenNoVersionIsDeclared()
        {
            const string older = "memory://older.NodeSet2.xml";
            const string newer = "memory://newer.NodeSet2.xml";

            m_fileSystem.Add(
                older, Encoding.UTF8.GetBytes(NodeSet(null, "2024-05-01T00:00:00Z")));
            m_fileSystem.Add(
                newer, Encoding.UTF8.GetBytes(NodeSet(null, "2024-12-01T00:00:00Z")));

            NodesetFileCollection collection = Create((older, null), (newer, null));

            Assert.That(collection.Files[ModelUri], Is.EqualTo(newer));
        }

        /// <summary>
        /// Regression: the pairwise "keep the newer one" selection used a
        /// comparison that was not transitive (a version against a date compared
        /// equal and was then settled by publication date), so which NodeSet won
        /// depended on the order of the AdditionalFiles.
        /// </summary>
        [TestCase(0, 1, 2)]
        [TestCase(0, 2, 1)]
        [TestCase(1, 0, 2)]
        [TestCase(1, 2, 0)]
        [TestCase(2, 0, 1)]
        [TestCase(2, 1, 0)]
        public void NewestSelectionDoesNotDependOnInputOrder(int first, int second, int third)
        {
            (string Path, string Version, string Published)[] candidates =
            [
                ("memory://a.NodeSet2.xml", "1.0.2", "2024-01-01T00:00:00Z"),
                ("memory://b.NodeSet2.xml", null, "2023-01-01T00:00:00Z"),
                ("memory://c.NodeSet2.xml", "1.0.3", "2022-01-01T00:00:00Z")
            ];
            foreach ((string path, string version, string published) in candidates)
            {
                m_fileSystem.Add(path, Encoding.UTF8.GetBytes(NodeSet(version, published)));
            }

            NodesetFileCollection collection = Create(
                (candidates[first].Path, null),
                (candidates[second].Path, null),
                (candidates[third].Path, null));

            Assert.That(
                collection.Files[ModelUri],
                Is.EqualTo("memory://c.NodeSet2.xml"),
                "the highest declared version wins whatever the order");
        }

        /// <summary>
        /// Regression: a ModelUri item metadata that differs from the model URI
        /// the NodeSet declares was used as the model's key for lookups while the
        /// collection was keyed by the declared URI, so the model was silently
        /// never generated.
        /// </summary>
        [Test]
        public void ModelUriMetadataThatDiffersFromTheNodeSetDoesNotLoseTheModel()
        {
            const string path = "memory://override.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(NodeSet("1.0.0")));

            var collection = new NodesetFileCollection(
                [(path, new NodesetFileOptions { ModelUri = ModelUri + "Other/" })],
                [],
                m_fileSystem,
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error));

            Assert.That(collection.ModelUris, Is.EqualTo(new[] { ModelUri }));
            Assert.That(
                collection.GetDesignFileListForModel(ModelUri, out NodesetFile nodeset),
                Is.Not.Null);
            Assert.That(nodeset.Info.ModelUri, Is.EqualTo(ModelUri));
        }

        /// <summary>
        /// Regression: a dependency that lists the root model among its own
        /// namespaces added the root to the design file list a second time.
        /// </summary>
        [Test]
        public void MutuallyReferencingNodeSetsListTheRootOnce()
        {
            const string rootUri = "http://test.org/UA/Root/";
            const string otherUri = "http://test.org/UA/Other/";
            const string root = "memory://root.NodeSet2.xml";
            const string other = "memory://other.NodeSet2.xml";
            m_fileSystem.Add(root, Encoding.UTF8.GetBytes(NodeSetFor(rootUri, otherUri)));
            m_fileSystem.Add(other, Encoding.UTF8.GetBytes(NodeSetFor(otherUri, rootUri)));

            var collection = new NodesetFileCollection(
                [(root, new NodesetFileOptions()), (other, new NodesetFileOptions())],
                [],
                m_fileSystem,
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error));

            List<string> files = collection.GetDesignFileListForModel(rootUri, out _);

            Assert.That(
                files.Select(f => f.Split(',')[0]),
                Is.EqualTo(new[] { root, other }));
        }

        /// <summary>
        /// Default Name/Prefix derived from the model URI. Names that already are
        /// valid identifiers must not change (existing consumers depend on them);
        /// the others - leading digit, '.', characters an identifier cannot hold,
        /// an empty path - are repaired, the same way on every OS.
        /// </summary>
        [TestCase("http://opcfoundation.org/UA/DI/", "DI", "DI")]
        [TestCase("http://opcfoundation.org/UA/Robotics-Intent/", "Robotics_Intent", "Robotics_Intent")]
        [TestCase("http://test.org/UA/Versioned/", "Versioned", "Versioned")]
        [TestCase("http://opcfoundation.org/UA/Machinery/Result/", "MachineryResult", "MachineryResult")]
        [TestCase("urn:opcfoundation.org:2026-09:NodeSetImport", "NodeSetImport", "NodeSetImport")]
        [TestCase("http://www.OPCFoundation.org/UA/2013/01/ISA95", "_201301ISA95", "_201301ISA95")]
        [TestCase("http://example.com/Models/v1.2/", "Modelsv1_2", "Modelsv1._2")]
        [TestCase("http://example.com/", "example_com", "example.com")]
        [TestCase("http://test.org/UA/a*b%3Fc/", "a_b_c", "a_b_c")]
        [TestCase("http://test.org/UA/class/", "@class", "@class")]
        public void DefaultNameAndPrefixAreValidIdentifiers(
            string modelUri,
            string expectedName,
            string expectedPrefix)
        {
            Assert.That(NodesetFileCollection.GetDefaultNameFromUri(modelUri), Is.EqualTo(expectedName));
            Assert.That(NodesetFileCollection.GetDefaultPrefixFromUri(modelUri), Is.EqualTo(expectedPrefix));
        }

        /// <summary>
        /// Identifier sidecar resolution: relative paths are normalized, and the
        /// suffix fallback only matches on a directory boundary.
        /// </summary>
        [TestCase("/p/Models/Model.NodeSet2.xml", "./ids.csv", "/p/Models/ids.csv")]
        [TestCase("/p/Models/Model.NodeSet2.xml", "../Shared/ids.csv", "/p/Shared/ids.csv")]
        [TestCase("/p/Models/Model.NodeSet2.xml", "ids.csv", "/p/Models/ids.csv")]
        [TestCase("/p/Other/Model.NodeSet2.xml", "Di.NodeIds.csv", "/p/Models/Di.NodeIds.csv")]
        [TestCase("/p/Other/Model.NodeSet2.xml", "NodeIds.csv", null)]
        public void IdentifierSidecarPathIsNormalizedAndMatchedOnWholeSegments(
            string nodeSetPath,
            string identifierFile,
            string expected)
        {
            string[] csvFiles =
            [
                "/p/Models/ids.csv",
                "/q/Models/ids.csv",
                "/p/Shared/ids.csv",
                "/q/Shared/ids.csv",
                "/p/Models/Di.NodeIds.csv",
                "/p/Robotics/Opc.Ua.Di.NodeIds.csv"
            ];

            Assert.That(
                NodesetIdentifierFileValidator.ResolvePath(nodeSetPath, identifierFile, csvFiles),
                Is.EqualTo(expected));
        }

        private static string NodeSetFor(string modelUri, string otherUri)
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                    <NamespaceUris>
                        <Uri>{modelUri}</Uri>
                        <Uri>{otherUri}</Uri>
                    </NamespaceUris>
                    <Models>
                        <Model ModelUri="{modelUri}" PublicationDate="2026-08-12T00:00:00Z" Version="1.0.0" />
                    </Models>
                    <Aliases>
                        <Alias Alias="HasSubtype">i=45</Alias>
                    </Aliases>
                    <UAObjectType NodeId="ns=1;i=1000" BrowseName="1:SomeType">
                        <DisplayName>SomeType</DisplayName>
                        <References>
                            <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                        </References>
                    </UAObjectType>
                </UANodeSet>
                """;
        }

        private NodesetFileCollection Create(params (string Path, string Version)[] inputs)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            List<(string, NodesetFileOptions)> files =
                [.. inputs.Select(i => (
                    i.Path,
                    new NodesetFileOptions { Version = i.Version }))];
            return new NodesetFileCollection(
                [.. files],
                [],
                m_fileSystem,
                telemetry);
        }

        private static string NodeSet(
            string version,
            string publicationDate = "2026-08-12T00:00:00Z")
        {
            string versionAttribute = version == null
                ? string.Empty
                : $" Version=\"{version}\"";

            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                    xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                    xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                    <NamespaceUris>
                        <Uri>{ModelUri}</Uri>
                    </NamespaceUris>
                    <Models>
                        <Model ModelUri="{ModelUri}"
                            PublicationDate="{publicationDate}"{versionAttribute} />
                    </Models>
                    <Aliases>
                        <Alias Alias="HasSubtype">i=45</Alias>
                    </Aliases>
                    <UAObjectType NodeId="ns=1;i=1000" BrowseName="1:SomeType">
                        <DisplayName>SomeType</DisplayName>
                        <References>
                            <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                        </References>
                    </UAObjectType>
                </UANodeSet>
                """;
        }
    }
}
