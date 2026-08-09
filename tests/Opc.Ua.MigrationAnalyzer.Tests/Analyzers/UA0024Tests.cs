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
    /// Tests for UA0024 (the diagnostics locks removed from IServerInternal, ISession and
    /// ISubscription).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of the interface it exercises. The members are
    /// gone from the 2.0 assemblies, so the rule has to fire on sources that still compile
    /// against the old surface — which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0024Tests
    {
        private const string ServerShim = """
            namespace Opc.Ua.Server
            {
                public interface IServerInternal
                {
                    object DiagnosticsLock { get; }
                    object DiagnosticsWriteLock { get; }
                }
            }
            """;

        private const string SessionShim = """
            namespace Opc.Ua.Server
            {
                public interface ISession
                {
                    object DiagnosticsLock { get; }
                }
            }
            """;

        private const string SubscriptionShim = """
            namespace Opc.Ua.Server
            {
                public interface ISubscription
                {
                    object DiagnosticsWriteLock { get; }
                }
            }
            """;

        [Test]
        public async Task ReportsOnServerDiagnosticsLockAsync()
        {
            string source = ServerShim + """

                class C
                {
                    static object M(Opc.Ua.Server.IServerInternal server) => server.DiagnosticsLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("IServerInternal.DiagnosticsLock"));
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("UpdateServerDiagnostics"),
                "the message must name the replacement, which is what the compiler error does not.");
        }

        [Test]
        public async Task ReportsOnServerDiagnosticsWriteLockAsync()
        {
            string source = ServerShim + """

                class C
                {
                    static object M(Opc.Ua.Server.IServerInternal server) => server.DiagnosticsWriteLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("DiagnosticsWriteLock"));
        }

        [Test]
        public async Task ReportsOnSessionDiagnosticsLockAsync()
        {
            string source = SessionShim + """

                class C
                {
                    static object M(Opc.Ua.Server.ISession session) => session.DiagnosticsLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ISession.DiagnosticsLock"));
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("UpdateDiagnostics"));
        }

        [Test]
        public async Task ReportsOnSubscriptionDiagnosticsWriteLockAsync()
        {
            string source = SubscriptionShim + """

                class C
                {
                    static object M(Opc.Ua.Server.ISubscription subscription)
                        => subscription.DiagnosticsWriteLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ISubscription.DiagnosticsWriteLock"));
        }

        [Test]
        public async Task ReportsInsideALockStatementAsync()
        {
            // The shape the migration guide has to talk about: the lock statement is what a
            // consumer actually wrote, and it cannot be rewritten mechanically.
            string source = ServerShim + """

                class C
                {
                    static void M(Opc.Ua.Server.IServerInternal server)
                    {
                        lock (server.DiagnosticsLock)
                        {
                        }
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0024"));
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public object DiagnosticsLock { get; } = new object();
                }

                class C
                {
                    static object M(Unrelated u) => u.DiagnosticsLock;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0024RemovedDiagnosticsLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0024"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        [Test]
        public async Task DoesNotReportOnTheReplacementAsync()
        {
            const string source = """
                namespace Opc.Ua.Server
                {
                    public interface IServerInternal
                    {
                        void UpdateServerDiagnostics(System.Action<object> update);
                    }
                }

                class C
                {
                    static void M(Opc.Ua.Server.IServerInternal server)
                        => server.UpdateServerDiagnostics(_ => { });
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0024RemovedDiagnosticsLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diagnostics.Any(d => d.Id == "UA0024"), Is.False);
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0024RemovedDiagnosticsLockAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0024");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0024 to fire.");
            return diagnostic!;
        }
    }
}
