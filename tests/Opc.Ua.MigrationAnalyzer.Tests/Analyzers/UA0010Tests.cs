// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0010 (using/Dispose on identity types that are no longer
    /// IDisposable in 2.0). The analyzer is diagnostic-only: no code fix is
    /// registered.
    /// </summary>
    [TestFixture]
    public class UA0010Tests
    {
        [Test]
        public async Task ReportsOnUsingDeclarationOfCertificateIdentifierAsync()
        {
            const string source = """
                #pragma warning disable CS1674
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        using var ci = new CertificateIdentifier();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.True,
                "Expected UA0010 to fire on 'using var ci = new CertificateIdentifier();'.");
        }

        [Test]
        public async Task ReportsOnUsingStatementOfUserIdentityAsync()
        {
            const string source = """
                #pragma warning disable CS1674
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        using (var ui = new UserIdentity()) { }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.True,
                "Expected UA0010 to fire on 'using (var ui = new UserIdentity()) { }'.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedDisposableAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        using var ms = new System.IO.MemoryStream();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.False,
                "Unrelated IDisposable types must not trigger UA0010.");
        }

        [Test]
        public async Task DoesNotReportOnPlainDeclarationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        var ci = new CertificateIdentifier();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.False,
                "A plain variable declaration without 'using' must not trigger UA0010.");
        }
    }
}
