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
    /// Tests for UA0030 (the ISubscription publish-pipeline members and
    /// SessionPublishQueue internalized in 2.0).
    /// </summary>
    /// <remarks>
    /// Each source declares the 1.5.378 shape of the surface it exercises. The members are
    /// gone from the 2.0 assemblies, so the rule has to fire on sources that still compile
    /// against the old surface — which is exactly the migration path it exists for.
    /// </remarks>
    [TestFixture]
    public class UA0030Tests
    {
        private const string SubscriptionShim = """
            namespace Opc.Ua.Server
            {
                public interface ISubscription
                {
                    int PublishTimerExpired();
                    object Acknowledge(object context, uint sequenceNumber);
                    void ItemReadyToPublish(object monitoredItem);
                    object Publish(object context, out uint[] availableSequenceNumbers, out bool moreNotifications);
                    System.Threading.Tasks.ValueTask TransferSessionAsync(object context, bool sendInitialValues);
                }
            }
            """;

        [Test]
        public async Task ReportsOnPublishTimerExpiredAsync()
        {
            string source = SubscriptionShim + """

                class C
                {
                    static int M(Opc.Ua.Server.ISubscription subscription) => subscription.PublishTimerExpired();
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ISubscription.PublishTimerExpired"));
            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Subscription"),
                "the message must say custom subscriptions derive from Subscription.");
        }

        [Test]
        public async Task ReportsDeletedNoOpGuidanceOnItemReadyToPublishAsync()
        {
            string source = SubscriptionShim + """

                class C
                {
                    static void M(Opc.Ua.Server.ISubscription subscription)
                        => subscription.ItemReadyToPublish(null);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("no-op"),
                "the dead members get remove-the-call guidance, not relocation guidance.");
        }

        [Test]
        public async Task ReportsOnPublishAsync()
        {
            string source = SubscriptionShim + """

                class C
                {
                    static object M(Opc.Ua.Server.ISubscription subscription)
                        => subscription.Publish(null, out uint[] a, out bool m);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(diagnostic.Id, Is.EqualTo("UA0030"));
        }

        [Test]
        public async Task ReportsOnTransferSessionAsync()
        {
            string source = SubscriptionShim + """

                class C
                {
                    static System.Threading.Tasks.ValueTask M(Opc.Ua.Server.ISubscription subscription)
                        => subscription.TransferSessionAsync(null, sendInitialValues: false);
                }
                """;

            Diagnostic diagnostic = await SingleAsync(source).ConfigureAwait(false);

            Assert.That(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("TransferSubscriptions"),
                "the message must point at the service that replaces the direct transfer.");
        }

        [Test]
        public async Task ReportsOnSessionPublishQueueTypeUsageAsync()
        {
            const string source = """
                namespace Opc.Ua.Server
                {
                    public class SessionPublishQueue
                    {
                        public void RemoveQueuedRequests() { }
                    }
                }

                class C
                {
                    static void M(Opc.Ua.Server.SessionPublishQueue queue)
                        => queue.RemoveQueuedRequests();
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0030SubscriptionPublishPipelineAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.FirstOrDefault(d => d.Id == "UA0030");
            Assert.That(
                diagnostic,
                Is.Not.Null,
                "a reference to the internalized queue type must fire the rule.");
            Assert.That(
                diagnostic!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("SessionPublishQueue"));
        }

        [Test]
        public async Task DoesNotReportOnAnUnrelatedTypeAsync()
        {
            const string source = """
                class Unrelated
                {
                    public int PublishTimerExpired() => 0;
                }

                class C
                {
                    static int M(Unrelated u) => u.PublishTimerExpired();
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0030SubscriptionPublishPipelineAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0030"),
                Is.False,
                "a member of the same name on an unrelated type must not fire the rule.");
        }

        [Test]
        public async Task DoesNotReportOnRemainingMembersAsync()
        {
            const string source = """
                namespace Opc.Ua.Server
                {
                    public interface ISubscription
                    {
                        void ResendData(object context);
                        void GetMonitoredItems(out uint[] serverHandles, out uint[] clientHandles);
                    }
                }

                class C
                {
                    static void M(Opc.Ua.Server.ISubscription subscription)
                    {
                        subscription.ResendData(null);
                        subscription.GetMonitoredItems(out uint[] s, out uint[] c);
                    }
                }
                """;

            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0030SubscriptionPublishPipelineAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Id == "UA0030"),
                Is.False,
                "the members that stayed on ISubscription must not fire the rule.");
        }

        private static async Task<Diagnostic> SingleAsync(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0030SubscriptionPublishPipelineAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? diagnostic = diagnostics.SingleOrDefault(d => d.Id == "UA0030");
            Assert.That(diagnostic, Is.Not.Null, "expected UA0030 to fire.");
            return diagnostic!;
        }
    }
}
