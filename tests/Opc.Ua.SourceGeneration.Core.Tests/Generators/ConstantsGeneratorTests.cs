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
    }
}
