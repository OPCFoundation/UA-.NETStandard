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
    /// Tests for UA0031 (the four ISubscriptionManager routing wrappers collapsed in 2.0).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of the interface it exercises. The members are
    /// gone from the 2.0 assemblies, so the rule has to fire on sources that still compile
    /// against the old surface — which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0031Tests
    {
        private const string ManagerShim = """
            namespace Opc.Ua.Server
            {
                public interface ISubscriptionManager
                {
                    object Republish(object context, uint subscriptionId, uint retransmitSequenceNumber);
                    void SetTriggering(object context, uint subscriptionId, uint triggeringItemId);
                    object ModifyMonitoredItemsAsync(object context, uint subscriptionId);
                    object SetMonitoringModeAsync(object context, uint subscriptionId);
                    bool TryGetSubscription(uint id, out object subscription);
                }
            }
            """;

        [Test]
        public async Task ReportsOnRepublishAsync()
        {
            string source = ManagerShim + """

                class C
                {
                    static object M(Opc.Ua.Server.ISubscriptionManager manager)
                        => manager.Republish(null, 1u, 2u);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ISubscriptionManager.Republish"));
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("TryGetSubscription"),
                "the message must name the replacement, which is what the compiler error does not.");
        }

        [Test]
        public async Task ReportsOnSetTriggeringAsync()
        {
            string source = ManagerShim + """

                class C
                {
                    static void M(Opc.Ua.Server.ISubscriptionManager manager)
                        => manager.SetTriggering(null, 1u, 2u);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0031"));
        }

        [Test]
        public async Task ReportsOnModifyMonitoredItemsAsync()
        {
            string source = ManagerShim + """

                class C
                {
                    static object M(Opc.Ua.Server.ISubscriptionManager manager)
                        => manager.ModifyMonitoredItemsAsync(null, 1u);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ModifyMonitoredItemsAsync"));
        }

        [Test]
        public async Task ReportsOnSetMonitoringModeAsync()
        {
            string source = ManagerShim + """

                class C
                {
                    static object M(Opc.Ua.Server.ISubscriptionManager manager)
                        => manager.SetMonitoringModeAsync(null, 1u);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0031"));
        }

        [Test]
        public async Task DoesNotReportOnTheReplacementAsync()
        {
            string source = ManagerShim + """

                class C
                {
                    static bool M(Opc.Ua.Server.ISubscriptionManager manager)
                        => manager.TryGetSubscription(1u, out object subscription);
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0031SubscriptionManagerWrapperAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diagnostics.Any(d => d.Id == "UA0031"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public object Republish(object context, uint id, uint sequenceNumber) => null;
                }

                class C
                {
                    static object M(Unrelated u) => u.Republish(null, 1u, 2u);
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0031SubscriptionManagerWrapperAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0031"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0031SubscriptionManagerWrapperAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0031");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0031 to fire.");
            return diagnostic!;
        }
    }
}
