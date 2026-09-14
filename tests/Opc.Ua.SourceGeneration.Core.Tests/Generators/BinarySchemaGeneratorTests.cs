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
using System.Globalization;
using System.IO;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the BinarySchemaGenerator class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BinarySchemaGeneratorTests
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
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([]);
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
            Assert.Throws<ArgumentNullException>(() => new BinarySchemaGenerator(context));
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
            var generator = new BinarySchemaGenerator(m_context);

            // Assert
            Assert.That(generator, Is.Not.Null);
        }

        /// <summary>
        /// Tests that Emit creates a binary schema file with the correct name.
        /// </summary>
        [Test]
        public void Emit_CreatesFileWithCorrectName()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

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

            var generator = new BinarySchemaGenerator(m_context);

            // Act
            IEnumerable<Resource> result = generator.Emit();

            // Assert
            Assert.That(capturedPath, Is.Not.Null);
            Assert.That(capturedPath, Does.Contain("Test.Types.bsd"));
            Assert.That(capturedPath, Does.StartWith("C:\\output"));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FirstOrDefault()?.ResourceName, Is.EqualTo("TypesBsd"));
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Tests that Emit with validateOutput false does not perform validation.
        /// </summary>
        [Test]
        public void Emit_WithValidateOutputFalse_DoesNotValidate()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(memoryStream);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "C:\\output",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new BinarySchemaGenerator(m_context);

            // Act
            IEnumerable<Resource> result = generator.Emit();

            // Assert - Only OpenWrite should be called, not OpenRead for validation
            Assert.That(result, Is.Not.Null);
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Tests that Emit returns TextFileResource with correct prefix.
        /// </summary>
        [Test]
        public void Emit_ReturnsTextFileResourceWithCorrectPrefix()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(memoryStream);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "TestOutput",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new BinarySchemaGenerator(m_context);

            // Act
            IEnumerable<Resource> result = generator.Emit();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FirstOrDefault()?.ResourceName, Is.EqualTo("TypesBsd"));
        }

        /// <summary>
        /// Regression: a union was written as a plain field sequence with no
        /// leading SwitchField and no SwitchValue on its members, so the served
        /// DataTypeDictionary described a different wire layout than the
        /// generated Encode/Decode produces.
        /// </summary>
        [Test]
        public void Emit_UnionStructure_WritesSwitchFieldAndSwitchValues()
        {
            DataTypeDesign union = CreateStructure("TestUnion", isUnion: true,
                CreateField("First", BasicDataType.Int32, isOptional: false),
                CreateField("Second", BasicDataType.String, isOptional: false));

            string schema = EmitSchema(union);

            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"SwitchField\" TypeName=\"opc:UInt32\" />"),
                "a union is prefixed by its 32 bit selector");
            Assert.That(
                schema,
                Does.Contain(
                    "<opc:Field Name=\"First\" TypeName=\"opc:Int32\" " +
                    "SwitchField=\"SwitchField\" SwitchValue=\"1\" />"));
            Assert.That(
                schema,
                Does.Contain(
                    "<opc:Field Name=\"Second\" TypeName=\"opc:CharArray\" " +
                    "SwitchField=\"SwitchField\" SwitchValue=\"2\" />"));
        }

        /// <summary>
        /// Regression: a structure with optional fields was written without the
        /// encoding mask's presence bits, and its optional members carried no
        /// SwitchField.
        /// </summary>
        [Test]
        public void Emit_StructureWithOptionalFields_WritesEncodingMaskBits()
        {
            DataTypeDesign structure = CreateStructure("TestOptional", isUnion: false,
                CreateField("Always", BasicDataType.Int32, isOptional: false),
                CreateField("Maybe", BasicDataType.Double, isOptional: true));

            string schema = EmitSchema(structure);

            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"MaybeSpecified\" TypeName=\"opc:Bit\" />"),
                "the optional field's presence bit leads the structure");
            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"Reserved1\" TypeName=\"opc:Bit\" Length=\"31\" />"),
                "the mask is padded to 32 bits");
            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"Always\" TypeName=\"opc:Int32\" />"),
                "a mandatory field has no switch");
            Assert.That(
                schema,
                Does.Contain(
                    "<opc:Field Name=\"Maybe\" TypeName=\"opc:Double\" " +
                    "SwitchField=\"MaybeSpecified\" />"));
        }

        /// <summary>
        /// A structure without optional fields keeps the plain sequence it had.
        /// </summary>
        [Test]
        public void Emit_PlainStructure_WritesNoSwitchFields()
        {
            DataTypeDesign structure = CreateStructure("TestPlain", isUnion: false,
                CreateField("Value", BasicDataType.Int32, isOptional: false));

            string schema = EmitSchema(structure);

            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"Value\" TypeName=\"opc:Int32\" />"));
            Assert.That(schema, Does.Not.Contain("Reserved1"));
            Assert.That(schema, Does.Not.Contain("SwitchField=\""));
        }

        /// <summary>
        /// Regression: the DataType description went into the Documentation
        /// element unescaped, so a description containing '&amp;' (the shipped
        /// OpenUsd NodeSet has one) produced a non-well-formed Types.bsd.
        /// </summary>
        [Test]
        public void Emit_DescriptionWithMarkupCharacters_EscapesTheDocumentation()
        {
            DataTypeDesign structure = CreateStructure("TestDocumented", isUnion: false,
                CreateField("Value", BasicDataType.Int32, isOptional: false));
            structure.Description = new Schema.Model.LocalizedText
            {
                Value = "A&C <not an element>"
            };

            string schema = EmitSchema(structure);

            Assert.That(
                schema,
                Does.Contain(
                    "<opc:Documentation>A&amp;C &lt;not an element&gt;</opc:Documentation>"));
            Assert.That(
                schema,
                Does.Not.Contain("<opc:Documentation>A&C"),
                "the raw text makes the emitted schema non-well-formed");
        }

        /// <summary>
        /// Regression: only the Documentation element text was escaped. Field
        /// and enum-value names are authored data too, and they land in XML
        /// attributes - an unescaped '&amp;' there makes the served
        /// DataTypeDictionary non-well-formed just the same.
        /// </summary>
        [Test]
        public void Emit_FieldNameWithMarkupCharacters_EscapesTheAttribute()
        {
            DataTypeDesign structure = CreateStructure("TestEscaped", isUnion: false,
                CreateField("Read&Write", BasicDataType.Int32, isOptional: false));

            string schema = EmitSchema(structure);

            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"Read&amp;Write\" TypeName=\"opc:Int32\" />"));
            Assert.That(
                schema,
                Does.Not.Contain("Name=\"Read&Write\""),
                "the raw name makes the emitted schema non-well-formed");
        }

        /// <summary>
        /// The binary encoding mask is 32 bits wide (OPC 10000-6 5.2.7), so a
        /// 33rd optional field has no presence bit. Emitting the schema anyway
        /// would describe a mask wider than the one the generated encoder
        /// writes, and the generated Fields enum would wrap "1 &lt;&lt; 32" back
        /// onto the first field's bit.
        /// </summary>
        [Test]
        public void Emit_StructureWithMoreThan32OptionalFields_Fails()
        {
            Parameter[] fields = new Parameter[33];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                fields[ii] = CreateField(
                    "Field" + ii.ToString(CultureInfo.InvariantCulture),
                    BasicDataType.Int32,
                    isOptional: true);
            }

            DataTypeDesign structure = CreateStructure("TooManyOptional", false, fields);

            Assert.That(
                () => EmitSchema(structure),
                Throws.InstanceOf<InvalidOperationException>());
        }

        /// <summary>
        /// Exactly 32 optional fields fill the mask, so no padding is written.
        /// </summary>
        [Test]
        public void Emit_StructureWith32OptionalFields_WritesNoPadding()
        {
            Parameter[] fields = new Parameter[32];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                fields[ii] = CreateField(
                    "Field" + ii.ToString(CultureInfo.InvariantCulture),
                    BasicDataType.Int32,
                    isOptional: true);
            }

            string schema = EmitSchema(CreateStructure("FullMask", false, fields));

            Assert.That(
                schema,
                Does.Contain("<opc:Field Name=\"Field31Specified\" TypeName=\"opc:Bit\" />"));
            Assert.That(schema, Does.Not.Contain("Reserved1"));
        }

        private string EmitSchema(params DataTypeDesign[] dataTypes)
        {
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns(dataTypes);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<Parameter>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            m_context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            new BinarySchemaGenerator(m_context) { ValidateOutput = false }.Emit();

            return System.Text.Encoding.UTF8.GetString(
                fileSystem.Get(Path.Combine("out", "Test.Types.bsd")));
        }

        private static DataTypeDesign CreateStructure(
            string name,
            bool isUnion,
            params Parameter[] fields)
        {
            const string uri = "http://test.org/UA/";
            var structure = new DataTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName(name, uri),
                SymbolicName = new System.Xml.XmlQualifiedName(name, uri),
                BrowseName = name,
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
                Fields = fields
            };
            foreach (Parameter field in fields)
            {
                field.Parent = structure;
            }
            return structure;
        }

        private static Parameter CreateField(
            string name,
            BasicDataType basicType,
            bool isOptional)
        {
            return new Parameter
            {
                Name = name,
                IsOptional = isOptional,
                ValueRank = ValueRank.Scalar,
                DataTypeNode = new DataTypeDesign
                {
                    SymbolicId = new System.Xml.XmlQualifiedName(
                        basicType.ToString(), Types.Namespaces.OpcUa),
                    SymbolicName = new System.Xml.XmlQualifiedName(
                        basicType.ToString(), Types.Namespaces.OpcUa),
                    BasicDataType = basicType
                }
            };
        }
    }
}
