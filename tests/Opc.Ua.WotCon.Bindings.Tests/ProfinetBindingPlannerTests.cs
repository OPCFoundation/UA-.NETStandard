/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ProfinetBindingPlanner"/> validation and compilation logic.
    /// </summary>
    [TestFixture]
    public sealed class ProfinetBindingPlannerTests
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

        [Test]
        public void ProfinetPlannerIdentityIdIsCorrect()
        {
            var planner = new ProfinetBindingPlanner();

            Assert.That(planner.Identity.Id, Is.EqualTo("w3c.profinet"));
        }

        [Test]
        public void ProfinetPlannerVersionIsCorrect()
        {
            var planner = new ProfinetBindingPlanner();

            Assert.That(planner.Identity.Version, Is.EqualTo("0.1-draft"));
        }

        [Test]
        public void ProfinetPlannerIsNotExecutable()
        {
            var planner = new ProfinetBindingPlanner();

            Assert.That(planner.Capability.IsExecutable, Is.False);
        }

        [Test]
        public void ProfinetPlannerSupportsOnlyReadAndWriteProperty()
        {
            var planner = new ProfinetBindingPlanner();

            Assert.That(planner.Capability.Supports(WoTBindingCapabilityEnum.ReadProperty), Is.True);
            Assert.That(planner.Capability.Supports(WoTBindingCapabilityEnum.WriteProperty), Is.True);
            Assert.That(planner.Capability.Supports(WoTBindingCapabilityEnum.ObserveProperty), Is.False);
        }

        [Test]
        public void ProfinetPlannerMatchesFormWithPnvVocabularyPrefix()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":0,"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingMatch match = planner.Match(form, WotBindingSelectionContext.Empty);

            Assert.That(match.IsMatch, Is.True);
        }

        [Test]
        public void ProfinetPlannerCompilesValidFormWithAllRequiredFields()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":2,"pnv:index":3,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Is.Not.Empty);
        }

        [Test]
        public void ProfinetPlannerAddressMetadataContainsSlotSubslotIndex()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":4,"pnv:subslot":5,"pnv:index":6,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            WotAddressingDescriptor addressing = result.Entries[0].Addressing;
            Assert.That(addressing.Metadata.ContainsKey("slot"), Is.True);
            Assert.That(addressing.Metadata["slot"], Is.EqualTo("4"));
            Assert.That(addressing.Metadata.ContainsKey("subslot"), Is.True);
            Assert.That(addressing.Metadata["subslot"], Is.EqualTo("5"));
            Assert.That(addressing.Metadata.ContainsKey("index"), Is.True);
            Assert.That(addressing.Metadata["index"], Is.EqualTo("6"));
        }

        [Test]
        public void ProfinetPlannerCompilesFormWithOptionalApiField()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":0,"pnv:index":0,"pnv:api":42,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            WotAddressingDescriptor addressing = result.Entries[0].Addressing;
            Assert.That(addressing.Metadata.ContainsKey("api"), Is.True);
            Assert.That(addressing.Metadata["api"], Is.EqualTo("42"));
        }

        [Test]
        public void ProfinetPlannerCompilesSyntheticEndpointWhenHrefIsAbsent()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            WotEndpointDescriptor endpoint = result.Entries[0].Endpoint;
            Assert.That(endpoint.Scheme, Is.EqualTo("profinet"));
            Assert.That(endpoint.Host, Is.Null);
            Assert.That(endpoint.Port, Is.EqualTo(-1));
        }

        [Test]
        public void ProfinetPlannerCompilesFormWithHrefAndParsesEndpoint()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"profinet://plc.example.com:102","pnv:slot":1,"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            WotEndpointDescriptor endpoint = result.Entries[0].Endpoint;
            Assert.That(endpoint.Scheme, Is.EqualTo("profinet"));
            Assert.That(endpoint.Host, Is.EqualTo("plc.example.com"));
            Assert.That(endpoint.Port, Is.EqualTo(102));
        }

        [Test]
        public void ProfinetPlannerCompilesReadPropertyOperation()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ReadProperty));
        }

        [Test]
        public void ProfinetPlannerCompilesWritePropertyOperation()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":0,"pnv:index":0,"op":"writeproperty"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.WriteProperty));
        }

        [Test]
        public void ProfinetPlannerRejectsObservePropertyAsUnsupported()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":0,"pnv:index":0,"op":"observeproperty"}""",
                ops: ["observeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnsupportedOperation), Is.True);
        }

        [Test]
        public void ProfinetPlannerRejectsMissingSlot()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void ProfinetPlannerRejectsMissingSubslot()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
        }

        [Test]
        public void ProfinetPlannerRejectsMissingIndex()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
        }

        [Test]
        public void ProfinetPlannerRejectsNegativeSlotValue()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":-1,"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
        }

        [Test]
        public void ProfinetPlannerRejectsAllMissingRequiredFields()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"profinet://plc.example.com","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Diagnostics.Count(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.EqualTo(3));
        }

        [Test]
        public void ProfinetPlannerAcceptsZeroSlotSubslotAndIndex()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":0,"pnv:subslot":0,"pnv:index":0,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void ProfinetPlannerCompilesBothReadAndWriteWhenBothOpsPresent()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"pnv:slot":1,"pnv:subslot":2,"pnv:index":3}""",
                ops: ["readproperty", "writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Has.Length.EqualTo(2));
        }
    }
}
