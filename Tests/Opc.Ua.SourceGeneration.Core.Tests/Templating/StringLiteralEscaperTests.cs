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

using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StringLiteralEscaperTests
    {
        [Test]
        public void PlainAscii_PassesThroughUnchanged()
        {
            const string input = "TemperatureSensor_42";

            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                input, out bool modified);

            Assert.That(escaped, Is.EqualTo(input));
            Assert.That(modified, Is.False);
            Assert.That(StringLiteralEscaper.RequiresEscaping(input), Is.False);
        }

        [Test]
        public void NullInput_ReturnsEmpty()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                null, out bool modified);

            Assert.That(escaped, Is.EqualTo(string.Empty));
            Assert.That(modified, Is.False);
        }

        [Test]
        public void EmptyInput_ReturnsEmpty()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                string.Empty, out bool modified);

            Assert.That(escaped, Is.EqualTo(string.Empty));
            Assert.That(modified, Is.False);
        }

        [Test]
        public void DoubleQuote_BecomesEscapedQuote()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                "ab\"cd", out bool modified);

            Assert.That(escaped, Is.EqualTo("ab\\\"cd"));
            Assert.That(modified, Is.True);
            AssertRoundTrips("ab\"cd");
        }

        [Test]
        public void Backslash_BecomesEscapedBackslash()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                "a\\b", out bool modified);

            Assert.That(escaped, Is.EqualTo("a\\\\b"));
            Assert.That(modified, Is.True);
            AssertRoundTrips("a\\b");
        }

        [Test]
        public void CarriageReturnNewlineTab_BecomeNamedEscapes()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                "\r\n\t", out bool modified);

            Assert.That(escaped, Is.EqualTo("\\r\\n\\t"));
            Assert.That(modified, Is.True);
            AssertRoundTrips("\r\n\t");
        }

        [Test]
        public void ControlCharacterU0001_BecomesUnicodeEscape()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                "\u0001", out bool modified);

            Assert.That(escaped, Is.EqualTo("\\u0001"));
            Assert.That(modified, Is.True);
            AssertRoundTrips("\u0001");
        }

        [Test]
        public void Delete0x7F_IsEscaped()
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                "\u007f", out bool modified);

            Assert.That(escaped, Is.EqualTo("\\u007F"));
            Assert.That(modified, Is.True);
            AssertRoundTrips("\u007f");
        }

        [Test]
        public void BackslashIsReplacedFirst_NotDouble()
        {
            // The spec calls out: replace '\' first, then '"'. Verifies
            // we don't accidentally re-escape an already-escaped quote.
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                "\\\"", out bool modified);

            Assert.That(escaped, Is.EqualTo("\\\\\\\""));
            Assert.That(modified, Is.True);
            AssertRoundTrips("\\\"");
        }

        [Test]
        public void MixedInput_RoundTripsThroughRoslyn()
        {
            const string original = "weird \"name\" with \\ and \r\n\t and \u001b control";
            AssertRoundTrips(original);
        }

        [Test]
        public void UnicodeCharactersAtOrAbove0x20_PassThrough()
        {
            // Non-ASCII letters are valid in C# regular string literals
            // and should not be escaped by this helper.
            const string input = "TempératureSenseur_Ω";

            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(
                input, out bool modified);

            Assert.That(escaped, Is.EqualTo(input));
            Assert.That(modified, Is.False);
        }

        [Test]
        public void RequiresEscaping_ReturnsTrueForUnsafeChars()
        {
            Assert.That(StringLiteralEscaper.RequiresEscaping("\""), Is.True);
            Assert.That(StringLiteralEscaper.RequiresEscaping("\\"), Is.True);
            Assert.That(StringLiteralEscaper.RequiresEscaping("\r"), Is.True);
            Assert.That(StringLiteralEscaper.RequiresEscaping("\n"), Is.True);
            Assert.That(StringLiteralEscaper.RequiresEscaping("\t"), Is.True);
            Assert.That(StringLiteralEscaper.RequiresEscaping("\u0000"), Is.True);
            Assert.That(StringLiteralEscaper.RequiresEscaping("\u007f"), Is.True);
        }

        [Test]
        public void RequiresEscaping_ReturnsFalseForSafeStrings()
        {
            Assert.That(StringLiteralEscaper.RequiresEscaping(null), Is.False);
            Assert.That(StringLiteralEscaper.RequiresEscaping(string.Empty), Is.False);
            Assert.That(StringLiteralEscaper.RequiresEscaping("hello world"), Is.False);
            Assert.That(StringLiteralEscaper.RequiresEscaping("Ω"), Is.False);
        }

        [Test]
        public void Fuzz_AdversarialStringsRoundTripViaRoslyn()
        {
            // Generate adversarial strings using JSON-style payloads
            // (System.Text.Json produces strings with embedded escapes,
            // surrogate pairs, control chars, etc.).
            string[] seeds =
            [
                "\"",
                "\\",
                "\r\n\t",
                "\u0000",
                "\u001f",
                "\u007f",
                "embedded \" quote",
                "embedded \\ backslash",
                "mixed \"\\\r\n\t\u0001\u007f end",
                "\"\\\"\\\\\\\"",
                JsonSerializer.Serialize("a\"b\\c\u0001\u0002"),
                JsonSerializer.Serialize(new { A = "\"\\", B = "\r\n", C = "\u007f" }),
            ];

            foreach (string seed in seeds)
            {
                AssertRoundTrips(seed);
            }
        }

        [Test]
        public void Fuzz_AllControlChars_RoundTrip()
        {
            var builder = new StringBuilder();
            for (int c = 0; c < 0x20; c++)
            {
                builder.Append((char)c);
            }
            builder.Append((char)0x7f);
            AssertRoundTrips(builder.ToString());
        }

        /// <summary>
        /// Wrap the escaped content in <c>"</c> quotes, parse with
        /// Roslyn as a C# expression, and assert the parsed literal
        /// equals the original input. Proves the helper's contract.
        /// </summary>
        private static void AssertRoundTrips(string original)
        {
            string escaped = StringLiteralEscaper.AsCSharpStringLiteralContent(original);
            string wrapped = "\"" + escaped + "\"";

            ExpressionSyntax expr = SyntaxFactory.ParseExpression(wrapped);
            Assert.That(expr, Is.InstanceOf<LiteralExpressionSyntax>(),
                $"Roslyn could not parse: {wrapped}");
            LiteralExpressionSyntax literal = (LiteralExpressionSyntax)expr;
            Assert.That(literal.Kind(), Is.EqualTo(SyntaxKind.StringLiteralExpression));

            string parsed = (string)literal.Token.Value;
            Assert.That(parsed, Is.EqualTo(original),
                $"Round-trip mismatch. wrapped='{wrapped}'");

            // Also ensure compiler doesn't report any diagnostic on the
            // wrapped literal (catches escape sequences that parse but
            // are illegal, like an unterminated \u escape).
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                $"class C {{ string F() => {wrapped}; }}");
            var diags = tree.GetDiagnostics().Where(
                d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.That(diags, Is.Empty,
                $"Roslyn rejected wrapped literal: {string.Join("; ", diags.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))}");
        }
    }
}
