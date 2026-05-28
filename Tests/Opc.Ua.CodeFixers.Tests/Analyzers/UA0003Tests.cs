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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.CodeFixers.Analyzers;
using Opc.Ua.CodeFixers.CodeFixes;

namespace Opc.Ua.CodeFixers.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0003 (null comparison on now-struct built-in type).
    /// </summary>
    [TestFixture]
    public class UA0003Tests
    {
        [Test]
        public async Task ReportsOnNodeIdEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n == null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source);

            Diagnostic? ua0003 = diags.SingleOrDefault(d => d.Id == "UA0003");
            Assert.That(ua0003, Is.Not.Null);
            Assert.That(
                ua0003!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("NodeId"));
        }

        [Test]
        public async Task ReportsOnNullEqualsNodeIdAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => null == n;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.True);
        }

        [Test]
        public async Task ReportsOnLocalizedTextNotEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText lt) => lt != null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source);

            Diagnostic? ua0003 = diags.SingleOrDefault(d => d.Id == "UA0003");
            Assert.That(ua0003, Is.Not.Null);
            Assert.That(
                ua0003!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("LocalizedText"));
        }

        [Test]
        public async Task DoesNotReportOnStringEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(string s) => s == null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnEqualsMethodCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n.Equals(null);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.False);
        }

        [Test]
        public async Task FixRewritesNodeIdEqualsNullToIsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n == null;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n.IsNull;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0003NullCheckOnStructTypeAnalyzer(),
                new UA0003NullCheckOnStructTypeCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task FixRewritesLocalizedTextNotEqualsNullToNotIsNullOrEmptyAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText lt) => lt != null;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText lt) => !lt.IsNullOrEmpty;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0003NullCheckOnStructTypeAnalyzer(),
                new UA0003NullCheckOnStructTypeCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
