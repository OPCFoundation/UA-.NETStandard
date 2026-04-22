/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="ModelDependencyGenerator"/> templating output.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelDependencyGeneratorTests
    {
        private const string TestUri = "http://test.org/UA/";
        private const string TestPrefix = "Test";

        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private MemoryStream m_memoryStream;
        private string m_capturedPath;

        [SetUp]
        public void SetUp()
        {
            m_mockFileSystem = new Mock<IFileSystem>();
            m_mockModelDesign = new Mock<IModelDesign>();
            m_mockTelemetry = new Mock<ITelemetryContext>();
            m_memoryStream = new MemoryStream();
            m_capturedPath = null;

            m_mockFileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>()))
                .Callback<string>(path => m_capturedPath = path)
                .Returns(m_memoryStream);
        }

        [TearDown]
        public void TearDown()
        {
            m_memoryStream?.Dispose();
        }

        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ModelDependencyGenerator(null));
        }

        [Test]
        public void Emit_NoTargetNamespace_ReturnsEmptyAndDoesNotOpenFile()
        {
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns((Namespace)null);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([]);

            var generator = new ModelDependencyGenerator(BuildContext());

            IEnumerable<Resource> result = generator.Emit();

            Assert.That(result, Is.Empty);
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Emit_TargetNamespaceWithoutPrefix_ReturnsEmpty()
        {
            var target = new Namespace { Value = TestUri, Prefix = null };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target]);

            var generator = new ModelDependencyGenerator(BuildContext());

            Assert.That(generator.Emit(), Is.Empty);
            m_mockFileSystem.Verify(fs => fs.OpenWrite(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Emit_SelfOnly_WritesOneAttributeUnderExpectedFileName()
        {
            ConfigureSelf("1.05.04", "2024-05-01T00:00:00Z");

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(m_capturedPath, Does.Contain("Test.ModelDependencies.g.cs"));
            Assert.That(
                output,
                Does.Contain(
                    "[assembly: global::Opc.Ua.ModelDependencyAttribute(\"http://test.org/UA/\", \"Test\", \"1.05.04\", \"2024-05-01T00:00:00Z\")]"));
        }

        [Test]
        public void Emit_NullVersionAndDate_RendersBareNullLiterals()
        {
            ConfigureSelf(version: null, publicationDate: null);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(
                output,
                Does.Contain(
                    "[assembly: global::Opc.Ua.ModelDependencyAttribute(\"http://test.org/UA/\", \"Test\", null, null)]"));
            // Defensive: ensure we never emit the literal string "null".
            Assert.That(output, Does.Not.Contain("\"null\""));
        }

        [Test]
        public void Emit_OpcUaRootInDeclaredNamespaces_IsSkipped()
        {
            Namespace target = ConfigureSelf();
            var opcUa = new Namespace
            {
                Value = Ua.Types.Namespaces.OpcUa,
                Prefix = "Opc.Ua",
                Name = "OpcUa"
            };
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target, opcUa]);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(output, Does.Not.Contain(Ua.Types.Namespaces.OpcUa));
            Assert.That(output, Does.Contain(TestUri));
        }

        [Test]
        public void Emit_DeclaredAndReferencedDeps_DedupesAndPreservesOrder()
        {
            Namespace target = ConfigureSelf();
            var declared = new Namespace
            {
                Value = "http://example.org/UA/Declared/",
                Prefix = "Example.Declared",
                Name = "Declared",
                Version = "1.0",
                PublicationDate = "2024-01-01T00:00:00Z"
            };
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target, declared]);

            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                // Duplicate of declared — must be skipped.
                ["http://example.org/UA/Declared/"] = new ModelDependencyReference(
                    "ExampleAssembly",
                    "http://example.org/UA/Declared/",
                    "Example.Declared",
                    "1.0",
                    "2024-01-01T00:00:00Z"),
                // Unique — must be emitted last.
                ["http://example.org/UA/Referenced/"] = new ModelDependencyReference(
                    "ExampleAssembly",
                    "http://example.org/UA/Referenced/",
                    "Example.Referenced",
                    "2.0",
                    "2024-06-01T00:00:00Z")
            };

            var generator = new ModelDependencyGenerator(BuildContext(referenced));
            generator.Emit();

            string output = ReadOutput();
            int selfIdx = output.IndexOf(TestUri, StringComparison.Ordinal);
            int declaredIdx = output.IndexOf("Declared/", StringComparison.Ordinal);
            int referencedIdx = output.IndexOf("Referenced/", StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(selfIdx, Is.GreaterThan(0), "self entry missing");
                Assert.That(declaredIdx, Is.GreaterThan(selfIdx), "declared after self");
                Assert.That(referencedIdx, Is.GreaterThan(declaredIdx), "referenced after declared");
                int firstDeclared = output.IndexOf(
                    "\"http://example.org/UA/Declared/\"",
                    StringComparison.Ordinal);
                int lastDeclared = output.LastIndexOf(
                    "\"http://example.org/UA/Declared/\"",
                    StringComparison.Ordinal);
                Assert.That(firstDeclared, Is.EqualTo(lastDeclared),
                    "Declared dependency must be emitted exactly once");
            });
        }

        [Test]
        public void Emit_OutputContainsSharedCodeHeaderBanner()
        {
            ConfigureSelf();

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.StartWith("// <auto-generated />"));
                Assert.That(output, Does.Contain("OPC Foundation MIT License"));
                Assert.That(output, Does.Contain("#nullable enable annotations"));
            });
        }

        [Test]
        public void Emit_QuoteCharacterInValue_IsEscaped()
        {
            var target = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "Test",
                Version = "v\"1\"",
                PublicationDate = null
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target]);
            m_mockModelDesign.Setup(m => m.TargetVersion).Returns((string)null);
            m_mockModelDesign.Setup(m => m.TargetPublicationDate).Returns((DateTime?)null);

            var generator = new ModelDependencyGenerator(BuildContext());
            generator.Emit();

            string output = ReadOutput();
            Assert.That(output, Does.Contain("\"v\\\"1\\\"\""));
        }

        private Namespace ConfigureSelf(
            string version = "1.05.04",
            string publicationDate = "2024-05-01T00:00:00Z")
        {
            var target = new Namespace
            {
                Value = TestUri,
                Prefix = TestPrefix,
                Name = "Test",
                Version = version,
                PublicationDate = publicationDate
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(target);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([target]);
            m_mockModelDesign.Setup(m => m.TargetVersion).Returns((string)null);
            m_mockModelDesign.Setup(m => m.TargetPublicationDate).Returns((DateTime?)null);
            return target;
        }

        private GeneratorContext BuildContext(
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null)
        {
            var context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "C:\\output",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };
            if (referencedModels != null)
            {
                context = context with { ReferencedModels = referencedModels };
            }
            return context;
        }

        private string ReadOutput()
        {
            // The generator wraps the captured stream in a StreamWriter and
            // disposes it on Emit(); the stream's Position is at the end.
            return Encoding.UTF8.GetString(m_memoryStream.ToArray());
        }
    }
}
