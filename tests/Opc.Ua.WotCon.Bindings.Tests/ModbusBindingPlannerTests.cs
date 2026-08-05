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
