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
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixer;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0004 (null-conditional access on now-struct built-in type).
    /// </summary>
    [TestFixture]
    public class UA0004Tests
    {
        [Test]
        public async Task ReportsOnNodeIdConditionalAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId?.NamespaceIndex;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source);

            Diagnostic? ua0004 = diags.SingleOrDefault(d => d.Id == "UA0004");
            Assert.That(ua0004, Is.Not.Null, "Expected UA0004 to fire on nodeId?.NamespaceIndex.");
            Assert.That(
                ua0004!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("NodeId"));
        }

        [Test]
        public async Task ReportsOnDataValueConditionalAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(DataValue dv) => dv?.IsGood;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0004"), Is.True,
                "Expected UA0004 to fire on dv?.IsGood.");
        }

        [Test]
        public async Task DoesNotReportOnStringConditionalAccessAsync()
        {
            const string source = """
                class C
                {
                    static object M(string s) => s?.Length;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0004"), Is.False,
                "Conditional access on string must not trigger UA0004.");
        }

        [Test]
        public async Task DoesNotReportOnPlainMemberAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId.NamespaceIndex;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0004"), Is.False,
                "Plain member access without '?.' must not trigger UA0004.");
        }

        [Test]
        public async Task FixRewritesConditionalAccessToDirectAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId?.NamespaceIndex;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId.NamespaceIndex;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0004ConditionalAccessOnStructAnalyzer(),
                new UA0004ConditionalAccessOnStructCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
