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
using Opc.Ua.MigrationAnalyzer.Analyzers;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0021 (CertificateValidator / CertificateValidationEventArgs structural rename).
    /// The rule is diagnostic-only; there is no accompanying code fix because the 2.0
    /// replacement is structural (event-based per-error accept handler -> async
    /// ValidateAsync returning CertificateValidationResult plus AcceptError callback).
    /// </summary>
    [TestFixture]
    public class UA0021Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnCertificateValidatorReferenceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(CertificateValidator v) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0021 = diags.SingleOrDefault(d => d.Id == "UA0021");
            Assert.That(ua0021, Is.Not.Null);
            Assert.That(
                ua0021!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("CertificateValidator"));
        }

        [Test]
        public async Task ReportsDiagnosticOnCertificateValidationEventArgsReferenceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(CertificateValidationEventArgs e) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0021 = diags.SingleOrDefault(d => d.Id == "UA0021");
            Assert.That(ua0021, Is.Not.Null);
            Assert.That(
                ua0021!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("CertificateValidationEventArgs"));
        }

        [Test]
        public async Task DoesNotReportOnReplacementTypesAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ICertificateManager m, ICertificateValidatorEx v, CertificateValidationResult r) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedSimilarlyNamedTypeAsync()
        {
            // A user-defined CertificateValidator in a non-Opc.Ua namespace, with no
            // 'using Opc.Ua;' anywhere — the symbol resolves to the user's own type
            // (not [Obsolete]) and the syntactic fallback is also gated by the using.
            const string source = """
                namespace Other.Pki
                {
                    public class CertificateValidator { }
                    public class CertificateValidationEventArgs { }
                    class C
                    {
                        static void M(CertificateValidator v, CertificateValidationEventArgs e) { }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.False);
        }

        [Test]
        public async Task ReportsExactlyTwoDiagnosticsForBothLegacyTypesAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(CertificateValidator v, CertificateValidationEventArgs e) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Count(d => d.Id == "UA0021"), Is.EqualTo(2));
        }

        /// <summary>
        /// Regression test for the Phase 11.A relaxation of the syntactic-fallback
        /// using-directive gate. Real-world OPC UA consumer code typically imports
        /// sub-namespaces like <c>using Opc.Ua.Server;</c> rather than the bare
        /// <c>using Opc.Ua;</c>. Before the fix, the syntactic fallback only fired
        /// when the bare directive was present, so UA0021 silently missed every
        /// reference in files that imported only sub-namespaces. The fix accepts
        /// any using directive whose name starts with <c>Opc.Ua</c>.
        /// </summary>
        [Test]
        public async Task ReportsDiagnosticInFileWithOnlyOpcUaServerUsingAsync()
        {
            const string source = """
                using Opc.Ua.Server;   // sub-namespace, no bare Opc.Ua
                class C
                {
                    static void M() => System.Console.WriteLine(typeof(CertificateValidator).FullName);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.True,
                "UA0021 must fire when the file imports any Opc.Ua sub-namespace (not just bare Opc.Ua).");
        }

        /// <summary>
        /// Defensive check: code declared inside the <c>Opc.Ua.*</c> namespace tree
        /// (e.g. a consumer extending the stack) must also trigger the syntactic
        /// fallback even when there is no <c>using</c> directive at all.
        /// </summary>
        [Test]
        public async Task ReportsDiagnosticInNamespaceUnderOpcUaTreeAsync()
        {
            const string source = """
                namespace Opc.Ua.Extensions
                {
                    class C
                    {
                        static void M() => System.Console.WriteLine(typeof(CertificateValidator).FullName);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.True);
        }

        /// <summary>
        /// Negative companion to the relaxation: a file that imports neither
        /// <c>Opc.Ua</c> nor any sub-namespace, and is not declared under the
        /// <c>Opc.Ua</c> tree, must NOT trigger the syntactic fallback even if
        /// it happens to mention an identifier named <c>CertificateValidator</c>.
        /// </summary>
        [Test]
        public async Task DoesNotReportInFileWithNoOpcUaUsingOrNamespaceAsync()
        {
            const string source = """
                namespace Other.Pki
                {
                    class CertificateValidator { }
                    class C
                    {
                        static void M() => System.Console.WriteLine(typeof(CertificateValidator).FullName);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.False);
        }

        /// <summary>
        /// Regression test for #5083: on the bare 2.0 stack (legacy types absent), a
        /// property named <c>CertificateValidator</c> of type <c>ICertificateValidatorEx</c>
        /// must not be reported when it is accessed, assigned, initialized or passed to
        /// <c>nameof</c>.
        /// </summary>
        [Test]
        public async Task DoesNotReportOnPropertyNamedCertificateValidatorAsync()
        {
            const string source = """
                using Opc.Ua;

                namespace Opc.Ua
                {
                    public interface ICertificateValidatorEx { }
                }

                namespace Plc
                {
                    public sealed class DeferredListener
                    {
                        public DeferredListener(ICertificateValidatorEx validator)
                        {
                            CertificateValidator = validator;
                        }

                        public ICertificateValidatorEx CertificateValidator { get; set; }

                        public static string Name => nameof(CertificateValidator);
                    }

                    public static class Usage
                    {
                        public static ICertificateValidatorEx Get(DeferredListener l)
                        {
                            l.CertificateValidator = l.CertificateValidator;
                            var copy = new DeferredListener(l.CertificateValidator)
                            {
                                CertificateValidator = l.CertificateValidator
                            };
                            return copy.CertificateValidator;
                        }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Where(d => d.Id == "UA0021"), Is.Empty);
        }

        /// <summary>
        /// Regression test for #5083: fields, locals, parameters, methods and events named
        /// <c>CertificateValidator</c> / <c>CertificateValidationEventArgs</c> bind to
        /// non-type symbols and must not be reported by the symbol-absent fallback.
        /// </summary>
        [Test]
        public async Task DoesNotReportOnNonTypeSymbolsNamedLikeLegacyTypesAsync()
        {
            const string source = """
                using System;
                using Opc.Ua.Server;

                namespace Plc
                {
                    public sealed class C
                    {
                        private object? CertificateValidationEventArgs;

                        public event EventHandler? CertificateValidator;

                        public void M(object CertificateValidator)
                        {
                            CertificateValidationEventArgs = CertificateValidator;
                            object? CertificateValidationEventArgs2 = this.CertificateValidationEventArgs;
                            _ = CertificateValidationEventArgs2;
                            this.CertificateValidator?.Invoke(this, EventArgs.Empty);
                        }

                        public static int N()
                        {
                            int CertificateValidationEventArgs = 1;
                            return CertificateValidationEventArgs + Helpers.CertificateValidator(1, 2);
                        }
                    }

                    public static class Helpers
                    {
                        public static int CertificateValidator(int a, int b) => a + b;

                        public static int Twice(int a) => CertificateValidator(a, a);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Where(d => d.Id == "UA0021"), Is.Empty);
        }

        /// <summary>
        /// A consumer-defined type named <c>CertificateValidator</c> outside the
        /// <c>Opc.Ua</c> tree is not the legacy type even when the file imports
        /// <c>Opc.Ua</c>, while an unresolved legacy reference in the same file still is.
        /// </summary>
        [Test]
        public async Task ReportsOnlyUnresolvedLegacyTypeWhenOwnTypeIsInScopeAsync()
        {
            const string source = """
                using Opc.Ua;

                namespace Other.Pki
                {
                    public class CertificateValidator { }

                    public static class C
                    {
                        public static void M(CertificateValidator v, CertificateValidationEventArgs e) { }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic[] ua0021 = [.. diags.Where(d => d.Id == "UA0021")];
            Assert.That(ua0021, Has.Length.EqualTo(1));
            Assert.That(
                ua0021[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("CertificateValidationEventArgs"));
        }

        /// <summary>
        /// Unresolved legacy usages on the bare 2.0 stack keep being reported, both in
        /// type positions and as a simple name in a type-only position where a member with
        /// the same name is in scope.
        /// </summary>
        [Test]
        public async Task ReportsUnresolvedLegacyTypeUsagesWithoutStubsAsync()
        {
            const string source = """
                using Opc.Ua;

                public sealed class C
                {
                    public object? CertificateValidator { get; set; }

                    public static void M(CertificateValidationEventArgs e)
                    {
                        System.Console.WriteLine(typeof(CertificateValidator).FullName);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsWithoutStubsAsync(
                    new UA0021CertificateValidatorRenameAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Count(d => d.Id == "UA0021"), Is.EqualTo(2));
        }
    }
}
