/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Additional tests for <see cref="WotProtocolBinderBase"/> helpers:
    /// teardown-op suppression in <c>ResolveOperations</c>, capability deduplication,
    /// unsupported-op warning, synthetic endpoint from <c>MakeEndpointOrSynthetic</c>,
    /// unknown security-scheme warning from <c>ResolveSecurity</c>, and
    /// href-too-long rejection from <c>RequireHref</c>.
    /// </summary>
    [TestFixture]
    public sealed class WotProtocolBinderBaseAdditionalTests
    {
        private static WotBindingPlanContext DefaultContext()
        {
            return new WotBindingPlanContext();
        }

        private static WotAffordanceForm MakePropertyForm(
            string formJson,
            ImmutableArray<string> ops = default,
            ImmutableArray<string> securitySchemes = default)
        {
            using var formDoc = JsonDocument.Parse(formJson);
            JsonElement formElement = formDoc.RootElement.Clone();

            string? href = null;
            if (formElement.ValueKind == JsonValueKind.Object &&
                formElement.TryGetProperty("href", out JsonElement hrefEl) &&
                hrefEl.ValueKind == JsonValueKind.String)
            {
                href = hrefEl.GetString();
            }

            ImmutableArray<string> operations = ops.IsDefault
                ? (ImmutableArray<string>)["readproperty", "writeproperty"]
                : ops;

            return new WotAffordanceForm(
                WotAffordanceKind.Property,
                "p",
                operations,
                href,
                null,
                null,
                securitySchemes.IsDefault ? [] : securitySchemes,
                "/properties/p/forms/0",
                formElement,
                default);
        }

        private static WotAffordanceForm MakeEventForm(
            string formJson,
            ImmutableArray<string> securitySchemes = default)
        {
            using var formDoc = JsonDocument.Parse(formJson);
            JsonElement formElement = formDoc.RootElement.Clone();

            string? href = null;
            if (formElement.ValueKind == JsonValueKind.Object &&
                formElement.TryGetProperty("href", out JsonElement hrefEl) &&
                hrefEl.ValueKind == JsonValueKind.String)
            {
                href = hrefEl.GetString();
            }

            return new WotAffordanceForm(
                WotAffordanceKind.Event,
                "ev",
                ["subscribeevent", "unsubscribeevent"],
                href,
                null,
                null,
                securitySchemes.IsDefault ? [] : securitySchemes,
                "/events/ev/forms/0",
                formElement,
                default);
        }

        [Test]
        public void ResolveOperationsDropsUnsubscribeEventTeardownOpForMqttPlanner()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakeEventForm(
                """{"href":"mqtt://broker.example.com/events/temp"}""");

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Has.Length.EqualTo(1));
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.SubscribeEvent));
            Assert.That(result.Entries.Any(e => e.OpToken == "unsubscribeevent"), Is.False);
        }

        [Test]
        public void ResolveOperationsDropsUnobservePropertyTeardownOpForMqttPlanner()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com/sensors/temp"}""",
                ops: ["observeproperty", "unobserveproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Has.Length.EqualTo(1));
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ObserveProperty));
            Assert.That(result.Entries.Any(e => e.OpToken == "unobserveproperty"), Is.False);
        }

        [Test]
        public void ResolveOperationsDeduplicatesDuplicateCapabilityForHttpPlanner()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://example.com/p"}""",
                ops: ["readproperty", "readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Has.Length.EqualTo(1));
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ReadProperty));
        }

        [Test]
        public void ResolveOperationsEmitsUnsupportedOpWarningForProfinetWithObserveProperty()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":0,"pnv:subslot":0,"pnv:index":0}""",
                ops: ["readproperty", "observeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Has.Length.EqualTo(1));
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnsupportedOperation &&
                d.Severity == WotDiagnosticSeverity.Warning), Is.True);
        }

        [Test]
        public void ResolvesSecurityEmitsWarningForUnknownSchemeInHttpPlanner()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://example.com/p","op":"readproperty"}""",
                ops: ["readproperty"],
                securitySchemes: ["myCustomScheme"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnknownSecurityScheme &&
                d.Severity == WotDiagnosticSeverity.Warning), Is.True);
        }

        [Test]
        public void ResolvesSecurityEmitsWarningForUnknownSchemeInMqttPlanner()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com/sensors/temp","op":"readproperty"}""",
                ops: ["readproperty"],
                securitySchemes: ["bearer_sc"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnknownSecurityScheme &&
                d.Severity == WotDiagnosticSeverity.Warning), Is.True);
        }

        [Test]
        public void ResolvesSecurityNosecSchemeDoesNotEmitWarning()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://example.com/p","op":"readproperty"}""",
                ops: ["readproperty"],
                securitySchemes: ["nosec_sc"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnknownSecurityScheme), Is.False);
        }

        [Test]
        public void RequireHrefRejectsHrefExceedingMaxUriLength()
        {
            var planner = new HttpBindingPlanner();
            string longHref = "http://example.com/" + new string('a', 2048);
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"" + longHref + "\",\"op\":\"readproperty\"}",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
        }
    }
}
