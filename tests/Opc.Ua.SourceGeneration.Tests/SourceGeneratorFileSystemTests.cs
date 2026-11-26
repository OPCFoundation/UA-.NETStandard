// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
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

            SourceGeneratorFileSystem? fileSystem = null;
            Assert.DoesNotThrow(
                () => fileSystem = new SourceGeneratorFileSystem([first, duplicate]));

            Assert.That(fileSystem!.Exists("design.xml"), Is.True);

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

        /// <summary>
        /// Streamed output preserves the old UTF-8 text, encoding and checksum, including its BOM.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void GeneratedSourceStreamMatchesByteArrayPath(bool includePreamble)
        {
            using var fileSystem = new VirtualFileSystem();
            using (var writer = new StreamWriter(fileSystem.OpenWrite("generated.cs"), new UTF8Encoding(includePreamble)))
            {
                writer.Write("// \u00E9 \U0001F642\r\n");
                writer.Write(new string('x', 100_000));
            }
            var expected = SourceText.From(Encoding.UTF8.GetString(fileSystem.Get("generated.cs")),
                Encoding.UTF8);

            SourceText actual = SourceGenerator.ReadGeneratedSource(fileSystem, "generated.cs");

            Assert.That(actual.ToString(), Is.EqualTo(expected.ToString()));
            Assert.That(actual.Encoding, Is.EqualTo(expected.Encoding));
            Assert.That(actual.ChecksumAlgorithm, Is.EqualTo(expected.ChecksumAlgorithm));
            Assert.That(actual.GetChecksum(), Is.EqualTo(expected.GetChecksum()));
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
