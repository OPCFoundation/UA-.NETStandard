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
using System.Globalization;
using System.IO;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the DataTypeGenerator class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataTypeGeneratorTests
    {
        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private GeneratorContext m_context;

        [SetUp]
        public void SetUp()
        {
            m_mockFileSystem = new Mock<IFileSystem>();
            m_mockModelDesign = new Mock<IModelDesign>();
            m_mockTelemetry = new Mock<ITelemetryContext>();

            // Setup default namespace
            var targetNamespace = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace]);
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentNullException when context is null.
        /// </summary>
        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            // Arrange
            GeneratorContext context = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DataTypeGenerator(context));
        }

        /// <summary>
        /// Tests that constructor creates instance with valid context.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_CreatesInstance()
        {
            // Arrange
            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "TestOutput",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            // Act
            var generator = new DataTypeGenerator(m_context);

            // Assert
            Assert.That(generator, Is.Not.Null);
        }

        /// <summary>
        /// Tests that Emit returns early without creating files when no data types exist.
        /// </summary>
        [Test]
        public void Emit_NoDataTypes_ReturnsEarlyWithoutCreatingFiles()
        {
            // Arrange
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([]);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "TestOutput",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new DataTypeGenerator(m_context);

            // Act
            generator.Emit();

            // Assert - OpenWrite should not be called
            m_mockFileSystem.Verify(
                fs => fs.OpenWrite(It.IsAny<string>()),
                Times.Never,
                "OpenWrite should not be called when there are no data types");
        }

        /// <summary>
        /// Tests that Emit creates a file with the correct name when data types exist.
        /// </summary>
        [Test]
        public void Emit_WithDataTypes_CreatesFileWithCorrectName()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

            var dataType = new DataTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName("TestDataType", "http://test.org/UA/"),
                SymbolicName = new System.Xml.XmlQualifiedName("TestDataType", "http://test.org/UA/"),
                BasicDataType = BasicDataType.Structure
            };

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([dataType]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            string capturedPath = null;
            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Callback<string>(path => capturedPath = path)
                .Returns(memoryStream);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "C:\\output",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new DataTypeGenerator(m_context);

            // Act
            generator.Emit();

            // Assert
            Assert.That(capturedPath, Is.Not.Null);
            Assert.That(capturedPath, Does.Contain("Test.DataTypes.g.cs"));
            Assert.That(capturedPath, Does.StartWith("C:\\output"));
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Regression: a structure field named after a C# keyword produced
        /// <c>public int event { ... }</c>, which does not compile. The property
        /// identifier is escaped while the DataMember name - the wire name -
        /// stays the authored one.
        /// </summary>
        [Test]
        public void Emit_FieldNamedAfterAKeyword_EscapesThePropertyIdentifier()
        {
            string source = EmitStructureWithField("event", isUnion: false);

            Assert.That(
                source,
                Does.Contain("@event"),
                "the property identifier must be escaped");
            Assert.That(
                source,
                Does.Contain("Name = \"event\""),
                "the wire name stays the authored field name");
        }

        /// <summary>
        /// Regression: a field whose name collides with a member the templates
        /// emit on every generated data type (TypeId here) produced a duplicate
        /// member. The property is renamed; the wire name is unaffected.
        /// </summary>
        [Test]
        public void Emit_FieldNamedAfterAGeneratedMember_RenamesTheProperty()
        {
            string source = EmitStructureWithField("TypeId", isUnion: false);

            Assert.That(source, Does.Contain("TypeIdField"));
            Assert.That(
                source,
                Does.Contain("Name = \"TypeId\""),
                "the wire name stays the authored field name");
        }

        /// <summary>
        /// A union's switch enumeration, its case labels and the value it reads
        /// and writes must all use the same escaped identifier.
        /// </summary>
        [Test]
        public void Emit_UnionFieldNamedAfterAKeyword_IsConsistentAcrossTheType()
        {
            string source = EmitStructureWithField("event", isUnion: true);

            Assert.That(
                source,
                Does.Contain("@event = 1"),
                "the switch enumeration member is escaped");
            Assert.That(
                source,
                Does.Contain("case TestUnionFields.@event:"),
                "the case label uses the same identifier");
            Assert.That(
                source,
                Does.Not.Contain("TestUnionFields.event:"),
                "an unescaped keyword would not compile");
        }

        /// <summary>
        /// The binary encoding mask is 32 bits wide (OPC 10000-6 5.2.7), so the
        /// 33rd optional field has no bit to occupy. "1 &lt;&lt; 32" wraps back to 1
        /// in C#, which would silently hand it the first field's bit and make
        /// Encode set — and Decode read — the wrong presence flag.
        /// </summary>
        [Test]
        public void Emit_StructureWithMoreThan32OptionalFields_Fails()
        {
            Assert.That(
                () => EmitStructureWithOptionalFields(33),
                Throws.InstanceOf<InvalidOperationException>());
        }

        /// <summary>
        /// Exactly 32 optional fields fill the mask; the last one takes the
        /// high bit rather than wrapping.
        /// </summary>
        [Test]
        public void Emit_StructureWith32OptionalFields_UsesTheHighBit()
        {
            string source = EmitStructureWithOptionalFields(32);

            Assert.That(source, Does.Contain("Field0 = 0x1,"));
            Assert.That(source, Does.Contain("Field31 = 0x80000000,"));
        }

        private string EmitStructureWithOptionalFields(int count)
        {
            const string uri = "http://test.org/UA/";
            const string typeName = "TestOptionalStructure";

            var int32 = new DataTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName(
                    "Int32", Types.Namespaces.OpcUa),
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "Int32", Types.Namespaces.OpcUa),
                BasicDataType = BasicDataType.Int32,
                NumericId = 6,
                NumericIdSpecified = true
            };

            var fields = new Parameter[count];
            for (int ii = 0; ii < count; ii++)
            {
                fields[ii] = new Parameter
                {
                    Name = "Field" + ii.ToString(CultureInfo.InvariantCulture),
                    ValueRank = ValueRank.Scalar,
                    IsOptional = true,
                    DataType = int32.SymbolicId,
                    DataTypeNode = int32
                };
            }

            var structure = new DataTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName(typeName, uri),
                SymbolicName = new System.Xml.XmlQualifiedName(typeName, uri),
                BrowseName = typeName,
                ClassName = typeName,
                BasicDataType = BasicDataType.UserDefined,
                IsStructure = true,
                // Selects the ClassWithOptionalFields template, which is what
                // emits the encoding-mask Fields enumeration.
                HasFields = true,
                BaseType = new System.Xml.XmlQualifiedName(
                    "Structure", Types.Namespaces.OpcUa),
                BaseTypeNode = new DataTypeDesign
                {
                    SymbolicId = new System.Xml.XmlQualifiedName(
                        "Structure", Types.Namespaces.OpcUa),
                    SymbolicName = new System.Xml.XmlQualifiedName(
                        "Structure", Types.Namespaces.OpcUa),
                    BasicDataType = BasicDataType.Structure
                },
                Fields = fields
            };
            foreach (Parameter field in fields)
            {
                field.Parent = structure;
            }

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([structure]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<Parameter>())).Returns(false);
            m_mockModelDesign.Setup(m => m.UseAllowSubtypes).Returns(true);

            using var fileSystem = new VirtualFileSystem();
            m_context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            new DataTypeGenerator(m_context).Emit();

            return System.Text.Encoding.UTF8.GetString(
                fileSystem.Get(Path.Combine("out", "Test.DataTypes.g.cs")));
        }

        private string EmitStructureWithField(string fieldName, bool isUnion)
        {
            const string uri = "http://test.org/UA/";
            string typeName = isUnion ? "TestUnion" : "TestStructure";

            var int32 = new DataTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName(
                    "Int32", Types.Namespaces.OpcUa),
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "Int32", Types.Namespaces.OpcUa),
                BasicDataType = BasicDataType.Int32,
                NumericId = 6,
                NumericIdSpecified = true
            };
            var field = new Parameter
            {
                Name = fieldName,
                ValueRank = ValueRank.Scalar,
                DataType = int32.SymbolicId,
                DataTypeNode = int32
            };
            var structure = new DataTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName(typeName, uri),
                SymbolicName = new System.Xml.XmlQualifiedName(typeName, uri),
                BrowseName = typeName,
                ClassName = typeName,
                BasicDataType = BasicDataType.UserDefined,
                IsStructure = true,
                IsUnion = isUnion,
                BaseType = new System.Xml.XmlQualifiedName(
                    "Structure", Types.Namespaces.OpcUa),
                BaseTypeNode = new DataTypeDesign
                {
                    SymbolicId = new System.Xml.XmlQualifiedName(
                        "Structure", Types.Namespaces.OpcUa),
                    SymbolicName = new System.Xml.XmlQualifiedName(
                        "Structure", Types.Namespaces.OpcUa),
                    BasicDataType = BasicDataType.Structure
                },
                Fields = [field]
            };
            field.Parent = structure;

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([structure]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<Parameter>())).Returns(false);
            m_mockModelDesign.Setup(m => m.UseAllowSubtypes).Returns(true);

            using var fileSystem = new VirtualFileSystem();
            m_context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            new DataTypeGenerator(m_context).Emit();

            return System.Text.Encoding.UTF8.GetString(
                fileSystem.Get(Path.Combine("out", "Test.DataTypes.g.cs")));
        }
    }
}
