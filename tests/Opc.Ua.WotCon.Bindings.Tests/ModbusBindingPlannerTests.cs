// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ModbusBindingPlanner"/> validation and compilation logic.
    /// </summary>
    [TestFixture]
    public sealed class ModbusBindingPlannerTests
    {
        private static WotProtocolBinderRegistry Registry()
        {
            return new WotProtocolBinderRegistry([new ModbusBindingPlanner()]);
        }

        private static WotBindingPlan PrepareForm(string formContent, string affordanceName = "p")
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"properties\":{\"" + affordanceName + "\":{\"type\":\"number\"," +
                "\"forms\":[{" + formContent + "}]}}}";
            return Registry().Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
        }

        [Test]
        public void PlannerCompilesMinimaValidForm()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
        }

        /// <summary>
        /// A document cannot omit the Modbus host and rely on an implicit loopback device.
        /// </summary>
        [TestCase("modbus+tcp:///1")]
        [TestCase("modbus:///1")]
        [Category("SecurityRegression")]
        public void SecurityF45HostlessModbusDocumentIsNotPlanned(string href)
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"" +
                href +
                "\"," +
                "\"modv:entity\":\"holdingRegister\",\"modv:address\":100," +
                "\"op\":[\"readproperty\",\"writeproperty\",\"observeproperty\"]");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
            Assert.That(
                plan.Diagnostics.Any(diagnostic => diagnostic.Code == WotBindingDiagnosticCode.InvalidHref),
                Is.True);
        }

        [Test]
        public void PlannerRejectsInvalidScheme()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"http://127.0.0.1:80/p\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerRejectsAddressAboveMax()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":65536,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void PlannerRejectsNegativeAddress()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":-1,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerRejectsAddressPlusQuantityBeyond16Bit()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":65500,\"modv:quantity\":40,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        [Test]
        public void PlannerAcceptsAddressPlusQuantityAtExactBoundary()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":65535,\"modv:quantity\":1,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerRejectsInvalidEntityName()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"invalidEntity\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void PlannerRejectsFormWithNeitherEntityNorFunction()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void PlannerRejectsEntityFunctionEntityMismatch()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"coil\",\"modv:function\":3," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [Test]
        public void PlannerRejectsWriteFunctionWithReadOperation()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":6," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerRejectsReadFunctionWithWriteOperation()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":3," +
                "\"modv:address\":0,\"op\":\"writeproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerDropsWriteOnReadOnlyEntityButPreservesRead()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"discreteInput\"," +
                "\"modv:address\":0");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.False);
        }

        [Test]
        public void PlannerDropsWriteOnInputRegisterButPreservesRead()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"inputRegister\"," +
                "\"modv:address\":0");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.False);
        }

        [Test]
        public void PlannerRejectsQuantityBelowOne()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:quantity\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void PlannerRejectsRegisterQuantityAboveMax()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:quantity\":126,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        [Test]
        public void PlannerAcceptsCoilQuantityUpToMaxCoil()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"coil\"," +
                "\"modv:address\":0,\"modv:quantity\":2000,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
        }

        [Test]
        public void PlannerRejectsInvalidUnitId()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:unitID\":256,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void PlannerAcceptsZeroUnitId()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:unitID\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerAcceptsFunctionCode1WithReadOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":1," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
        }

        [Test]
        public void PlannerAcceptsFunctionCode2WithReadOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":2," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
        }

        [Test]
        public void PlannerAcceptsFunctionCode4WithReadOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":4," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
        }

        [Test]
        public void PlannerAcceptsFunctionCode5WithWriteOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":5," +
                "\"modv:address\":0,\"op\":\"writeproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.True);
        }

        [Test]
        public void PlannerAcceptsFunctionCode15WithWriteOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":15," +
                "\"modv:address\":0,\"op\":\"writeproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.True);
        }

        [Test]
        public void PlannerAcceptsFunctionCode16WithWriteOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":16," +
                "\"modv:address\":0,\"op\":\"writeproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.True);
        }

        [Test]
        public void PlannerRejectsUnknownFunctionCode()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":99," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void PlannerAcceptsModbusSchemeAltForm()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus://127.0.0.1:502/1\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
        }

        [Test]
        public void PlannerExtractsUnitIdFromHrefPath()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502/5\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms[0].Addressing.Target, Does.Contain("@5"));
        }

        [Test]
        public void PlannerEncodesAddressingTargetCorrectly()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":10,\"modv:quantity\":3," +
                "\"modv:unitID\":1,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            string target = plan.CompiledForms[0].Addressing.Target;
            Assert.That(target, Does.Contain("holdingRegister"));
            Assert.That(target, Does.Contain(":10:"));
            Assert.That(target, Does.Contain(":3@1"));
        }

        [Test]
        public void PlannerStoresDataTypeInPayloadMetadata()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"modv:type\":\"float32\",\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(
                plan.CompiledForms[0].Payload.Metadata.TryGetValue("type", out string? t) && t == "float32",
                Is.True);
        }

        [Test]
        public void PlannerHandlesObservePropertyOp()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"observeproperty\"");

            Assert.That(plan.CompiledForms.Any(f =>
                f.Operation == WoTBindingCapabilityEnum.ObserveProperty), Is.True);
        }

        [Test]
        public void PlannerRejectsInvalidHref()
        {
            WotBindingPlan plan = PrepareForm(
                "\"href\":\"not-a-uri\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }
    }
}
