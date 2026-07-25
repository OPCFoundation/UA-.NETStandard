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

using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>
    /// Exercises the OPC 10101 §6.5.4 <c>uav:mapToNodeId</c> / <c>uav:mapToType</c>
    /// / <c>uav:mapByFieldPath</c> target-mapping terms: they are protocol-neutral
    /// (a non-OPC-UA source may be mapped onto an OPC UA target), valid only on
    /// property affordances, <c>mapByFieldPath</c> requires <c>mapToType</c>, the
    /// terms must never be authored on a form, and the resulting
    /// <see cref="WotTargetMappingDescriptor"/> is carried by every compiled entry.
    /// Validation is centralized in <see cref="WotProtocolBinderRegistry.Prepare"/>
    /// so it applies identically to every protocol.
    /// </summary>
    [TestFixture]
    public sealed class WotTargetMappingTests
    {
        private static WotBindingPlan Prepare(string document)
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            var request = WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document));
            return registry.Prepare(request);
        }

        [Test]
        public void MapToNodeIdOnModbusPropertyPropagatesToCompiledEntries()
        {
            string document = WotBindingTestSupport.Property("sensor",
                "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:quantity\":1}",
                "\"uav:mapToNodeId\":\"ns=2;s=Sensor1\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms.All(f => f.TargetMapping.TargetNodeId == "ns=2;s=Sensor1"), Is.True);
        }

        [Test]
        public void MapToNodeIdOnHttpPropertyIsAccepted()
        {
            string document = WotBindingTestSupport.Property(
                "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d.example.com/temp\",\"contentType\":\"application/json\"}",
                "\"uav:mapToNodeId\":\"ns=2;s=Temp\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms.All(f => f.TargetMapping.TargetNodeId == "ns=2;s=Temp"), Is.True);
        }

        [Test]
        public void MapToNodeIdOnOpcUaPropertyIsAccepted()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=5\"}",
                "\"uav:mapToNodeId\":\"ns=3;s=Mapped\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms.All(f => f.TargetMapping.TargetNodeId == "ns=3;s=Mapped"), Is.True);
        }

        [Test]
        public void MapToTypeWithFieldPathOnPropertyIsAccepted()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapToType\":\"ns=2;i=100\",\"uav:mapByFieldPath\":\"Value/SubField\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.Diagnostics.Any(d => d.IsError), Is.False);
            WotCompiledForm entry = plan.CompiledForms[0];
            Assert.That(entry.TargetMapping.TargetTypeNodeId, Is.EqualTo("ns=2;i=100"));
            Assert.That(entry.TargetMapping.FieldPath, Is.EqualTo("Value/SubField"));
        }

        [Test]
        public void MapByFieldPathWithoutMapToTypeIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapByFieldPath\":\"Value/SubField\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingFieldPathRequiresType),
                Is.True);
        }

        [Test]
        public void MapToNodeIdAndMapToTypeTogetherAreAccepted()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapToNodeId\":\"ns=2;s=Target\",\"uav:mapToType\":\"ns=2;i=100\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms[0].TargetMapping.TargetNodeId, Is.EqualTo("ns=2;s=Target"));
            Assert.That(plan.CompiledForms[0].TargetMapping.TargetTypeNodeId, Is.EqualTo("ns=2;i=100"));
        }

        [Test]
        public void MapToNodeIdOnActionIsRejected()
        {
            string document = WotBindingTestSupport.Action(
                "run",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/run\",\"htv:methodName\":\"POST\"}",
                "\"uav:mapToNodeId\":\"ns=2;s=X\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingNotOnProperty),
                Is.True);
        }

        [Test]
        public void MapToNodeIdOnEventIsRejected()
        {
            string document = WotBindingTestSupport.Event(
                "alarm",
                    /*lang=json,strict*/
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=42\",\"op\":[\"subscribeevent\"]}",
                "\"uav:mapToNodeId\":\"ns=2;s=X\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingNotOnProperty),
                Is.True);
        }

        [Test]
        public void MapByFieldPathOnActionReportsOnlyPropertyAffordanceError()
        {
            string document = WotBindingTestSupport.Action(
                "run",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/run\",\"htv:methodName\":\"POST\"}",
                "\"uav:mapByFieldPath\":\"Value\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(
                plan.Diagnostics.Count(d => d.Code == WotBindingDiagnosticCode.TargetMappingNotOnProperty),
                Is.EqualTo(1));
            Assert.That(
                plan.Diagnostics.Any(d =>
                    d.Code == WotBindingDiagnosticCode.TargetMappingFieldPathRequiresType),
                Is.False);
            Assert.That(
                plan.Diagnostics.Single(d =>
                    d.Code == WotBindingDiagnosticCode.TargetMappingNotOnProperty).JsonPointer,
                Is.EqualTo("/actions/run/uav:mapByFieldPath"));
        }

        [Test]
        public void MapToNodeIdAuthoredOnFormIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\",\"uav:mapToNodeId\":\"ns=2;s=X\"}");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingAuthoredOnForm),
                Is.True);
        }

        [Test]
        public void MapToTypeAuthoredOnFormIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\",\"uav:mapToType\":\"ns=2;i=1\"}");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingAuthoredOnForm),
                Is.True);
        }

        [Test]
        public void NonStringMapToNodeIdOnPropertyIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapToNodeId\":42");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingInvalidValue),
                Is.True);
        }

        [Test]
        public void NonStringMapToNodeIdOnFormIsRejectedAsFormMisuse()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\",\"uav:mapToNodeId\":42}");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingAuthoredOnForm),
                Is.True);
        }

        [Test]
        public void MapToNodeIdEmptyIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapToNodeId\":\"\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingEmptyValue),
                Is.True);
        }

        [Test]
        public void MapToTypeEmptyIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapToType\":\"\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingEmptyValue),
                Is.True);
        }

        [Test]
        public void MapByFieldPathEmptyIsRejected()
        {
            string document = WotBindingTestSupport.Property(
                "t",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d/x\",\"contentType\":\"application/json\"}",
                "\"uav:mapToType\":\"ns=2;i=1\",\"uav:mapByFieldPath\":\"\"");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(
                plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.TargetMappingEmptyValue),
                Is.True);
        }

        [Test]
        public void MapToNodeIdPropagatesToEveryCompiledOperationEntry()
        {
            string document = WotBindingTestSupport.Property("sensor",
                "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:quantity\":1}",
                "\"uav:mapToNodeId\":\"ns=2;s=Sensor1\"");

            WotBindingPlan plan = Prepare(document);

            // A holding register defaults to both read and write property ops.
            Assert.That(plan.CompiledForms, Has.Length.GreaterThan(1));
            Assert.That(plan.CompiledForms.All(f => f.TargetMapping.TargetNodeId == "ns=2;s=Sensor1"), Is.True);
        }

        [Test]
        public void NoMappingTermsAuthoredCompiledEntriesCarryEmptyTargetMapping()
        {
            string document = WotBindingTestSupport.Property(
                "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d.example.com/temp\",\"contentType\":\"application/json\"}");

            WotBindingPlan plan = Prepare(document);

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms.All(f => f.TargetMapping.IsEmpty), Is.True);
        }

        [Test]
        public void FormExtractorParsesTargetMappingFromAffordanceElement()
        {
            string document = WotBindingTestSupport.Property("t", /*lang=json,strict*/ "{\"href\":\"https://d/x\"}",
                "\"uav:mapToNodeId\":\"ns=2;s=X\",\"uav:mapToType\":\"ns=2;i=10\",\"uav:mapByFieldPath\":\"A/B\"");

            WotAffordanceForm form = WotBindingTestSupport.Form(document, "t");

            Assert.That(form.TargetMapping.TargetNodeId, Is.EqualTo("ns=2;s=X"));
            Assert.That(form.TargetMapping.TargetTypeNodeId, Is.EqualTo("ns=2;i=10"));
            Assert.That(form.TargetMapping.FieldPath, Is.EqualTo("A/B"));
        }

        [Test]
        public void FormExtractorWithNoMappingTermsProducesEmptyTargetMapping()
        {
            string document = WotBindingTestSupport.Property("t", /*lang=json,strict*/ "{\"href\":\"https://d/x\"}");

            WotAffordanceForm form = WotBindingTestSupport.Form(document, "t");

            Assert.That(form.TargetMapping.IsEmpty, Is.True);
        }

        [Test]
        public void CompiledFormWithTargetMappingReturnsNewInstanceWithUpdatedMapping()
        {
            var original = new WotCompiledForm(
                new WotBindingIdentity("test.binder", "1.0", "urn:test", "Test"),
                WotAffordanceKind.Property, "t", "/properties/t/forms/0",
                WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                new WotEndpointDescriptor("stub", "d", -1, "stub://d"),
                new WotAddressingDescriptor("t"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                new WotPayloadDescriptor("application/json", "json"),
                [],
                isExecutable: false);

            Assert.That(original.TargetMapping.IsEmpty, Is.True);

            var mapping = new WotTargetMappingDescriptor(targetNodeId: "ns=2;s=X");
            WotCompiledForm mapped = original.WithTargetMapping(mapping);

            Assert.That(mapped, Is.Not.SameAs(original));
            Assert.That(mapped.TargetMapping.TargetNodeId, Is.EqualTo("ns=2;s=X"));
            Assert.That(original.TargetMapping.IsEmpty, Is.True, "The original entry must stay unchanged.");

            WotCompiledForm same = mapped.WithTargetMapping(mapping);
            Assert.That(same, Is.SameAs(mapped));
        }
    }
}
