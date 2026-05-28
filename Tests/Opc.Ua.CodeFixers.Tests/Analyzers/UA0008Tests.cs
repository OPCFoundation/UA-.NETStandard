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
    /// Tests for UA0008 (Session.Call params object[] → params Variant[]).
    /// </summary>
    [TestFixture]
    public class UA0008Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnSessionCallWithRawArgsAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId objId, NodeId methodId)
                    {
                        session.Call(objId, methodId, 1, "two");
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source);

            Diagnostic? ua0008 = diags.SingleOrDefault(d => d.Id == "UA0008");
            Assert.That(ua0008, Is.Not.Null,
                "Expected UA0008 to fire on Session.Call with raw int / string args.");
            Assert.That(
                ua0008!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Call"));
        }

        [Test]
        public async Task ReportsDiagnosticOnSessionCallAsyncWithRawArgsAsync()
        {
            const string source = """
                using System.Threading;
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId objId, NodeId methodId, CancellationToken ct)
                    {
                        _ = session.CallAsync(objId, methodId, ct, 42);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0008"), Is.True,
                "Expected UA0008 to fire on Session.CallAsync with a raw int arg.");
        }

        [Test]
        public async Task DoesNotReportWhenAllArgsAreVariantAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId objId, NodeId methodId)
                    {
                        session.Call(objId, methodId, Variant.From(1), Variant.From("two"));
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0008"), Is.False,
                "All-Variant arguments must not trigger UA0008.");
        }

        [Test]
        public async Task DoesNotReportWhenNoVariadicArgsAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId objId, NodeId methodId)
                    {
                        session.Call(objId, methodId);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0008"), Is.False,
                "Session.Call with no variadic args must not trigger UA0008.");
        }

        [Test]
        public async Task FixWrapsRawArgsWithVariantFromAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId o, NodeId m)
                    {
                        session.Call(o, m, 1, "two");
                    }
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId o, NodeId m)
                    {
                        session.Call(o, m, Variant.From(1), Variant.From("two"));
                    }
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0008SessionCallParamsObjectAnalyzer(),
                new UA0008SessionCallParamsObjectCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
