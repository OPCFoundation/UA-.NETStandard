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
using System.IO;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the ConstantsGenerator class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConstantsGeneratorTests
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
            Assert.Throws<ArgumentNullException>(() => new ConstantsGenerator(context));
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
            var generator = new ConstantsGenerator(m_context);

            // Assert
            Assert.That(generator, Is.Not.Null);
        }

        /// <summary>
        /// Tests that Emit returns early without creating files when no nodes exist.
        /// </summary>
        [Test]
        public void Emit_NoNodes_ReturnsEarlyWithoutCreatingFiles()
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

            var generator = new ConstantsGenerator(m_context);

            // Act
            generator.Emit();

            // Assert - OpenWrite should not be called when there are no browse names
            m_mockFileSystem.Verify(
                fs => fs.OpenWrite(It.IsAny<string>()),
                Times.Never,
                "OpenWrite should not be called when there are no browse names");
        }

        /// <summary>
        /// Tests that Emit creates a file with the correct name when nodes with browse names exist.
        /// </summary>
        [Test]
        public void Emit_WithNodes_CreatesFileWithCorrectName()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

            var objectType = new ObjectTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName("TestObjectType", "http://test.org/UA/"),
                SymbolicName = new System.Xml.XmlQualifiedName("TestObjectType", "http://test.org/UA/"),
                BrowseName = "TestObjectType"
            };

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);
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

            var generator = new ConstantsGenerator(m_context);

            // Act
            generator.Emit();

            // Assert
            Assert.That(capturedPath, Is.Not.Null);
            Assert.That(capturedPath, Does.Contain("Test.Constants.g.cs"));
            Assert.That(capturedPath, Does.StartWith("C:\\output"));
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Tests that Emit handles empty output folder.
        /// </summary>
        [Test]
        public void Emit_EmptyOutputFolder_CreatesFileInCurrentDirectory()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

            var objectType = new ObjectTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName("TestObjectType", "http://test.org/UA/"),
                SymbolicName = new System.Xml.XmlQualifiedName("TestObjectType", "http://test.org/UA/"),
                BrowseName = "TestObjectType"
            };

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            string capturedPath = null;
            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Callback<string>(path => capturedPath = path)
                .Returns(memoryStream);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = string.Empty,
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new ConstantsGenerator(m_context);

            // Act
            generator.Emit();

            // Assert
            Assert.That(capturedPath, Is.Not.Null);
            Assert.That(capturedPath, Does.Contain("Test.Constants.g.cs"));
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Regression: when a namespace's XmlNamespace equals its URI, the URI
        /// was queued twice and both entries rendered under the "...Xsd" name,
        /// so the model got two identical Xsd constants (CS0102) and no plain
        /// constant at all.
        /// </summary>
        [Test]
        public void Emit_XmlNamespaceEqualToNamespaceUri_EmitsASinglePlainConstant()
        {
            const string uri = "http://test.org/UA/";
            var targetNamespace = new Namespace
            {
                Value = uri,
                XmlNamespace = uri,
                Prefix = "Test",
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace]);

            string output = EmitWithSingleObjectType(targetNamespace);

            Assert.That(
                output,
                Does.Contain("public const string TestNamespace = \"http://test.org/UA/\";"),
                "the namespace must get its plain constant");
            Assert.That(
                output,
                Does.Not.Contain("TestNamespaceXsd"),
                "no Xsd companion when the XML namespace is the namespace URI");
        }

        /// <summary>
        /// A distinct XML namespace still gets its own "...Xsd" constant, next to
        /// the plain one.
        /// </summary>
        [Test]
        public void Emit_XmlNamespaceDifferentFromNamespaceUri_EmitsBothConstants()
        {
            var targetNamespace = new Namespace
            {
                Value = "http://test.org/UA/",
                XmlNamespace = "http://test.org/UA/Types.xsd",
                Prefix = "Test",
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace]);

            string output = EmitWithSingleObjectType(targetNamespace);

            Assert.That(
                output,
                Does.Contain("public const string TestNamespace = \"http://test.org/UA/\";"));
            Assert.That(
                output,
                Does.Contain(
                    "public const string TestNamespaceXsd = \"http://test.org/UA/Types.xsd\";"));
        }

        /// <summary>
        /// Regression: the DefaultInstanceBrowseName value is authored data, not
        /// a symbolic name, so it can contain spaces and punctuation. It used to
        /// be emitted verbatim as the constant's identifier
        /// (<c>public const string Pump 1 = ...</c>).
        /// </summary>
        [Test]
        public void Emit_DefaultInstanceBrowseNameWithSpaces_EmitsALegalIdentifier()
        {
            var targetNamespace = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace]);

            var objectType = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "PumpType", targetNamespace.Value),
                SymbolicId = new System.Xml.XmlQualifiedName(
                    "PumpType", targetNamespace.Value),
                BrowseName = "PumpType",
                Children = new ListOfChildren
                {
                    Items =
                    [
                        new PropertyDesign
                        {
                            SymbolicName = new System.Xml.XmlQualifiedName(
                                Types.BrowseNames.DefaultInstanceBrowseName,
                                Types.Namespaces.OpcUa),
                            BrowseName = Types.BrowseNames.DefaultInstanceBrowseName,
                            DecodedValue = new QualifiedName("Pump 1")
                        }
                    ]
                },
                HasChildren = true
            };

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            m_context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            new ConstantsGenerator(m_context).Emit();
            string output = System.Text.Encoding.UTF8.GetString(
                fileSystem.Get(Path.Combine("out", "Test.Constants.g.cs")));

            Assert.That(
                output,
                Does.Contain("public const string Pump1 = \"Pump 1\";"),
                "the identifier is sanitized while the value stays the browse name");
            Assert.That(
                output,
                Does.Not.Contain("public const string Pump 1"),
                "a name with a space is not a legal C# identifier");
        }

        /// <summary>
        /// Regression: two namespaces can sanitize to the same constant name
        /// (GetNameFromUri drops the host). Deduping on the name alone dropped
        /// the second one's constant entirely while GetConstantSymbolForNamespace
        /// still formatted that name for it, so every reference to the second
        /// namespace silently resolved to the first one's URI. The Namespaces
        /// class can only hold one member of the name, so this is reported.
        /// </summary>
        [Test]
        public void Emit_TwoNamespacesWithTheSameNameButDifferentUris_Throws()
        {
            var targetNamespace = new Namespace
            {
                Value = "http://a.org/UA/Robotics/",
                Prefix = "Test",
                Name = "Robotics"
            };
            var other = new Namespace
            {
                Value = "http://b.org/UA/Robotics/",
                Prefix = "Test",
                Name = "Robotics"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace, other]);

            Assert.That(
                () => EmitWithSingleObjectType(targetNamespace),
                Throws.TypeOf<ServiceResultException>(),
                "emitting both gives CS0102 and dropping one resolves to the wrong URI");
        }

        /// <summary>
        /// The same namespace listed twice is not a collision - it collapses to
        /// one constant.
        /// </summary>
        [Test]
        public void Emit_TheSameNamespaceListedTwice_EmitsOneConstant()
        {
            var targetNamespace = new Namespace
            {
                Value = "http://a.org/UA/Robotics/",
                Prefix = "Test",
                Name = "Robotics"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign
                .Setup(m => m.Namespaces)
                .Returns([targetNamespace, targetNamespace]);

            string output = EmitWithSingleObjectType(targetNamespace);

            Assert.That(
                CountOccurrences(output, "public const string Robotics ="),
                Is.EqualTo(1));
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            for (int index = text.IndexOf(value, StringComparison.Ordinal);
                index >= 0;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// Regression: sanitizing can land on a constant name a sibling already
        /// claimed ("Pump Type" and the symbolic name "PumpType" both yield
        /// "PumpType"). The dictionary mixes symbolic names with sanitized
        /// browse names, so a hit is not necessarily a design error - it must
        /// neither overwrite the existing entry nor fail the whole model.
        /// </summary>
        [Test]
        public void Emit_DefaultInstanceBrowseNameCollidingWithASymbolicName_KeepsTheFirstEntry()
        {
            var targetNamespace = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace]);

            var objectType = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "PumpType", targetNamespace.Value),
                SymbolicId = new System.Xml.XmlQualifiedName(
                    "PumpType", targetNamespace.Value),
                BrowseName = "PumpType",
                Children = new ListOfChildren
                {
                    Items =
                    [
                        new PropertyDesign
                        {
                            SymbolicName = new System.Xml.XmlQualifiedName(
                                Types.BrowseNames.DefaultInstanceBrowseName,
                                Types.Namespaces.OpcUa),
                            BrowseName = Types.BrowseNames.DefaultInstanceBrowseName,
                            // Sanitizes onto "PumpType", which the type above
                            // already claimed with a different value.
                            DecodedValue = new QualifiedName("Pump Type")
                        }
                    ]
                },
                HasChildren = true
            };

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            m_context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new ConstantsGenerator(m_context);
            Assert.That(() => generator.Emit(), Throws.Nothing);

            string output = System.Text.Encoding.UTF8.GetString(
                fileSystem.Get(Path.Combine("out", "Test.Constants.g.cs")));

            Assert.That(
                output,
                Does.Contain("public const string PumpType = \"PumpType\";"),
                "the symbolic name entry wins; it is the one generated code uses");
            Assert.That(
                output,
                Does.Not.Contain("public const string PumpType = \"Pump Type\";"));
        }

        private string EmitWithSingleObjectType(Namespace targetNamespace)
        {
            var objectType = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "TestObjectType", targetNamespace.Value),
                SymbolicId = new System.Xml.XmlQualifiedName(
                    "TestObjectType", targetNamespace.Value),
                BrowseName = "TestObjectType"
            };

            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([objectType]);
            m_mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            m_context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            new ConstantsGenerator(m_context).Emit();
            return System.Text.Encoding.UTF8.GetString(
                fileSystem.Get(Path.Combine("out", "Test.Constants.g.cs")));
        }
    }
}
