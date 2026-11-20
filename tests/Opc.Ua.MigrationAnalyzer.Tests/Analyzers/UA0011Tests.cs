/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0011 (IUserIdentityTokenHandler synchronous Encrypt/Decrypt/Sign/Verify
    /// replaced by the *Async counterparts).
    /// </summary>
    [TestFixture]
    public class UA0011Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnEncryptCallOnInterfaceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static byte[] M(IUserIdentityTokenHandler handler, byte[] bytes)
                    {
                #pragma warning disable CS0618
                        return handler.Encrypt(bytes);
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0011 = diags.SingleOrDefault(d => d.Id == "UA0011");
            Assert.That(ua0011, Is.Not.Null, "Expected UA0011 to fire on handler.Encrypt(bytes).");
            Assert.That(
                ua0011!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Encrypt"));
        }

        [Test]
        public async Task ReportsDiagnosticOnSignCallOnInterfaceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static byte[] M(IUserIdentityTokenHandler handler, byte[] bytes)
                    {
                #pragma warning disable CS0618
                        return handler.Sign(bytes);
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0011 = diags.SingleOrDefault(d => d.Id == "UA0011");
            Assert.That(ua0011, Is.Not.Null, "Expected UA0011 to fire on handler.Sign(bytes).");
            Assert.That(
                ua0011!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Sign"));
        }

        [Test]
        public async Task DoesNotReportOnEncryptAsyncCallAsync()
        {
            const string source = """
                using System.Threading;
                using System.Threading.Tasks;
                using Opc.Ua;
                class C
                {
                    static async Task<byte[]> M(IUserIdentityTokenHandler handler, byte[] bytes, CancellationToken ct)
                    {
                        return await handler.EncryptAsync(bytes, ct);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0011"), Is.False,
                "EncryptAsync must not trigger UA0011.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedTypeEncryptCallAsync()
        {
            const string source = """
                class Other
                {
                    public byte[] Encrypt(byte[] data) => data;
                }
                class C
                {
                    static byte[] M(Other o, byte[] bytes) => o.Encrypt(bytes);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0011"), Is.False,
                "Encrypt on an unrelated type must not trigger UA0011.");
        }

        [Test]
        public async Task ReportsDiagnosticOnShimExtensionCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static byte[] M(IUserIdentityTokenHandler handler, byte[] bytes)
                    {
                #pragma warning disable CS0618
                        return UserIdentityTokenHandlerShim.Encrypt(handler, bytes);
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0011 = diags.SingleOrDefault(d => d.Id == "UA0011");
            Assert.That(ua0011, Is.Not.Null,
                "Expected UA0011 to fire on shim extension call carrying [OpcUaShim(\"UA0011\")].");
        }

        [Test]
        public async Task DoesNotReportOnShimWithDifferentRuleIdAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static byte[] M(IUserIdentityTokenHandler handler, byte[] bytes)
                    {
                #pragma warning disable CS0618
                        return UserIdentityTokenHandlerShim.EncryptUnrelated(handler, bytes);
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0011"), Is.False,
                "A shim attribute with a different RuleId must not trigger UA0011.");
        }
    }
}
