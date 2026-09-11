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
using Opc.Ua.Tests;
using SchemaTypes = Opc.Ua.Schema.Types;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="ModelDesignValidator"/> class. The tests
    /// drive the validator the same way <c>DesignFileExtensions.OpenModelDesign</c>
    /// does in production: a resource file system that exposes the built-in UA
    /// design files falls back to a virtual file system holding the test design
    /// resources copied next to the assembly.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ModelDesignValidatorTests
    {
        private const string TargetNamespaceUri = "http://test.org/UA/Data/";
        private const string OpcUaNamespaceUri = "http://opcfoundation.org/UA/";

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
        public void ConstructorSetsDefaultProperties()
        {
            ModelDesignValidator validator = CreateValidator();

            Assert.That(validator.MaxRecursionDepth, Is.EqualTo(100));
            Assert.That(validator.ReleaseCandidate, Is.False);
            Assert.That(validator.UseAllowSubtypes, Is.False);
            Assert.That(validator.ModelVersion, Is.Null);
            Assert.That(validator.ModelPublicationDate, Is.Null);
        }

        [Test]
        public void SettablePropertiesRoundTrip()
        {
            ModelDesignValidator validator = CreateValidator();

            validator.MaxRecursionDepth = 42;
            validator.ReleaseCandidate = true;
            validator.UseAllowSubtypes = true;
            validator.ModelVersion = "2.1.0";
            validator.ModelPublicationDate = "2024-06-01";

            Assert.That(validator.MaxRecursionDepth, Is.EqualTo(42));
            Assert.That(validator.ReleaseCandidate, Is.True);
            Assert.That(validator.UseAllowSubtypes, Is.True);
            Assert.That(validator.ModelVersion, Is.EqualTo("2.1.0"));
            Assert.That(validator.ModelPublicationDate, Is.EqualTo("2024-06-01"));
        }

        [Test]
        public void ValidateNullTargetsThrowsArgumentException()
        {
            ModelDesignValidator validator = CreateValidator();

            Assert.Throws<ArgumentException>(() => validator.Validate(null, [], null));
        }

        [Test]
        public void ValidateEmptyTargetsThrowsArgumentException()
        {
            ModelDesignValidator validator = CreateValidator();

            Assert.Throws<ArgumentException>(() => validator.Validate([], [], null));
        }

        [Test]
        public void ValidateMissingFileThrowsFileNotFoundException()
        {
            ModelDesignValidator validator = CreateValidator();
            string missing = ResourcePath("does-not-exist-model.xml");

            Assert.Throws<FileNotFoundException>(() => validator.Validate([missing], [], null));
        }

        [Test]
        public void ValidateMalformedXmlThrowsInvalidOperationException()
        {
            const string path = "memory://malformed-design.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes("this is not xml at all"));
            ModelDesignValidator validator = CreateValidator();

            Assert.Throws<InvalidOperationException>(() => validator.Validate([path], [], null));
        }

        [Test]
        public void ValidateUndefinedBaseTypeThrowsInvalidOperationException()
        {
            const string path = "memory://undefined-design.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(UndefinedBaseTypeDesign));
            ModelDesignValidator validator = CreateValidator();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => validator.Validate([path], [], null));
            Assert.That(ex.Message, Does.Contain("MyType"));
        }

        [Test]
        public void TryFindNodeNullSymbolicIdThrowsInvalidOperationException()
        {
            ModelDesignValidator validator = CreateValidator();

            Assert.Throws<InvalidOperationException>(
                () => validator.TryFindNode(null, "source", "reference", out _));
        }

        [Test]
        public void ValidateSetsTargetNamespace()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.TargetNamespace, Is.Not.Null);
            Assert.That(validator.TargetNamespace.Value, Is.EqualTo(TargetNamespaceUri));
            Assert.That(validator.TargetNamespace.Name, Is.EqualTo("TestData"));
            Assert.That(validator.TargetNamespace.Prefix, Is.EqualTo("TestData"));
        }

        [Test]
        public void ValidateSetsTargetVersion()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.TargetVersion, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void ValidatePopulatesNamespaces()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.Namespaces, Has.Length.EqualTo(2));

            Namespace opcUa = validator.Namespaces.Single(x => x.Value == OpcUaNamespaceUri);
            Namespace testData = validator.Namespaces.Single(x => x.Value == TargetNamespaceUri);

            Assert.That(opcUa.Name, Is.EqualTo("OpcUa"));
            Assert.That(opcUa.Prefix, Is.EqualTo("Opc.Ua"));
            Assert.That(testData.Name, Is.EqualTo("TestData"));
        }

        [Test]
        public void ValidatePopulatesNamespaceUris()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.NamespaceUris.Count, Is.EqualTo(2));
            Assert.That(validator.NamespaceUris.GetString(0), Is.EqualTo(OpcUaNamespaceUri));
            Assert.That(validator.NamespaceUris.GetString(1), Is.EqualTo(TargetNamespaceUri));
        }

        [Test]
        public void ValidatePopulatesNodes()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.Nodes, Has.Length.EqualTo(83));
            Assert.That(validator.GetNodeDesigns().Count(), Is.EqualTo(83));
        }

        [Test]
        public void ValidatePopulatesDependencies()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            List<ModelTableEntry> dependencies = [.. validator.Dependencies];

            Assert.That(dependencies, Has.Count.EqualTo(1));
            Assert.That(dependencies[0].ModelUri, Is.EqualTo(OpcUaNamespaceUri));
        }

        [Test]
        public void ValidatePopulatesRolePermissionsAndAccessRestrictions()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.RolePermissions, Has.Count.EqualTo(97));
            Assert.That(validator.AccessRestrictions, Has.Count.EqualTo(61));
        }

        [Test]
        public void TryFindNodeReturnsKnownNode()
        {
            ModelDesignValidator validator = CreateValidatedValidator();
            var symbolicId = new XmlQualifiedName("GenerateValuesEventType", TargetNamespaceUri);

            bool found = validator.TryFindNode(symbolicId, "source", "reference", out NodeDesign node);

            Assert.That(found, Is.True);
            Assert.That(node, Is.Not.Null);
            Assert.That(node.BrowseName, Is.EqualTo("GenerateValuesEventType"));
            Assert.That(node, Is.TypeOf<ObjectTypeDesign>());
        }

        [Test]
        public void TryFindNodeUnknownNameReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidatedValidator();
            var symbolicId = new XmlQualifiedName("ThereIsNoSuchNode", TargetNamespaceUri);

            bool found = validator.TryFindNode(symbolicId, "source", "reference", out NodeDesign node);

            Assert.That(found, Is.False);
            Assert.That(node, Is.Null);
        }

        [Test]
        public void GetListOfServicesReturnsAllServicesWhenUnfiltered()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.GetListOfServices(), Has.Length.EqualTo(39));
        }

        [Test]
        public void GetListOfServicesFiltersByCategory()
        {
            ModelDesignValidator validator = CreateValidatedValidator();

            Assert.That(validator.GetListOfServices(ServiceCategory.Session), Has.Length.EqualTo(4));
            Assert.That(validator.GetListOfServices(ServiceCategory.None), Is.Empty);
        }

        [TestCase(SpecificationVersion.V103)]
        [TestCase(SpecificationVersion.V104)]
        [TestCase(SpecificationVersion.V105)]
        public void ValidateSucceedsForSpecificationVersion(SpecificationVersion version)
        {
            ModelDesignValidator validator = CreateValidator(version: version);

            validator.Validate(
                [ResourcePath("TestDataDesign.xml")],
                [],
                ResourcePath("TestDataDesign.csv"));

            Assert.That(validator.TargetNamespace.Value, Is.EqualTo(TargetNamespaceUri));
            Assert.That(validator.Nodes, Has.Length.EqualTo(83));
        }

        [Test]
        public void IsExcludedNullNodeDesignReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);

            Assert.That(validator.IsExcluded((NodeDesign)null), Is.False);
        }

        [Test]
        public void IsExcludedTestingPurposeReturnsTrue()
        {
            ModelDesignValidator validator = CreateValidator();
            var node = new NodeDesign { Purpose = DataTypePurpose.Testing };

            Assert.That(validator.IsExcluded(node), Is.True);
        }

        [Test]
        public void IsExcludedMatchingReleaseStatusReturnsTrue()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);
            var node = new NodeDesign { ReleaseStatus = ReleaseStatus.Draft };

            Assert.That(validator.IsExcluded(node), Is.True);
        }

        [Test]
        public void IsExcludedMatchingCategoryReturnsTrue()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Experimental"]);
            var node = new NodeDesign { Category = "Experimental" };

            Assert.That(validator.IsExcluded(node), Is.True);
        }

        [Test]
        public void IsExcludedNonMatchingNodeReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);
            var node = new NodeDesign { ReleaseStatus = ReleaseStatus.Released, Category = "Core" };

            Assert.That(validator.IsExcluded(node), Is.False);
        }

        [Test]
        public void IsExcludedWithoutExclusionListReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidator();
            var node = new NodeDesign { ReleaseStatus = ReleaseStatus.Draft };

            Assert.That(validator.IsExcluded(node), Is.False);
        }

        [Test]
        public void IsExcludedNullParameterReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);

            Assert.That(validator.IsExcluded((Parameter)null), Is.False);
        }

        [Test]
        public void IsExcludedParameterMatchingReleaseStatusReturnsTrue()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Deprecated"]);
            var parameter = new Parameter { ReleaseStatus = ReleaseStatus.Deprecated };

            Assert.That(validator.IsExcluded(parameter), Is.True);
        }

        [Test]
        public void IsExcludedParameterNonMatchingReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);
            var parameter = new Parameter { ReleaseStatus = ReleaseStatus.Released };

            Assert.That(validator.IsExcluded(parameter), Is.False);
        }

        [Test]
        public void IsExcludedDataTypeMatchingPurposeReturnsTrue()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Testing"]);
            var dataType = new SchemaTypes.DataType { Purpose = SchemaTypes.DataTypePurpose.Testing };

            Assert.That(validator.IsExcluded(dataType), Is.True);
        }

        [Test]
        public void IsExcludedDataTypeMatchingReleaseStatusReturnsTrue()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);
            var dataType = new SchemaTypes.DataType { ReleaseStatus = SchemaTypes.ReleaseStatus.Draft };

            Assert.That(validator.IsExcluded(dataType), Is.True);
        }

        [Test]
        public void IsExcludedDataTypeNonMatchingReturnsFalse()
        {
            ModelDesignValidator validator = CreateValidator(exclusions: ["Draft"]);
            var dataType = new SchemaTypes.DataType { ReleaseStatus = SchemaTypes.ReleaseStatus.Released };

            Assert.That(validator.IsExcluded(dataType), Is.False);
        }

        private ModelDesignValidator CreateValidator(
            IReadOnlyList<string> exclusions = null,
            uint startId = 1000,
            SpecificationVersion version = SpecificationVersion.V105)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IFileSystem fileSystem = typeof(ModelDesignValidator).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(m_fileSystem);
            return new ModelDesignValidator(fileSystem, startId, exclusions, telemetry, version);
        }

        private ModelDesignValidator CreateValidatedValidator()
        {
            ModelDesignValidator validator = CreateValidator();
            validator.Validate(
                [ResourcePath("TestDataDesign.xml")],
                [],
                ResourcePath("TestDataDesign.csv"));
            return validator;
        }

        private static string ResourcePath(string fileName)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", fileName);
        }

        /// <summary>
        /// Regression: the validator resolved a subtype against its base type's
        /// already-validated state, so a base declared after its subtype in the
        /// same design file failed to resolve. Declaration order inside a file
        /// must not matter.
        /// </summary>
        [Test]
        public void ValidateSubtypeDeclaredBeforeItsBaseTypeSucceeds()
        {
            const string path = "memory://out-of-order-design.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(SubtypeBeforeBaseDesign));
            ModelDesignValidator validator = CreateValidator();

            Assert.DoesNotThrow(() => validator.Validate([path], [], null));

            NodeDesign derived = validator.GetNodeDesigns()
                .First(n => n.SymbolicName?.Name == "DerivedType");
            Assert.That(
                ((TypeDesign)derived).BaseTypeNode?.SymbolicName?.Name,
                Is.EqualTo("MiddleType"));
        }

        /// <summary>
        /// Regression: an explicit NumericId was rejected when the same number
        /// was already used by a node in another loaded namespace. A NodeId is
        /// only unique within its own namespace.
        /// </summary>
        [Test]
        public void ValidateExplicitNumericIdMatchingAStandardIdSucceeds()
        {
            const string path = "memory://numeric-id-design.xml";
            // 58 is BaseObjectType in the standard namespace, which is always
            // loaded alongside the target model.
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(ExplicitNumericIdDesign));
            ModelDesignValidator validator = CreateValidator();

            Assert.DoesNotThrow(() => validator.Validate([path], [], null));

            NodeDesign node = validator.GetNodeDesigns()
                .First(n => n.SymbolicName?.Name == "ReusesStandardId");
            Assert.That(node.NumericId, Is.EqualTo(58u));
        }

        /// <summary>
        /// Regression: an explicit ValueRank was overwritten by the shape of the
        /// DefaultValue, so a scalar default on an array-ranked variable
        /// silently downgraded the variable to a scalar.
        /// </summary>
        [Test]
        public void ValidateExplicitValueRankSurvivesTheDefaultValueShape()
        {
            const string path = "memory://value-rank-design.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(ExplicitValueRankDesign));
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([path], [], null);

            var variable = (VariableTypeDesign)validator.GetNodeDesigns()
                .First(n => n.SymbolicName?.Name == "ArrayVariableType");

            Assert.That(
                variable.ValueRank,
                Is.EqualTo(ValueRank.Array),
                "the authored ValueRank is the contract");
        }

        /// <summary>
        /// Regression: an OptionSet's OptionSetValues stopped at bit 31 because
        /// the bit was computed as a signed "1 &lt;&lt; 31", and bits 32-63 of a
        /// 64 bit option set were never walked at all.
        /// </summary>
        [Test]
        public void ValidateOptionSetEmitsValuesAboveBitThirty()
        {
            const string path = "memory://option-set-design.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(WideOptionSetDesign));
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([path], [], null);

            var optionSet = (DataTypeDesign)validator.GetNodeDesigns()
                .First(n => n.SymbolicName?.Name == "WideOptionSet");

            VariableDesign values = optionSet.Children.Items
                .OfType<VariableDesign>()
                .First(v => v.SymbolicName.Name == "OptionSetValues");

            var decoded = (Opc.Ua.LocalizedText[])values.DecodedValue;

            Assert.That(
                decoded,
                Has.Length.EqualTo(40),
                "the 40th bit is the highest one used, so 40 entries are emitted");
            Assert.That(decoded[31].Text, Is.EqualTo("BitThirtyOne"));
            Assert.That(decoded[39].Text, Is.EqualTo("BitThirtyNine"));
        }

        private const string SubtypeBeforeBaseDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
                xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/"
                xmlns="http://test.org/UA/Ordering/"
                TargetNamespace="http://test.org/UA/Ordering/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                    XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    >http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Ordering" Prefix="Ordering">http://test.org/UA/Ordering/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="DerivedType" BaseType="MiddleType" />
              <opc:ObjectType SymbolicName="MiddleType" BaseType="RootType" />
              <opc:ObjectType SymbolicName="RootType" BaseType="ua:BaseObjectType" />
            </opc:ModelDesign>
            """;

        private const string ExplicitNumericIdDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
                xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/"
                xmlns="http://test.org/UA/Ids/"
                TargetNamespace="http://test.org/UA/Ids/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                    XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    >http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Ids" Prefix="Ids">http://test.org/UA/Ids/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="ReusesStandardId" BaseType="ua:BaseObjectType"
                  NumericId="58" />
            </opc:ModelDesign>
            """;

        private const string ExplicitValueRankDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
                xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/"
                xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
                xmlns="http://test.org/UA/Ranks/"
                TargetNamespace="http://test.org/UA/Ranks/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                    XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    >http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Ranks" Prefix="Ranks">http://test.org/UA/Ranks/</opc:Namespace>
              </opc:Namespaces>
              <opc:VariableType SymbolicName="ArrayVariableType" BaseType="ua:BaseDataVariableType"
                  DataType="ua:Int32" ValueRank="Array">
                <opc:DefaultValue>
                  <uax:Int32>0</uax:Int32>
                </opc:DefaultValue>
              </opc:VariableType>
            </opc:ModelDesign>
            """;

        private const string WideOptionSetDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
                xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/"
                xmlns="http://test.org/UA/Options/"
                TargetNamespace="http://test.org/UA/Options/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                    XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    >http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Options" Prefix="Options">http://test.org/UA/Options/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="WideOptionSet" BaseType="ua:UInt64" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="BitZero" Identifier="1" />
                  <opc:Field Name="BitThirty" Identifier="1073741824" />
                  <opc:Field Name="BitThirtyOne" Identifier="2147483648" />
                  <opc:Field Name="BitThirtyNine" Identifier="549755813888" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private const string UndefinedBaseTypeDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
                xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/"
                xmlns="http://test.org/UA/Undefined/"
                TargetNamespace="http://test.org/UA/Undefined/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                    XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    >http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Undefined" Prefix="Undefined">http://test.org/UA/Undefined/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="MyType" BaseType="ua:ThisTypeDoesNotExist" />
            </opc:ModelDesign>
            """;
    }
}
