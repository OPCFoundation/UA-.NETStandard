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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

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
        public void ConvertUsesSynchronousConverterEntryPointForSelfContainedDocument()
        {
            const string json = """
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/"
                    }
                  ],
                  "@type": [
                    "tm:ThingModel",
                    "uav:objectType"
                  ],
                  "title": "SelfContained",
                  "uav:browseName": "nsu=urn:self-contained;SelfContained",
                  "uav:id": "nsu=urn:self-contained;s=SelfContained"
                }
                """;

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.Create("SelfContained.tm.json", json),
                new NodesetFileOptions(),
                CancellationToken.None);
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            WotConversionResult<UANodeSet> conversion = WotNodeSetConverter.ToNodeSetResult(document);

            string expectedXml = WriteNodeSet(conversion.Value!);
            string actualXml = outcome.NodeSetText.GetText(CancellationToken.None)!.ToString();

            Assert.That(outcome.Diagnostics, Is.Empty);
            Assert.That(actualXml, Is.EqualTo(expectedXml));
        }

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
                EmbeddedText.Create("Malformed.tm.json", "{ \"title\": "),
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
                EmbeddedText.Create("BadLine.tm.json", json),
                new NodesetFileOptions(),
                CancellationToken.None);

            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.That(span.Path, Is.EqualTo("BadLine.tm.json"));
            Assert.That(span.StartLinePosition.Line, Is.GreaterThan(0));
        }

        [Test]
        public void ConvertMapsUtf8ByteOffsetAfterAccentedCharacterToSourceTextColumn()
        {
            const string prefix = "{ \"title\": \"Café\", \"base\": ";
            const string json = prefix + "}";

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.Create("BadAccent.tm.json", json),
                new NodesetFileOptions(),
                CancellationToken.None);

            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.That(span.Path, Is.EqualTo("BadAccent.tm.json"));
            Assert.That(span.StartLinePosition.Line, Is.Zero);
            Assert.That(span.StartLinePosition.Character, Is.EqualTo(prefix.Length));
        }

        [Test]
        public void ConvertMapsUtf8ByteOffsetAfterEmojiToSourceTextColumn()
        {
            const string prefix = "{ \"title\": \"Boiler 🔥\", \"base\": ";
            const string json = prefix + "}";

            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.Create("BadEmoji.tm.json", json),
                new NodesetFileOptions(),
                CancellationToken.None);

            Diagnostic diagnostic = SingleDiagnostic(outcome, "MODELGEN030");
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            Assert.That(span.Path, Is.EqualTo("BadEmoji.tm.json"));
            Assert.That(span.StartLinePosition.Line, Is.Zero);
            Assert.That(span.StartLinePosition.Character, Is.EqualTo(prefix.Length));
        }

        [Test]
        public void ConvertReportsParseErrorForEmptyDocument()
        {
            WotConversionOutcome outcome = WotNodeSetAdditionalText.Convert(
                EmbeddedText.Create("Empty.tm.json", string.Empty),
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
                EmbeddedText.Create("NotWot.tm.json", "{ \"unrelated\": 1 }"),
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
                EmbeddedText.Create("Malformed.tm.json", "{"),
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
                EmbeddedText.Create("Derived.tm.json", json),
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

        private static string WriteNodeSet(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            string xml = Encoding.UTF8.GetString(stream.ToArray());
            if (xml.Length > 0 && xml[0] == '\uFEFF')
            {
                xml = xml[1..];
            }
            return xml;
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
