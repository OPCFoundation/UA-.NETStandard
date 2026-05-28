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
    /// Tests for UA0007 (obsolete NodeId/ExpandedNodeId string constructor).
    /// </summary>
    [TestFixture]
    public class UA0007Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnNewNodeIdStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M() => new NodeId("ns=1;i=42");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source);

            Diagnostic? ua0007 = diags.SingleOrDefault(d => d.Id == "UA0007");
            Assert.That(ua0007, Is.Not.Null);
            Assert.That(
                ua0007!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("NodeId"));
        }

        [Test]
        public async Task ReportsDiagnosticOnNewExpandedNodeIdStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static ExpandedNodeId M() => new ExpandedNodeId("ns=1;i=42");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source);

            Diagnostic? ua0007 = diags.SingleOrDefault(d => d.Id == "UA0007");
            Assert.That(ua0007, Is.Not.Null);
            Assert.That(
                ua0007!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("ExpandedNodeId"));
        }

        [Test]
        public async Task DoesNotReportOnNewNodeIdUintAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M() => new NodeId(42u);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0007"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnNewNodeIdUintNamespaceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M() => new NodeId(42u, (ushort)0);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0007"), Is.False);
        }

        [Test]
        public async Task FixRewritesStringCtorToParseAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M(string s) => new NodeId(s);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static NodeId M(string s) => NodeId.Parse(s);
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0007ObsoleteNodeIdStringCtorAnalyzer(),
                new UA0007ObsoleteNodeIdStringCtorCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
