/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the EndpointsGenerator class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EndpointsGeneratorTests
    {
        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private GeneratorContext m_context = null!;

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
            GeneratorContext? context = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new EndpointsGenerator(context!));
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
            var generator = new EndpointsGenerator(m_context);

            // Assert
            Assert.That(generator, Is.Not.Null);
        }

        /// <summary>
        /// Tests that Emit creates a file with endpoints.
        /// </summary>
        [Test]
        public void Emit_AlwaysCreatesFile()
        {
            // Arrange
            using var memoryStream = new MemoryStream();

            string? capturedPath = null;
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

            var generator = new EndpointsGenerator(m_context);

            // Act
            generator.Emit();

            // Assert
            Assert.That(capturedPath, Is.Not.Null);
            Assert.That(capturedPath, Does.Contain("Opc.Ua.Endpoints.g.cs"));
            Assert.That(capturedPath, Does.StartWith("C:\\output"));
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// A service whose request has no fields (or no request type at all) must not
        /// throw: the request-field guard used <c>||</c> and dereferenced a null request.
        /// </summary>
        [Test]
        public void EmitServiceWithoutRequestFieldsDoesNotThrow()
        {
            using var memoryStream = new MemoryStream();
            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Returns(memoryStream);
            m_mockModelDesign
                .Setup(m => m.GetListOfServices(It.IsAny<ServiceCategory[]>()))
                .Returns(
                [
                    new Service { Name = "NoRequest", Category = ServiceCategory.Test },
                    new Service
                    {
                        Name = "NoFields",
                        Category = ServiceCategory.Test,
                        Request = new DataTypeDesign()
                    }
                ]);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "out",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new EndpointsGenerator(m_context);
            Assert.DoesNotThrow(() => generator.Emit());

            string content = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(content, Does.Contain("ServerInstance.NoRequestAsync("));
            Assert.That(content, Does.Contain("ServerInstance.NoFieldsAsync("));
        }
    }
}
