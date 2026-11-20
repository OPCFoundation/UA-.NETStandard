// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT projection schema reference.
    /// </summary>
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        /// <summary>
        /// Verifies that enumerated host forms must be authored before any provider is invoked.
        /// </summary>
        /// <param name="selectAll">The select-all flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EnumeratedHostFormsMustBeAuthoredBeforeAnyProviderIsInvoked(bool selectAll)
        {
            JsonObject plan = HostPlan(bulk: false);
            if (selectAll)
            {
                plan["uav:projects"]![0]!["uav:selectAll"] = true;
            }
            JsonObject source = Source();
            string originalPlan = plan.ToJsonString();
            string originalSource = source.ToJsonString();
            int calls = 0;
            var provider = new ProjectionFormProvider((_, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls++;
                return HostForms("https://host.test/runtime/provider-must-not-repair-enumeration");
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, source, new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero, "An invalid enumeration must fail before any host-provider invocation.");
                Assert.That(result.Success, Is.False);
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Location?.Reference == "/properties/reading/forms"), Is.True);
                Assert.That(plan.ToJsonString(), Is.EqualTo(originalPlan));
                Assert.That(source.ToJsonString(), Is.EqualTo(originalSource));
            });
        }
    }
}
