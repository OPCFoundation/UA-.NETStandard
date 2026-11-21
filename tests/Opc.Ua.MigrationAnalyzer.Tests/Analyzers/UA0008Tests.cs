// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixer;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
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
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source)
                .ConfigureAwait(false);

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
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task ReportsDiagnosticOnShimExtensionCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(Session session, NodeId objId, NodeId methodId)
                    {
                #pragma warning disable CS0618
                        SessionShim.Call(session, objId, methodId, 1, "two");
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0008SessionCallParamsObjectAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0008"), Is.True,
                "Expected UA0008 to fire on a call resolving to a [OpcUaShim(\"UA0008\")] member.");
        }
    }
}
