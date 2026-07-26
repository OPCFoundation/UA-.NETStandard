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
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Verifies the failure and diagnostic-projection paths of the WoT
    /// AdditionalFile conversion wrapper. The wrapper is documented never to
    /// throw (other than on cancellation), so every one of these inputs must
    /// come back as a <see cref="WotConversionOutcome"/> carrying a diagnostic
    /// and no NodeSet.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class WotNodeSetConversionFailureTests
    {
        [Test]
        public void ConvertReportsParseErrorWhenSourceTextThrows()
        {
            var source = new ThrowingAdditionalText(
                "Broken.tm.json",
                new InvalidOperationException("the file could not be opened"));

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                source,
                new NodesetFileOptions(),
                CancellationToken.None);

            Assert.That(outcome.NodeSetText, Is.Null);
            Assert.That(outcome.SourcePath, Is.EqualTo("Broken.tm.json"));
            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("the file could not be opened"));
        }

        [Test]
        public void ConvertPropagatesCancellationFromSourceText()
        {
            var source = new ThrowingAdditionalText(
                "Cancelled.tm.json",
                new OperationCanceledException());

            Assert.That(
                () => WotNodeSetAdditionalText.Convert(
                    source,
                    new NodesetFileOptions(),
                    CancellationToken.None),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void ConvertReportsParseErrorWhenSourceTextIsNull()
        {
            var source = new NullTextAdditionalText("Empty.tm.json");

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                source,
                new NodesetFileOptions(),
                CancellationToken.None);

            Assert.That(outcome.NodeSetText, Is.Null);
            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("could not be read"));
        }

        [Test]
        public void ConvertReportsParseErrorForMalformedJson()
        {
            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.FromContent("Malformed.tm.json", "{ \"title\": "),
                new NodesetFileOptions(),
                CancellationToken.None);

            Assert.That(outcome.NodeSetText, Is.Null);
            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            Assert.That(diagnostic.Location, Is.Not.EqualTo(Location.None));
        }

        [Test]
        public void ConvertAnchorsMalformedJsonDiagnosticAtTheOffendingLine()
        {
            // The syntax error sits on the third line, so the reported location
            // must not collapse to the start of the file.
            const string json = "{\r\n  \"title\": \"Demo\",\r\n  \"base\": ,\r\n}";

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.FromContent("BadLine.tm.json", json),
                new NodesetFileOptions(),
                CancellationToken.None);

            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.That(span.Path, Is.EqualTo("BadLine.tm.json"));
            Assert.That(span.StartLinePosition.Line, Is.GreaterThan(0));
        }

        [Test]
        public void ConvertReportsParseErrorForEmptyDocument()
        {
            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.FromContent("Empty.tm.json", string.Empty),
                new NodesetFileOptions(),
                CancellationToken.None);

            Assert.That(outcome.NodeSetText, Is.Null);
            Assert.That(outcome.Diagnostics.Any(d => d.Id == "MODELGEN030"), Is.True);
        }

        [Test]
        public void ConvertReportsConversionErrorForJsonThatIsNotAWotDocument()
        {
            // Well-formed JSON that carries none of the required WoT members.
            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.FromContent("NotWot.tm.json", "{ \"unrelated\": 1 }"),
                new NodesetFileOptions(),
                CancellationToken.None);

            Assert.That(outcome.NodeSetText, Is.Null);
            Assert.That(
                outcome.Diagnostics.Any(d => d.Id is "MODELGEN030" or "MODELGEN031"),
                Is.True,
                "a document that is not a Thing Model or Thing Description must be rejected");
        }

        [Test]
        public void ConvertPreservesTheSuppliedNodesetFileOptionsOnFailure()
        {
            var options = new NodesetFileOptions();

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.FromContent("Malformed.tm.json", "{"),
                options,
                CancellationToken.None);

            Assert.That(outcome.Options, Is.SameAs(options));
            Assert.That(outcome.NodeSetText, Is.Null);
        }

        [Test]
        public void ConvertProjectsConverterWarningsAsWarningDiagnostics()
        {
            // Source generation performs no I/O, so a tm:extends link can never
            // be resolved and the converter reports an unresolved-reference
            // warning that the wrapper must surface as MODELGEN032.
            const string json = """
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "@type": "tm:ThingModel",
                  "title": "Derived",
                  "links": [
                    {
                      "rel": "tm:extends",
                      "href": "https://example.test/unresolvable.tm.json",
                      "type": "application/tm+json"
                    }
                  ],
                  "properties": {
                    "value": { "type": "number" }
                  }
                }
                """;

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.FromContent("Derived.tm.json", json),
                new NodesetFileOptions(),
                CancellationToken.None);

            Assert.That(
                outcome.Diagnostics.Any(d => d.Id is "MODELGEN031" or "MODELGEN032" or "MODELGEN033"),
                Is.True,
                "converter diagnostics must be projected onto the MODELGEN03x descriptors");
            foreach (Diagnostic diagnostic in outcome.Diagnostics)
            {
                Assert.That(
                    diagnostic.Location.GetLineSpan().Path,
                    Is.EqualTo("Derived.tm.json"),
                    "every projected diagnostic must be anchored at the WoT source file");
            }
        }

        [Test]
        public void ConvertAnchorsUnreadableSourceDiagnosticAtTheFileWithoutAPosition()
        {
            var source = new ThrowingAdditionalText(
                "NoPosition.tm.json",
                new InvalidOperationException("unreadable"));

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                source,
                new NodesetFileOptions(),
                CancellationToken.None);

            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.That(span.Path, Is.EqualTo("NoPosition.tm.json"));
            Assert.That(span.StartLinePosition.Line, Is.Zero);
            Assert.That(span.StartLinePosition.Character, Is.Zero);
        }

        private static Diagnostic SingleDiagnostic(WotConversionOutcome outcome, string id)
        {
            Diagnostic[] matches = outcome.Diagnostics.Where(d => d.Id == id).ToArray();
            Assert.That(
                matches,
                Has.Length.EqualTo(1),
                $"expected exactly one {id} diagnostic but found " +
                string.Join(", ", outcome.Diagnostics.Select(d => d.Id)));
            return matches[0];
        }

        /// <summary>
        /// An AdditionalText whose content cannot be read.
        /// </summary>
        private sealed class ThrowingAdditionalText : AdditionalText
        {
            public ThrowingAdditionalText(string path, Exception exception)
            {
                Path = path;
                m_exception = exception;
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                throw m_exception;
            }

            private readonly Exception m_exception;
        }

        /// <summary>
        /// An AdditionalText that reports no content at all, which the Roslyn
        /// contract permits when a file disappears between discovery and read.
        /// </summary>
        private sealed class NullTextAdditionalText : AdditionalText
        {
            public NullTextAdditionalText(string path)
            {
                Path = path;
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                return null;
            }
        }
    }
}
