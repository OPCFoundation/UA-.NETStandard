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
