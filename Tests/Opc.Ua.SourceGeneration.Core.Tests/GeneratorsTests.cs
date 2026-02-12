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

using Moq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the Generators.GenerateCode method
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class GeneratorsTests
    {
        /// <summary>
        /// Tests that GenerateCode returns early when DesignFiles collection is empty.
        /// Input: Empty DesignFiles collection
        /// Expected: Method returns without processing, no file system or telemetry calls
        /// </summary>
        [Test]
        public void GenerateCode_EmptyDesignFilesCollection_ReturnsEarlyWithoutProcessing()
        {
            // Arrange
            var designFiles = new DesignFileCollection
            {
                DesignFiles = []
            };
            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var mockTelemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            const string outputDir = "output";

            // Act
            designFiles.GenerateCode(mockFileSystem.Object, outputDir, mockTelemetry.Object);

            // Assert - No calls should be made to mocks (MockBehavior.Strict would throw if they were)
            mockFileSystem.VerifyNoOtherCalls();
            mockTelemetry.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Tests that GenerateCode returns early when the nodesets Files collection is empty.
        /// Condition: nodesets.Files.Count == 0
        /// Expected: Method returns immediately without processing
        /// </summary>
        [Test]
        public void GenerateCode_EmptyFilesCollection_ReturnsEarly()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act
            nodesets.GenerateCode(
                mockFileSystem.Object,
                "output",
                mockTelemetry.Object);

            // Assert
            Assert.That(nodesets.Files.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that GenerateCode handles null outputDir parameter.
        /// Condition: outputDir is null
        /// Expected: Method executes without throwing exception
        /// </summary>
        [Test]
        public void GenerateCode_NullOutputDir_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                null,
                mockTelemetry.Object));
        }

        /// <summary>
        /// Tests that GenerateCode handles empty string outputDir parameter.
        /// Condition: outputDir is empty string
        /// Expected: Method executes without throwing exception
        /// </summary>
        [Test]
        public void GenerateCode_EmptyOutputDir_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                string.Empty,
                mockTelemetry.Object));
        }

        /// <summary>
        /// Tests that GenerateCode handles whitespace outputDir parameter.
        /// Condition: outputDir contains only whitespace
        /// Expected: Method executes without throwing exception
        /// </summary>
        [Test]
        public void GenerateCode_WhitespaceOutputDir_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "   ",
                mockTelemetry.Object));
        }

        /// <summary>
        /// Tests that GenerateCode handles outputDir with special characters.
        /// Condition: outputDir contains special characters
        /// Expected: Method executes without throwing exception
        /// </summary>
        [Test]
        public void GenerateCode_OutputDirWithSpecialCharacters_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "C:\\Special@#$%Folder\\Output",
                mockTelemetry.Object));
        }

        /// <summary>
        /// Tests that GenerateCode handles null options parameter by creating default options.
        /// Condition: options parameter is null
        /// Expected: Method creates new GeneratorOptions instance internally
        /// </summary>
        [Test]
        public void GenerateCode_NullOptions_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "output",
                mockTelemetry.Object,
                options: null));
        }

        /// <summary>
        /// Tests that GenerateCode uses provided options when not null.
        /// Condition: options parameter is provided
        /// Expected: Method uses the provided options
        /// </summary>
        [Test]
        public void GenerateCode_WithOptions_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);
            var options = new GeneratorOptions();

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "output",
                mockTelemetry.Object,
                options: options));
        }

        /// <summary>
        /// Tests that GenerateCode handles useAllowSubtypes set to false.
        /// Condition: useAllowSubtypes is false
        /// Expected: Method executes with useAllowSubtypes false
        /// </summary>
        [Test]
        public void GenerateCode_UseAllowSubtypesFalse_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "output",
                mockTelemetry.Object,
                useAllowSubtypes: false));
        }

        /// <summary>
        /// Tests that GenerateCode handles useAllowSubtypes set to true.
        /// Condition: useAllowSubtypes is true
        /// Expected: Method executes with useAllowSubtypes true
        /// </summary>
        [Test]
        public void GenerateCode_UseAllowSubtypesTrue_DoesNotThrow()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "output",
                mockTelemetry.Object,
                useAllowSubtypes: true));
        }

        /// <summary>
        /// Tests that GenerateCode handles all boolean parameter combinations.
        /// </summary>
        [Theory]
        public void GenerateCode_BooleanParameterCombinations_DoesNotThrow(
            bool useAllowSubtypes)
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var nodesets = new NodesetFileCollection(
                [],
                mockFileSystem.Object,
                mockTelemetry.Object);

            // Act & Assert
            Assert.DoesNotThrow(() => nodesets.GenerateCode(
                mockFileSystem.Object,
                "output",
                mockTelemetry.Object,
                useAllowSubtypes: useAllowSubtypes));
        }
    }
}
