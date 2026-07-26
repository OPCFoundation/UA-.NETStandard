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
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Additional unit tests for <see cref="ModbusBindingPlanner"/>: integer function codes,
    /// discrete-input and input-register entity compilations, read/write function-direction
    /// conflicts, write-multiple-coils, and unit-id resolution from the href path.
    /// </summary>
    [TestFixture]
    public sealed class ModbusBindingPlannerAdditionalTests
    {
        private static WotProtocolBinderRegistry Registry()
        {
            return new WotProtocolBinderRegistry([new ModbusBindingPlanner()]);
        }

        private static WotBindingPlan PreparePropertyForm(string formContent, string affordanceName = "p")
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"properties\":{\"" + affordanceName + "\":{\"type\":\"number\"," +
                "\"forms\":[{" + formContent + "}]}}}";

            return Registry().Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
        }

        [Test]
        public void ModbusPlannerCompilesWithIntegerFunctionCodeField()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":3," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms[0].Addressing.Metadata.ContainsKey("function"), Is.True);
            Assert.That(plan.CompiledForms[0].Addressing.Metadata["function"], Is.EqualTo("readHoldingRegisters"));
        }

        [Test]
        public void ModbusPlannerCompilesDiscreteInputEntityForReadProperty()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"discreteInput\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms[0].OperationInfo.Method, Is.EqualTo("readDiscreteInput"));
        }

        [Test]
        public void ModbusPlannerCompilesInputRegisterEntityForReadProperty()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"inputRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms[0].OperationInfo.Method, Is.EqualTo("readInputRegister"));
        }

        [Test]
        public void ModbusPlannerDropsWriteOperationForReadOnlyEntityAndPreservesRead()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"discreteInput\"," +
                "\"modv:address\":0");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms.All(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.ConflictingFields &&
                d.Severity == WotDiagnosticSeverity.Warning), Is.True);
        }

        [Test]
        public void ModbusPlannerDropsReadOperationWhenFunctionIsWriteDirected()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":\"writeSingleHoldingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [Test]
        public void ModbusPlannerDropsWriteOperationWhenFunctionIsReadDirected()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:function\":\"readHoldingRegisters\"," +
                "\"modv:address\":0,\"op\":\"writeproperty\"");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [Test]
        public void ModbusPlannerCompilesWriteMultipleCoilsForCoilEntityWithQuantityGreaterThanOne()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502\"," +
                "\"modv:entity\":\"coil\"," +
                "\"modv:address\":0,\"modv:quantity\":2,\"op\":\"writeproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms[0].OperationInfo.Method, Is.EqualTo("writeMultipleCoils"));
        }

        [Test]
        public void ModbusPlannerParsesUnitIdFromHrefPath()
        {
            WotBindingPlan plan = PreparePropertyForm(
                "\"href\":\"modbus+tcp://127.0.0.1:502/3\"," +
                "\"modv:entity\":\"holdingRegister\"," +
                "\"modv:address\":0,\"op\":\"readproperty\"");

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms[0].Addressing.Metadata["unitId"], Is.EqualTo("3"));
        }
    }
}
