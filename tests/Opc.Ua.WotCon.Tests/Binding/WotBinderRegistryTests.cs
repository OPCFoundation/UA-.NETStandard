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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Samples;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>
    /// Exercises the aggregating <see cref="WotProtocolBinderRegistry"/>:
    /// deterministic selection, version coexistence, executable upgrade when an
    /// executor is present, capability exposure and unsupported classification.
    /// </summary>
    [TestFixture]
    public sealed class WotBinderRegistryTests
    {
        private static WotBindingPlanRequest Request(string affordance, string href, string extraTerms = "")
        {
            string terms = string.IsNullOrEmpty(extraTerms) ? string.Empty : "," + extraTerms;
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"" + affordance + "\":{\"type\":\"number\",\"forms\":[{\"href\":\"" +
                href + "\"" + terms + "}]}}}";
            return WotBindingPlanRequest.FromDocument("xid", WoTDocumentKindEnum.ThingDescription,
                System.Text.Encoding.UTF8.GetBytes(td));
        }

        [Test]
        public void Prepare_SelectsBinderByScheme()
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());

            WotBindingPlan modbus = registry.Prepare(Request("m", "modbus+tcp://plc:502/1",
                "\"modv:entity\":\"holdingRegister\",\"modv:address\":0,\"modv:quantity\":1"));
            WotBindingPlan http = registry.Prepare(Request("h", "https://d/x"));

            Assert.That(modbus.CompiledForms.All(f => f.Binding.Id == "w3c.modbus"), Is.True);
            Assert.That(http.CompiledForms.All(f => f.Binding.Id == "w3c.http"), Is.True);
        }

        [Test]
        public void Prepare_NoBinder_MarksFormUnsupported()
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());

            WotBindingPlan plan = registry.Prepare(Request("x", "ftp://legacy/thing"));

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }

        [Test]
        public void Prepare_PlannerOnly_ProducesNonExecutableForms()
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());

            WotBindingPlan plan = registry.Prepare(Request("t", "coap://d/temp", "\"cov:method\":\"GET\""));

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.HasExecutableForms, Is.False);
            Assert.That(plan.HasNonExecutableForms, Is.True);
        }

        [Test]
        public void Prepare_ModbusReadOnlyEntity_DefaultOps_KeepsReadPlan()
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());

            // An input register carries the default read+write property ops. The
            // write op is not executable against a read-only entity, but it must be
            // dropped with a warning rather than an error: an error would set
            // HasErrors and cause the whole form (read binding included) to be
            // dropped as unsupported.
            WotBindingPlan input = registry.Prepare(Request("sensor", "modbus+tcp://plc:502/1",
                "\"modv:entity\":\"inputRegister\",\"modv:address\":0,\"modv:quantity\":1"));
            WotBindingPlan discrete = registry.Prepare(Request("flag", "modbus+tcp://plc:502/1",
                "\"modv:entity\":\"discreteInput\",\"modv:address\":0,\"modv:quantity\":1"));

            foreach (WotBindingPlan plan in new[] { input, discrete })
            {
                Assert.That(plan.FullySupported, Is.True, "The read-only form must not be dropped as unsupported.");
                Assert.That(
                    plan.CompiledForms.Any(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True,
                    "The read binding must be preserved.");
                Assert.That(
                    plan.CompiledForms.Any(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.False,
                    "The read-only write op must be dropped, not materialized.");
                Assert.That(
                    plan.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.ConflictingFields &&
                        d.Severity == Opc.Ua.Wot.WotDiagnosticSeverity.Warning),
                    Is.True,
                    "The dropped write must be reported as a warning, not an error.");
            }
        }

        [Test]
        public void Prepare_WithExecutor_UpgradesToExecutable()
        {
            var store = new MemoryWotStore();
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new MemoryWotBinder() },
                new IWotBindingExecutor[] { new MemoryWotBindingExecutor(store) });

            WotBindingPlan plan = registry.Prepare(Request("t", "mem://store/key"));

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.HasExecutableForms, Is.True);
            Assert.That(plan.CompiledForms.Any(f => f.IsExecutable), Is.True);
        }

        [Test]
        public void Registry_ExposesOneCapabilityPerBinder()
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());

            IReadOnlyList<WoTBindingCapabilityDataType> capabilities = registry.Capabilities;

            Assert.That(capabilities.Count, Is.EqualTo(8));
            Assert.That(capabilities.Select(c => c.BindingUri),
                Has.Some.EqualTo(HttpBindingPlanner.BindingUri));
            Assert.That(capabilities.Select(c => c.BindingUri),
                Has.Some.EqualTo(OpcUaBindingPlanner.BindingUri));
        }

        [Test]
        public void Registry_MultipleVersionsCoexist()
        {
            var registry = new WotProtocolBinderRegistry(new IWotProtocolBinder[]
            {
                new StubBinder("1.0"),
                new StubBinder("2.0")
            });

            Assert.That(registry.Binders.Count, Is.EqualTo(2));
            Assert.That(registry.Binders.Select(b => b.Identity.Version), Is.EquivalentTo(new[] { "1.0", "2.0" }));
            Assert.That(registry.Capabilities.Count, Is.EqualTo(2));
        }

        [Test]
        public void Registry_ExplicitPin_OverridesSchemeSelection()
        {
            var registry = new WotProtocolBinderRegistry(new IWotProtocolBinder[]
            {
                new HttpBindingPlanner(),
                new StubBinder("1.0")
            });

            // A stub-scheme href with an explicit pin on the stub binder id.
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"t\":{\"forms\":[{\"href\":\"stub://d/x\"}]}}}";
            var selection = new WotBindingSelectionContext(
                System.Collections.Immutable.ImmutableArray.Create("stub.binder"),
                System.Collections.Immutable.ImmutableArray<string>.Empty);
            var request = new WotBindingPlanRequest("xid", WoTDocumentKindEnum.ThingDescription,
                WotFormExtractor.Extract(System.Text.Encoding.UTF8.GetBytes(td)),
                selection: selection);

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.CompiledForms.All(f => f.Binding.Id == "stub.binder"), Is.True);
        }

        /// <summary>A minimal stub binder used for version and selection tests.</summary>
        private sealed class StubBinder : WotProtocolBinderBase
        {
            private static readonly string[] s_schemes = { "stub" };

            public StubBinder(string version)
            {
                Identity = new WotBindingIdentity("stub.binder", version, "urn:stub", "Stub");
                Capability = new WotBindingCapability("urn:stub", "Stub",
                    new WotBindingSource("urn:stub", version, WotBindingMaturity.UnofficialDraft),
                    new[] { WoTBindingCapabilityEnum.ReadProperty },
                    new[] { "application/json" }, isExecutable: false);
            }

            public override WotBindingIdentity Identity { get; }

            public override WotBindingCapability Capability { get; }

            protected override IReadOnlyCollection<string> Schemes => s_schemes;

            public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
                => MatchStandard(form, context, null);

            public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
            {
                var entry = new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer,
                    WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                    new WotEndpointDescriptor("stub", "d", -1, "stub://d"),
                    new WotAddressingDescriptor(form.AffordanceName),
                    new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                    new WotPayloadDescriptor("application/json", "json"),
                    System.Collections.Immutable.ImmutableArray<WotCredentialReference>.Empty,
                    isExecutable: false);
                return WotBindingCompilation.Supported(
                    System.Collections.Immutable.ImmutableArray.Create(entry),
                    System.Collections.Immutable.ImmutableArray<WotBindingDiagnostic>.Empty);
            }
        }
    }
}
