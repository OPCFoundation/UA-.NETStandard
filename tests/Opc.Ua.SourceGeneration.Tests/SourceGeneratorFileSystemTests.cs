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
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for the AdditionalText-backed file system the generator reads its
    /// design and NodeSet2 inputs through.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SourceGeneratorFileSystemTests
    {
        /// <summary>
        /// Regression: the constructor built its index with ToDictionary, which
        /// throws on a duplicate key. A project can list the same file as an
        /// AdditionalFile more than once (a glob overlapping an explicit item),
        /// and the throw escaped as a bare generator crash before any guarded
        /// region was reached.
        /// </summary>
        [Test]
        public void ConstructorToleratesDuplicateAdditionalFilePaths()
        {
            var first = new StubAdditionalText("design.xml", "<first/>");
            var duplicate = new StubAdditionalText("design.xml", "<duplicate/>");

            SourceGeneratorFileSystem fileSystem = null;
            Assert.DoesNotThrow(
                () => fileSystem = new SourceGeneratorFileSystem([first, duplicate]));

            Assert.That(fileSystem.Exists("design.xml"), Is.True);

            using Stream stream = fileSystem.OpenRead("design.xml");
            using var reader = new StreamReader(stream);

            Assert.That(
                reader.ReadToEnd(),
                Is.EqualTo("<first/>"),
                "the first entry wins, the duplicate is ignored");
        }

        /// <summary>
        /// Distinct paths are both indexed.
        /// </summary>
        [Test]
        public void ConstructorIndexesDistinctPaths()
        {
            var first = new StubAdditionalText("a.xml", "<a/>");
            var second = new StubAdditionalText("b.xml", "<b/>");

            var fileSystem = new SourceGeneratorFileSystem([first, second]);

            Assert.That(fileSystem.Exists("a.xml"), Is.True);
            Assert.That(fileSystem.Exists("b.xml"), Is.True);
            Assert.That(fileSystem.Exists("c.xml"), Is.False);
        }

        private sealed class StubAdditionalText : AdditionalText
        {
            public StubAdditionalText(string path, string text)
            {
                Path = path;
                m_text = SourceText.From(text);
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                return m_text;
            }

            private readonly SourceText m_text;
        }
    }
}
