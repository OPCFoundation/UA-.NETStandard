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
        public void Embed_Utf16TextResource_EmitsEscapedConstString()
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
        public void Embed_Utf16TextReaderResource_EmitsConstString()
        {
            using var reader = new StringReader("abc");
            string content = Embed(new TextReaderResource("Letters", reader, AsUtf16: true));

            Assert.That(content, Does.Contain("public const string Letters = \"abc\";"));
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
