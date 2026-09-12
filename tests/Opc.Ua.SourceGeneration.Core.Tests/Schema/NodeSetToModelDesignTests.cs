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
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Export;
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
        private const string ViewImportNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/ViewImport/</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="http://test.org/UA/ViewImport/"
                        PublicationDate="2026-08-12T00:00:00Z"
                        Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                    <Alias Alias="Organizes">i=35</Alias>
                </Aliases>
                <UAReferenceType NodeId="i=33" BrowseName="HierarchicalReferences" IsAbstract="true">
                    <DisplayName>HierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=35" BrowseName="Organizes">
                    <DisplayName>Organizes</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObject NodeId="ns=1;s=Views" BrowseName="1:Views">
                    <DisplayName>Views</DisplayName>
                    <References>
                        <Reference ReferenceType="Organizes">ns=1;s=Views_Operations</Reference>
                        <Reference ReferenceType="Organizes">ns=1;s=Views_Engineering</Reference>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
                <UAView NodeId="ns=1;s=Views_Operations"
                    BrowseName="1:Operations"
                    ParentNodeId="ns=1;s=Views"
                    ContainsNoLoops="true">
                    <DisplayName>Operations</DisplayName>
                    <References>
                        <Reference ReferenceType="Organizes" IsForward="false">i=87</Reference>
                    </References>
                </UAView>
                <UAView NodeId="ns=1;s=Views_Engineering"
                    BrowseName="1:Engineering"
                    ParentNodeId="ns=1;s=Views"
                    ContainsNoLoops="true">
                    <DisplayName>Engineering</DisplayName>
                    <References>
                        <Reference ReferenceType="Organizes" IsForward="false">i=87</Reference>
                    </References>
                </UAView>
            </UANodeSet>
            """;

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
                [],
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

        /// <summary>
        /// Verifies importing views as top-level nodes keeps their organizing folder references.
        /// </summary>
        [Test]
        public void ImportPreservesFolderOrganizesReferencesToViews()
        {
            const string path = "memory://ViewImport.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(ViewImportNodeSet));

            var settings = new NodeSetReaderSettings();
            NodeSetToModelDesign importer = new(
                m_fileSystem,
                path,
                settings,
                CreateTelemetry());

            ModelDesign model = importer.Import("ViewImport", "ViewImport");
            ObjectDesign viewsFolder = model.Items
                .OfType<ObjectDesign>()
                .Single(x => x.SymbolicName?.Name == "Views");
            string[] expectedViews = ["Operations", "Engineering"];

            Assert.Multiple(() =>
            {
                Assert.That(model.Items.OfType<ViewDesign>().Select(x => x.SymbolicName?.Name), Is.EquivalentTo(
                    expectedViews));
                Assert.That(viewsFolder.Children?.Items, Is.Null.Or.Empty);
                Assert.That(viewsFolder.References, Has.Length.EqualTo(2));
                Assert.That(viewsFolder.References.Select(x => x.ReferenceType.Name), Is.All.EqualTo("Organizes"));
                Assert.That(viewsFolder.References.Select(x => x.IsInverse), Is.All.False);
                Assert.That(viewsFolder.References.Select(x => x.TargetId.Name), Is.EquivalentTo(
                    expectedViews));
            });
        }

        [Test]
        public void TypeSymbolicNameUsesNodeIdNamespace()
        {
            var symbolicId = new XmlQualifiedName(
                "GroundControlPointDataType",
                "http://opcfoundation.org/UA/GPOS/");
            var symbolicName = new XmlQualifiedName(
                "GroundControlPointDataType",
                "http://opcfoundation.org/UA/RSL/");

            XmlQualifiedName normalized = NodeSetToModelDesign.NormalizeSymbolicNameNamespace(
                new UADataType(),
                symbolicId,
                symbolicName);

            Assert.That(normalized.Name, Is.EqualTo(symbolicName.Name));
            Assert.That(normalized.Namespace, Is.EqualTo(symbolicId.Namespace));
        }

        [Test]
        public void InstanceSymbolicNameKeepsBrowseNameNamespace()
        {
            var symbolicId = new XmlQualifiedName(
                "Position",
                "http://opcfoundation.org/UA/GPOS/");
            var symbolicName = new XmlQualifiedName(
                "Position",
                "http://opcfoundation.org/UA/RSL/");

            XmlQualifiedName normalized = NodeSetToModelDesign.NormalizeSymbolicNameNamespace(
                new UAVariable(),
                symbolicId,
                symbolicName);
            Assert.That(normalized, Is.SameAs(symbolicName));
            Assert.That(normalized, Is.SameAs(symbolicName));
        }

        /// <summary>
        /// Regression: a NodeSet without a &lt;Models&gt; entry crashed with an
        /// unexplained NullReferenceException inside the constructor. It is
        /// reported as the malformed input it is.
        /// </summary>
        [Test]
        public void ConstructorNodeSetWithoutModelsThrowsInvalidDataException()
        {
            const string path = "memory://NoModels.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(NoModelsNodeSet));

            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => new NodeSetToModelDesign(
                    m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry()));

            Assert.That(ex.Message, Does.Contain("Models"));
        }

        /// <summary>
        /// Regression: an instance node carrying neither a ParentNodeId nor a
        /// &lt;References&gt; element - both optional in the schema - crashed the
        /// import with a NullReferenceException. Such a node has no type
        /// definition either, so it is reported as the malformed input it is.
        /// </summary>
        [Test]
        public void ImportInstanceWithoutParentOrReferencesReportsTheNode()
        {
            const string path = "memory://NoReferences.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(NoReferencesNodeSet));

            NodeSetToModelDesign importer = new(
                m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry());

            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => importer.Import("NoRefs", "NoRefs"));

            Assert.That(ex.Message, Does.Contain("Orphan"));
        }

        /// <summary>
        /// An instance with references but no ParentNodeId and no inverse
        /// hierarchical reference imports as a top-level node.
        /// </summary>
        [Test]
        public void ImportInstanceWithoutParentImportsAsTopLevelNode()
        {
            const string path = "memory://NoParent.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(NoParentNodeSet));

            NodeSetToModelDesign importer = new(
                m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry());

            ModelDesign model = null;
            Assert.DoesNotThrow(() => model = importer.Import("NoParent", "NoParent"));
            Assert.That(
                model.Items.Select(x => x.SymbolicName?.Name),
                Does.Contain("Orphan"));
        }

        /// <summary>
        /// Regression: <c>AccessRestrictions="0"</c> means "no restrictions", but
        /// the mapping fell through its switch and returned EncryptionRequired,
        /// so an unrestricted node was imported as an encrypted one.
        /// </summary>
        [Test]
        public void ImportZeroAccessRestrictionsLeavesThemUnspecified()
        {
            const string path = "memory://ZeroRestrictions.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(ZeroAccessRestrictionsNodeSet));

            NodeSetToModelDesign importer = new(
                m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry());

            ModelDesign model = importer.Import("Restrictions", "Restrictions");
            NodeDesign node = model.Items
                .Single(x => x.SymbolicName?.Name == "Unrestricted");

            Assert.That(
                node.AccessRestrictionsSpecified,
                Is.False,
                "an empty mask carries no restriction the design schema can express");
            Assert.That(
                node.AccessRestrictions,
                Is.Not.EqualTo(AccessRestrictions.EncryptionRequired));
        }

        /// <summary>
        /// A real restriction mask is still imported.
        /// </summary>
        [Test]
        public void ImportEncryptionAccessRestrictionsIsPreserved()
        {
            const string path = "memory://EncryptionRestrictions.NodeSet2.xml";
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(
                    ZeroAccessRestrictionsNodeSet.Replace(
                        "AccessRestrictions=\"0\"",
                        "AccessRestrictions=\"2\"",
                        StringComparison.Ordinal)));

            NodeSetToModelDesign importer = new(
                m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry());

            ModelDesign model = importer.Import("Restrictions", "Restrictions");
            NodeDesign node = model.Items
                .Single(x => x.SymbolicName?.Name == "Unrestricted");

            Assert.That(node.AccessRestrictionsSpecified, Is.True);
            Assert.That(
                node.AccessRestrictions,
                Is.EqualTo(AccessRestrictions.EncryptionRequired));
        }

        /// <summary>
        /// Regression: making the empty mask unspecified must not also drop a
        /// real restriction that happens to carry a bit the design schema cannot
        /// name. A mask of EncryptionRequired plus a reserved bit matches no case
        /// in the switch, and returning "unspecified" there would publish an
        /// encryption-required node with no protection at all.
        /// </summary>
        [Test]
        public void ImportUnknownAccessRestrictionBitStaysRestricted()
        {
            const string path = "memory://UnknownRestrictions.NodeSet2.xml";

            // 0x02 EncryptionRequired | 0x10 (reserved / vendor bit).
            m_fileSystem.Add(
                path,
                Encoding.UTF8.GetBytes(
                    ZeroAccessRestrictionsNodeSet.Replace(
                        "AccessRestrictions=\"0\"",
                        "AccessRestrictions=\"18\"",
                        StringComparison.Ordinal)));

            NodeSetToModelDesign importer = new(
                m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry());

            ModelDesign model = importer.Import("Restrictions", "Restrictions");
            NodeDesign node = model.Items
                .Single(x => x.SymbolicName?.Name == "Unrestricted");

            Assert.That(
                node.AccessRestrictionsSpecified,
                Is.True,
                "an unrecognised combination must stay fail-closed");
            Assert.That(
                node.AccessRestrictions,
                Is.EqualTo(AccessRestrictions.SessionWithEncryptionRequired));
        }

        private const string NoModelsNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/NoModels/</Uri>
                </NamespaceUris>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                </UAObjectType>
            </UANodeSet>
            """;

        /// <summary>
        /// Regression: the non-hierarchical fallback was repointed from the child
        /// (where it never matched) to the parent, which made it reachable - and
        /// it ran before the child's own inverse hierarchical reference was
        /// considered. A parent that also declares a non-hierarchical forward
        /// reference to its child therefore had the child classified as
        /// non-hierarchical and dropped out of its Children entirely.
        /// </summary>
        [Test]
        public void ImportChildWithNonHierarchicalParentReferenceIsStillLinked()
        {
            const string path = "memory://MixedRefs.NodeSet2.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(MixedReferencesNodeSet));

            NodeSetToModelDesign importer = new(
                m_fileSystem, path, new NodeSetReaderSettings(), CreateTelemetry());

            ModelDesign model = importer.Import("MixedRefs", "MixedRefs");
            NodeDesign parent = model.Items
                .Single(x => x.SymbolicName?.Name == "ParentType");

            Assert.That(parent.Children?.Items, Is.Not.Null, "the child must be linked");
            Assert.That(
                parent.Children.Items.Select(x => x.SymbolicName?.Name),
                Does.Contain("Child"),
                "the child's inverse HasComponent outranks the parent's " +
                "non-hierarchical forward reference");
        }

        private const string MixedReferencesNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/MixedRefs/</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="http://test.org/UA/MixedRefs/"
                        PublicationDate="2026-08-12T00:00:00Z"
                        Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                    <Alias Alias="HasComponent">i=47</Alias>
                    <Alias Alias="HasCondition">i=9006</Alias>
                </Aliases>
                <UAReferenceType NodeId="i=33" BrowseName="HierarchicalReferences" IsAbstract="true">
                    <DisplayName>HierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=47" BrowseName="HasComponent">
                    <DisplayName>HasComponent</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=32" BrowseName="NonHierarchicalReferences" IsAbstract="true">
                    <DisplayName>NonHierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=32</Reference>
                    </References>
                </UAReferenceType>
                <UAReferenceType NodeId="i=9006" BrowseName="HasCondition">
                    <DisplayName>HasCondition</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=32</Reference>
                    </References>
                </UAReferenceType>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObjectType NodeId="ns=1;s=ParentType" BrowseName="1:ParentType">
                    <DisplayName>ParentType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                        <Reference ReferenceType="HasCondition">ns=1;s=Child</Reference>
                    </References>
                </UAObjectType>
                <UAObject NodeId="ns=1;s=Child" BrowseName="1:Child"
                    ParentNodeId="ns=1;s=ParentType">
                    <DisplayName>Child</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                        <Reference ReferenceType="HasComponent" IsForward="false">ns=1;s=ParentType</Reference>
                    </References>
                </UAObject>
            </UANodeSet>
            """;

        private const string NoReferencesNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/NoRefs/</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="http://test.org/UA/NoRefs/"
                        PublicationDate="2026-08-12T00:00:00Z"
                        Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                </Aliases>
                <UAReferenceType NodeId="i=33" BrowseName="HierarchicalReferences" IsAbstract="true">
                    <DisplayName>HierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObject NodeId="ns=1;s=Orphan" BrowseName="1:Orphan">
                    <DisplayName>Orphan</DisplayName>
                </UAObject>
            </UANodeSet>
            """;

        private const string NoParentNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/NoParent/</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="http://test.org/UA/NoParent/"
                        PublicationDate="2026-08-12T00:00:00Z"
                        Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                </Aliases>
                <UAReferenceType NodeId="i=33" BrowseName="HierarchicalReferences" IsAbstract="true">
                    <DisplayName>HierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObject NodeId="ns=1;s=Orphan" BrowseName="1:Orphan">
                    <DisplayName>Orphan</DisplayName>
                    <References>
                        <Reference ReferenceType="HasTypeDefinition">i=58</Reference>
                    </References>
                </UAObject>
            </UANodeSet>
            """;

        private const string ZeroAccessRestrictionsNodeSet = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                <NamespaceUris>
                    <Uri>http://test.org/UA/Restrictions/</Uri>
                </NamespaceUris>
                <Models>
                    <Model ModelUri="http://test.org/UA/Restrictions/"
                        PublicationDate="2026-08-12T00:00:00Z"
                        Version="1.0.0" />
                </Models>
                <Aliases>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                </Aliases>
                <UAReferenceType NodeId="i=33" BrowseName="HierarchicalReferences" IsAbstract="true">
                    <DisplayName>HierarchicalReferences</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=33</Reference>
                    </References>
                </UAReferenceType>
                <UAObjectType NodeId="i=58" BrowseName="BaseObjectType">
                    <DisplayName>BaseObjectType</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
                <UAObjectType NodeId="ns=1;s=Unrestricted"
                    BrowseName="1:Unrestricted"
                    AccessRestrictions="0">
                    <DisplayName>Unrestricted</DisplayName>
                    <References>
                        <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                    </References>
                </UAObjectType>
            </UANodeSet>
            """;

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
