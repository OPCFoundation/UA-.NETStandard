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
    /// Tests for UA0028 (ApplicationConfiguration.PropertiesLock removed).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of the type. The member is gone from the 2.0
    /// assemblies, so the rule has to fire on sources that still compile against the old
    /// surface - which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0028Tests
    {
        private const string ConfigurationShim = """
            using System.Collections.Generic;

            namespace Opc.Ua
            {
                public class ApplicationConfiguration
                {
                    public object PropertiesLock { get; } = new object();

                    public IDictionary<string, object> Properties { get; } =
                        new Dictionary<string, object>();
                }

                public class DerivedConfiguration : ApplicationConfiguration
                {
                }
            }
            """;

        [Test]
        public async Task ReportsOnPropertiesLockAsync()
        {
            string source = ConfigurationShim + """

                class C
                {
                    static object M(Opc.Ua.ApplicationConfiguration configuration)
                        => configuration.PropertiesLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0028"));
        }

        [Test]
        public async Task ReportsInsideALockStatementAsync()
        {
            string source = ConfigurationShim + """

                class C
                {
                    static void M(Opc.Ua.ApplicationConfiguration configuration)
                    {
                        lock (configuration.PropertiesLock)
                        {
                            configuration.Properties["key"] = "value";
                        }
                    }
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("GetOrAddProperty"),
                "the message must name the replacement, which is what the compiler error does not.");
        }

        [Test]
        public async Task ReportsOnADerivedConfigurationAsync()
        {
            string source = ConfigurationShim + """

                class C
                {
                    static object M(Opc.Ua.DerivedConfiguration configuration)
                        => configuration.PropertiesLock;
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0028"));
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public object PropertiesLock { get; } = new object();
                }

                class C
                {
                    static object M(Unrelated u) => u.PropertiesLock;
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0028RemovedPropertiesLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0028"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        [Test]
        public async Task DoesNotReportOnTheReplacementAsync()
        {
            const string source = """
                using System;
                using System.Collections.Generic;

                namespace Opc.Ua
                {
                    public class ApplicationConfiguration
                    {
                        public IDictionary<string, object> Properties { get; } =
                            new Dictionary<string, object>();

                        public T GetOrAddProperty<T>(string key, Func<T> valueFactory)
                            => valueFactory();
                    }
                }

                class C
                {
                    static string M(Opc.Ua.ApplicationConfiguration configuration)
                        => configuration.GetOrAddProperty("key", () => "value");
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0028RemovedPropertiesLockAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diagnostics.Any(d => d.Id == "UA0028"), Is.False);
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0028RemovedPropertiesLockAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0028");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0028 to fire.");
            return diagnostic!;
        }
    }
}
