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
    /// Tests for UA0026 (BaseVariableValue.Lock removed).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of the type. The member is gone from the 2.0
    /// assemblies, so the rule has to fire on sources that still compile against the old
    /// surface - which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0026Tests
    {
        private const string ValueShim = """
            namespace Opc.Ua
            {
                public class BaseVariableValue
                {
                    public object Lock { get; } = new object();
                }

                public class ServerStatusValue : BaseVariableValue
                {
                }
            }
            """;

        [Test]
        public async Task ReportsOnBaseVariableValueLockAsync()
        {
            string source = ValueShim + """

                class C
                {
                    static object M(Opc.Ua.BaseVariableValue value) => value.Lock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("BaseVariableValue.Lock"));
        }

        [Test]
        public async Task ReportsOnAGeneratedValueClassAsync()
        {
            // What a consumer actually holds: the generated <Type>Value, not the base.
            string source = ValueShim + """

                class C
                {
                    static object M(Opc.Ua.ServerStatusValue status) => status.Lock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ServerStatusValue.Lock"));
        }

        [Test]
        public async Task ReportsInsideALockStatementAsync()
        {
            string source = ValueShim + """

                class C
                {
                    static void M(Opc.Ua.ServerStatusValue status)
                    {
                        lock (status.Lock)
                        {
                        }
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0026"));
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("construct the value with a lock you own"),
                "the message must name the replacement, which is what the compiler error does not.");
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public object Lock { get; } = new object();
                }

                class C
                {
                    static object M(Unrelated u) => u.Lock;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0026RemovedVariableValueLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0026"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        [Test]
        public async Task DoesNotReportOnTheReplacementAsync()
        {
            const string source = """
                namespace Opc.Ua
                {
                    public class BaseVariableValue
                    {
                        public BaseVariableValue(object dataLock)
                        {
                        }
                    }
                }

                class C
                {
                    private readonly object m_lock = new object();

                    void M()
                    {
                        var value = new Opc.Ua.BaseVariableValue(m_lock);
                        lock (m_lock)
                        {
                        }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0026RemovedVariableValueLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diagnostics.Any(d => d.Id == "UA0026"), Is.False);
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0026RemovedVariableValueLockAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0026");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0026 to fire.");
            return diagnostic!;
        }
    }
}
