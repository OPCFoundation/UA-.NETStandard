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

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.CodeFixers.Analyzers;
using Opc.Ua.CodeFixers.CodeFixes;

namespace Opc.Ua.CodeFixers.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0005 (byte[] passed where ByteString is now expected).
    /// </summary>
    [TestFixture]
    public class UA0005Tests
    {
        [Test]
        public async Task ReportsOnByteArrayArgumentWhereByteStringIsExpectedAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source);

            Diagnostic? ua0005 = diags.SingleOrDefault(d => d.Id == "UA0005");
            Assert.That(ua0005, Is.Not.Null, "Expected UA0005 to fire when byte[] is passed to a ByteString parameter.");
            Assert.That(
                ua0005!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("M"));
        }

        [Test]
        public async Task DoesNotReportWhenArgumentAlreadyToByteStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr.ToByteString());
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0005"), Is.False,
                "Calling .ToByteString() at the call site must not trigger UA0005.");
        }

        [Test]
        public async Task DoesNotReportWhenByteArrayOverloadBindsAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void Caller(byte[] arr) => ByteStringApi.Process(arr);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0005"), Is.False,
                "When the byte[] overload binds the rule must not fire.");
        }

        [Test]
        public async Task DoesNotReportOnDefaultLiteralAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller() => M(default);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0005"), Is.False,
                "A 'default' literal has type ByteString — UA0005 must not fire.");
        }

        [Test]
        public async Task FixAppendsToByteStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr.ToByteString());
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0005ByteArrayToByteStringAnalyzer(),
                new UA0005ByteArrayToByteStringCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
