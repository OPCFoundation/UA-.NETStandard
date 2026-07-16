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

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0015 (obsolete sync/APM members on GDS/LDS discovery clients).
    /// </summary>
    [TestFixture]
    public class UA0015Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnGdsRegisterApplicationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(GlobalDiscoveryServerClient gdsClient)
                    {
                #pragma warning disable CS0618
                        gdsClient.RegisterApplication("urn:foo");
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0015GdsSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0015 = diags.SingleOrDefault(d => d.Id == "UA0015");
            Assert.That(ua0015, Is.Not.Null, "Expected UA0015 to fire on RegisterApplication.");
            Assert.That(
                ua0015!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("RegisterApplication"));
        }

        [Test]
        public async Task ReportsDiagnosticOnServerPushApplyChangesAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ServerPushConfigurationClient pushClient)
                    {
                #pragma warning disable CS0618
                        pushClient.ApplyChanges();
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0015GdsSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0015 = diags.SingleOrDefault(d => d.Id == "UA0015");
            Assert.That(ua0015, Is.Not.Null, "Expected UA0015 to fire on ApplyChanges.");
            Assert.That(
                ua0015!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ApplyChanges"));
        }

        [Test]
        public async Task ReportsDiagnosticOnLdsBeginFindServersAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(LocalDiscoveryServerClient ldsClient, string endpoint)
                    {
                #pragma warning disable CS0618
                        ldsClient.BeginFindServers(endpoint, null, null);
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0015GdsSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0015 = diags.SingleOrDefault(d => d.Id == "UA0015");
            Assert.That(ua0015, Is.Not.Null, "Expected UA0015 to fire on BeginFindServers.");
            Assert.That(
                ua0015!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("BeginFindServers"));
        }

        [Test]
        public async Task DoesNotReportOnRegisterApplicationAsyncAsync()
        {
            const string source = """
                using System.Threading;
                using System.Threading.Tasks;
                using Opc.Ua;
                class C
                {
                    static async Task M(GlobalDiscoveryServerClient gdsClient, CancellationToken ct)
                    {
                        await gdsClient.RegisterApplicationAsync("urn:foo", ct);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0015GdsSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0015"), Is.False,
                "RegisterApplicationAsync must not trigger UA0015.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedRegisterApplicationCallAsync()
        {
            const string source = """
                class Other
                {
                    public void RegisterApplication(string uri) { }
                }
                class C
                {
                    static void M(Other o) => o.RegisterApplication("urn:foo");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0015GdsSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0015"), Is.False,
                "RegisterApplication on an unrelated type must not trigger UA0015.");
        }

        [Test]
        public async Task ReportsDiagnosticOnShimExtensionCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(GlobalDiscoveryServerClient client)
                    {
                #pragma warning disable CS0618
                        client.RegisterApplicationLegacy("urn:foo");
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0015GdsSyncToAsyncAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0015"), Is.True,
                "Expected UA0015 to fire on a call resolving to a [OpcUaShim(\"UA0015\")] extension.");
        }
    }
}
