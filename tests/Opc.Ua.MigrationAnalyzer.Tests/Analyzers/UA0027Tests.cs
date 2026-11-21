// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// Tests for UA0027 (the protected DataLock removed from NodeBrowser).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of NodeBrowser. The member is gone from the 2.0
    /// assemblies, so the rule has to fire on sources that still compile against the old
    /// surface — which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0027Tests
    {
        private const string BrowserShim = """
            namespace Opc.Ua
            {
                public interface IReference
                {
                }

                public class NodeBrowser
                {
                    protected object DataLock { get; } = new object();

                    public virtual IReference Next() => null;
                }
            }
            """;

        [Test]
        public async Task ReportsOnDataLockInsideADerivedBrowserAsync()
        {
            string source = BrowserShim + """

                class MyBrowser : Opc.Ua.NodeBrowser
                {
                    public override Opc.Ua.IReference Next()
                    {
                        lock (DataLock)
                        {
                            return base.Next();
                        }
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("NodeBrowser.DataLock"));
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("single-consumer"),
                "the message must say why the lock went, not just that it is gone.");
        }

        [Test]
        public async Task ReportsOnQualifiedDataLockAccessAsync()
        {
            string source = BrowserShim + """

                class MyBrowser : Opc.Ua.NodeBrowser
                {
                    public override Opc.Ua.IReference Next()
                    {
                        lock (this.DataLock)
                        {
                            return base.Next();
                        }
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0027"));
        }

        [Test]
        public async Task ReportsOnceForAQualifiedAccessAsync()
        {
            string source = BrowserShim + """

                class MyBrowser : Opc.Ua.NodeBrowser
                {
                    public override Opc.Ua.IReference Next()
                    {
                        lock (this.DataLock)
                        {
                            return base.Next();
                        }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0027RemovedNodeBrowserDataLockAnalyzer(),
                    source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Count(d => d.Id == "UA0027"),
                Is.EqualTo(1),
                "the member-access and identifier handlers must not both report the same expression.");
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public object DataLock { get; } = new object();

                    void M()
                    {
                        lock (DataLock)
                        {
                        }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0027RemovedNodeBrowserDataLockAnalyzer(),
                    source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0027"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        [Test]
        public async Task DoesNotReportOnALocalNamedDataLockInsideABrowserAsync()
        {
            string source = BrowserShim + """

                class MyBrowser : Opc.Ua.NodeBrowser
                {
                    public override Opc.Ua.IReference Next()
                    {
                        object DataLock = new object();
                        lock (DataLock)
                        {
                            return base.Next();
                        }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0027RemovedNodeBrowserDataLockAnalyzer(),
                    source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0027"),
                Is.False,
                "a local that happens to be named DataLock is not the removed member.");
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedNodeBrowserInAnotherNamespaceAsync()
        {
            const string source = """
                namespace Other
                {
                    public class NodeBrowser
                    {
                        protected object DataLock { get; } = new object();
                    }

                    public class MyBrowser : NodeBrowser
                    {
                        public void M()
                        {
                            lock (DataLock)
                            {
                            }
                        }
                    }
                }

                namespace Opc.Ua
                {
                    public class NodeBrowser
                    {
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0027RemovedNodeBrowserDataLockAnalyzer(),
                    source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0027"),
                Is.False,
                "a NodeBrowser in an unrelated namespace must not fire the rule.");
        }

        [Test]
        public async Task ReportsInsideALambdaInADerivedBrowserAsync()
        {
            string source = BrowserShim + """

                class MyBrowser : Opc.Ua.NodeBrowser
                {
                    public System.Action M()
                    {
                        return () =>
                        {
                            lock (DataLock)
                            {
                            }
                        };
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0027"));
        }

        [Test]
        public async Task DoesNotReportOnABrowserThatTookNoLockAsync()
        {
            string source = BrowserShim + """

                class MyBrowser : Opc.Ua.NodeBrowser
                {
                    public override Opc.Ua.IReference Next()
                    {
                        return base.Next();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0027RemovedNodeBrowserDataLockAnalyzer(),
                    source)
                .ConfigureAwait(false);

            Assert.That(diagnostics.Any(d => d.Id == "UA0027"), Is.False);
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0027RemovedNodeBrowserDataLockAnalyzer(),
                    source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0027");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0027 to fire.");
            return diagnostic!;
        }
    }
}
