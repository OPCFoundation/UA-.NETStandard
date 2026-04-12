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

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ExtensionsAdditionalTests
    {
        [Test]
        public void IsDesignOrNodeset2FileReturnsTrueForXml()
        {
            var text = EmbeddedAdditionalText.Create("file.xml");

            Assert.That(text.IsDesignOrNodeset2File(), Is.True);
        }

        [Test]
        public void IsDesignOrNodeset2FileReturnsFalseForCsv()
        {
            var text = EmbeddedAdditionalText.Create("file.csv");

            Assert.That(text.IsDesignOrNodeset2File(), Is.False);
        }

        [Test]
        public void IsIdentifierFileReturnsTrueForCsv()
        {
            var text = EmbeddedAdditionalText.Create("ids.csv");

            Assert.That(text.IsIdentifierFile(), Is.True);
        }

        [Test]
        public void IsIdentifierFileReturnsFalseForXml()
        {
            var text = EmbeddedAdditionalText.Create("ids.xml");

            Assert.That(text.IsIdentifierFile(), Is.False);
        }

        [TestCase("test.XML", true)]
        [TestCase("test.Xml", true)]
        [TestCase("test.txt", false)]
        public void HasFileExtensionIsCaseInsensitive(
            string path, bool expected)
        {
            var text = EmbeddedAdditionalText.Create(path);

            Assert.That(text.HasFileExtension("xml"), Is.EqualTo(expected));
        }

        [Test]
        public void GetStringsWithBuildMetadataPrefix()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_metadata.AdditionalFiles.{SourceGenerator.Name}Items".ToLowerInvariant()] = "a;b"
            });

            List<string> result = options.GetStrings("Items", buildProperty: false);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain("a"));
            Assert.That(result, Does.Contain("b"));
        }

        private sealed class EmbeddedAdditionalText : AdditionalText
        {
            private EmbeddedAdditionalText(string path)
            {
                Path = path;
            }

            public override string Path { get; }

            public override SourceText GetText(
                System.Threading.CancellationToken cancellationToken = default)
            {
                return SourceText.From(string.Empty);
            }

            public static EmbeddedAdditionalText Create(string path)
            {
                return new EmbeddedAdditionalText(path);
            }
        }
    }
}
