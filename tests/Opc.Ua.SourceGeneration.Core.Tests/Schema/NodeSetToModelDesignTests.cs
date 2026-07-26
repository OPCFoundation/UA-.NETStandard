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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Tests;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="NodeSetToModelDesign"/> importer. The tests
    /// use the small, self-contained <c>CrossModelTypes.NodeSet2.xml</c> resource
    /// so that the deterministic surface (node set detection, namespace loading and
    /// the constructor side effects) can be verified without pulling in the full UA
    /// base model.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodeSetToModelDesignTests
    {
        private const string OpcUaNamespaceUri = "http://opcfoundation.org/UA/";
        private const string CrossModelNamespaceUri = "http://test.org/UA/CrossModel/Types";
        private const string SameNamedArgumentsNamespaceUri =
            "http://test.org/UA/SameNamedMethodArguments/";
        private const string NodeSetResource = "CrossModelTypes.NodeSet2.xml";
        private const string SameNamedArgumentsResource =
            "SameNamedMethodArguments.NodeSet2.xml";
        private const string DesignResource = "TestDataDesign.xml";

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

        [Test]
        public void IsNodeSetReturnsTrueForNodeSet()
        {
            bool result = NodeSetToModelDesign.IsNodeSet(m_fileSystem, ResourcePath(NodeSetResource));

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsNodeSetReturnsFalseForDesignFile()
        {
            bool result = NodeSetToModelDesign.IsNodeSet(m_fileSystem, ResourcePath(DesignResource));

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsNodeSetReturnsFalseForNonXmlContent()
        {
            const string path = "memory://garbage.txt";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes("just some text without any markup"));

            bool result = NodeSetToModelDesign.IsNodeSet(m_fileSystem, path);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsNodeSetNullFileSystemThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => NodeSetToModelDesign.IsNodeSet(null, ResourcePath(NodeSetResource)));
            Assert.That(ex.ParamName, Is.EqualTo("fileSystem"));
        }

        [Test]
        public void IsNodeSetNullFilePathThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => NodeSetToModelDesign.IsNodeSet(m_fileSystem, null));
            Assert.That(ex.ParamName, Is.EqualTo("filePath"));
        }

        [Test]
        public void LoadNamespacesReturnsModelAndDependencyNamespaces()
        {
            List<Namespace> namespaces = NodeSetToModelDesign.LoadNamespaces(
                m_fileSystem, ResourcePath(NodeSetResource));

            Assert.That(namespaces, Has.Count.EqualTo(2));

            Namespace opcUa = namespaces.Single(x => x.Value == OpcUaNamespaceUri);
            Namespace crossModel = namespaces.Single(x => x.Value == CrossModelNamespaceUri);

            Assert.That(opcUa.Name, Is.EqualTo("OpcUa"));
            Assert.That(opcUa.Prefix, Is.EqualTo("Opc.Ua"));
            Assert.That(crossModel.Name, Is.EqualTo("testorgUACrossModelTypes"));
            Assert.That(crossModel.Prefix, Is.EqualTo("test.org.UA.CrossModel.Types"));
        }

        [Test]
        public void LoadNamespacesNullFileSystemThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => NodeSetToModelDesign.LoadNamespaces(null, ResourcePath(NodeSetResource)));
            Assert.That(ex.ParamName, Is.EqualTo("fileSystem"));
        }

        [Test]
        public void LoadNamespacesNullFilePathThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => NodeSetToModelDesign.LoadNamespaces(m_fileSystem, null));
            Assert.That(ex.ParamName, Is.EqualTo("filePath"));
        }

        [Test]
        public void ConstructorRegistersNamespacesInSettings()
        {
            var settings = new NodeSetReaderSettings();

            NodeSetToModelDesign importer = new(
                m_fileSystem, ResourcePath(NodeSetResource), settings, CreateTelemetry());

            Assert.That(importer, Is.Not.Null);
            Assert.That(settings.NamespaceUris.Count, Is.EqualTo(2));
            Assert.That(settings.NamespaceUris.GetString(0), Is.EqualTo(OpcUaNamespaceUri));
            Assert.That(settings.NamespaceUris.GetString(1), Is.EqualTo(CrossModelNamespaceUri));
            Assert.That(settings.NamespaceTables, Has.Count.EqualTo(1));
            Assert.That(settings.NamespaceTables.ContainsKey(CrossModelNamespaceUri), Is.True);
            Assert.That(settings.NamespaceTables[CrossModelNamespaceUri], Is.EqualTo([CrossModelNamespaceUri]));
        }

        [Test]
        public void ConstructorNullFilePathThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new NodeSetToModelDesign(
                    m_fileSystem, null, new NodeSetReaderSettings(), CreateTelemetry()));
            Assert.That(ex.ParamName, Is.EqualTo("filePath"));
        }

        [Test]
        public void ConstructorNullSettingsThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new NodeSetToModelDesign(
                    m_fileSystem, ResourcePath(NodeSetResource), null, CreateTelemetry()));
            Assert.That(ex.ParamName, Is.EqualTo("settings"));
        }

        [Test]
        public void ConstructorNullFileSystemThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new NodeSetToModelDesign(
                    null, ResourcePath(NodeSetResource), new NodeSetReaderSettings(), CreateTelemetry()));
            Assert.That(ex.ParamName, Is.EqualTo("fileSystem"));
        }

        [Test]
        public void ImportWithoutBaseModelThrowsInvalidDataException()
        {
            var settings = new NodeSetReaderSettings();
            NodeSetToModelDesign importer = new(
                m_fileSystem, ResourcePath(NodeSetResource), settings, CreateTelemetry());

            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => importer.Import("Test", "CrossModel"));
            Assert.That(ex.Message, Does.Contain("WidgetType"));
        }

        [Test]
        public void ImportPreservesSameNamedInputAndOutputArgumentNames()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            string path = ResourcePath(SameNamedArgumentsResource);
            var nodesets = new NodesetFileCollection(
                [(path, new NodesetFileOptions())],
                m_fileSystem,
                telemetry);
            List<string> designFiles = nodesets.GetDesignFileListForModel(
                SameNamedArgumentsNamespaceUri,
                out _);
            Assert.That(designFiles, Is.Not.Null);

            IFileSystem fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(m_fileSystem);
            IModelDesign model = fileSystem.OpenModelDesign(
                new DesignFileCollection { Targets = designFiles },
                [],
                telemetry,
                useAllowSubtypes: false);
            MethodDesign method = model.GetNodeDesigns()
                .OfType<MethodDesign>()
                .Single(x => x.SymbolicName?.Name == "RoundTripMethodType");
            string[] expectedInputNames =
            [
                "Foo",
                "foo",
                "Class",
                "VersionId",
                "Await",
                "Ct",
                "cT",
                "CancellationToken",
                "Context",
                "ObjectId",
                "Method",
                "InputArguments",
                "Results",
                "_result",
                "_foo",
                "_",
                "Nameof",
                "__arglist",
                "__makeref",
                "__reftype",
                "__refvalue"
            ];
            string[] expectedOutputNames =
            [
                "VersionId",
                "class",
                "Changed",
                "OutputArguments",
                "ServiceResult",
                "Quote\"Name",
                "Back\\Slash",
                "Line\nBreak",
                "Δelta雪",
                "Foo",
                "RoundTripMethodStateResult",
                "Next\u0085Line",
                "Line\u2028Separator",
                "Paragraph\u2029Separator"
            ];

            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments, Has.Length.EqualTo(expectedInputNames.Length));
                Assert.That(
                    method.InputArguments.Select(argument => argument.Name),
                    Is.EqualTo(expectedInputNames));
                Assert.That(method.OutputArguments, Has.Length.EqualTo(expectedOutputNames.Length));
                Assert.That(
                    method.OutputArguments.Select(argument => argument.Name),
                    Is.EqualTo(expectedOutputNames));
            });
        }

        private static ITelemetryContext CreateTelemetry()
        {
            return NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
        }

        private static string ResourcePath(string fileName)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", fileName);
        }
    }
}
