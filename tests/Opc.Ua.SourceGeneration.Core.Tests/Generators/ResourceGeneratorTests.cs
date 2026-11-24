// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

        /// <summary>
        /// Model text (schema documentation) is embedded as a raw UTF-8 string
        /// literal. A run of three quotes in it used to close the literal and
        /// let the rest of the text compile as code; the delimiter is now
        /// longer than any quote run in the text.
        /// </summary>
        [Test]
        public void EmbedTextWithQuoteRunUsesLongerRawLiteralDelimiter()
        {
            string content = Embed(new TextResource(
                "Doc",
                "<doc>\n\"\"\"u8;\nstatic void Injected() { }\n\"\"\"\"\"</doc>"));

            // Five quotes in the text: the literal is delimited by six.
            Assert.That(content, Does.Contain("\"\"\"\"\"\"u8"));
        }

        /// <summary>
        /// C# ends a line at NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR, which
        /// the line-based raw literal writer does not split on, so text that
        /// contains one is embedded as bytes instead.
        /// </summary>
        [TestCase('\u0085')]
        [TestCase('\u2028')]
        [TestCase('\u2029')]
        public void EmbedTextWithUnicodeLineTerminatorEmitsBytes(char terminator)
        {
            string content = Embed(new TextResource(
                "Doc",
                "<doc>a" + terminator + "\"\"\"u8; static void Injected() { }</doc>"));

            Assert.That(content, Does.Contain("new byte[]"));
            Assert.That(content, Does.Not.Contain("Injected"));
            Assert.That(content, Does.Not.Contain(terminator.ToString()));
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
