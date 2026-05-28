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

namespace Opc.Ua.CodeFixers.Tests.Analyzers
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
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0011TokenHandlerSyncToAsyncAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0011"), Is.False,
                "Encrypt on an unrelated type must not trigger UA0011.");
        }
    }
}
