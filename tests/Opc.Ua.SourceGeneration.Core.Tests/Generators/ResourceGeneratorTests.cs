/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using System.Text;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="ResourceGenerator"/> class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ResourceGeneratorTests
    {
        /// <summary>
        /// A UTF-16 string resource is emitted as a const string whose value is an
        /// escaped C# string literal. It used to render as <c>public const string X = ;</c>
        /// because the nested template for the value was empty.
        /// </summary>
        [Test]
        public void EmbedUtf16TextResourceEmitsEscapedConstString()
        {
            string content = Embed(new TextResource(
                "Greeting",
                "Hello \"World\"\r\n\\ end",
                AsUtf16: true));

            Assert.That(
                content,
                Does.Contain(
                    "public const string Greeting = \"Hello \\\"World\\\"\\r\\n\\\\ end\";"));
        }

        /// <summary>
        /// Same as above for the text reader backed resource.
        /// </summary>
        [Test]
        public void EmbedUtf16TextReaderResourceEmitsConstString()
        {
            using var reader = new StringReader("abc");
            string content = Embed(new TextReaderResource("Letters", reader, AsUtf16: true));

            Assert.That(content, Does.Contain("public const string Letters = \"abc\";"));
        }

        /// <summary>
        /// An empty UTF-16 resource must be a constant expression: string.Empty
        /// is not allowed as a const initializer (CS0133).
        /// </summary>
        [Test]
        public void EmbedEmptyUtf16ResourceEmitsEmptyLiteral()
        {
            string content = Embed(new TextResource("Nothing", string.Empty, AsUtf16: true));

            Assert.That(content, Does.Contain("public const string Nothing = \"\";"));
        }

        private static string Embed(Resource resource)
        {
            var fileSystem = new Mock<IFileSystem>();
            var stream = new MemoryStream();
            fileSystem.Setup(fs => fs.OpenWrite(It.IsAny<string>())).Returns(stream);
            var modelDesign = new Mock<IModelDesign>();
            var context = new GeneratorContext
            {
                FileSystem = fileSystem.Object,
                OutputFolder = "out",
                ModelDesign = modelDesign.Object,
                Telemetry = new Mock<ITelemetryContext>().Object,
                Options = new GeneratorOptions()
            };

            new ResourceGenerator(context).Embed("Test", "Strings", false, resource);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
