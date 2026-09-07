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
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>
    /// Exercises the eight shipped planner / validator binders (HTTP, CoAP, MQTT,
    /// Modbus TCP, BACnet, PROFINET, LoRaWAN and OPC UA) across positive,
    /// negative and bounds cases, verifying href / vocabulary validation, op
    /// compatibility, required fields, immutable metadata and JSON-Pointer
    /// diagnostics.
    /// </summary>
    [TestFixture]
    public sealed class WotPlannerTests
    {
        [Test]
        public void HttpValidPropertyCompilesReadAndWrite()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"https://d.example.com/temp\",\"contentType\":\"application/json\"}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Entries.Select(e => e.Operation),
                Is.EquivalentTo(
                [
                    WoTBindingCapabilityEnum.ReadProperty,
                    WoTBindingCapabilityEnum.WriteProperty
                ]));
            WotCompiledForm read = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Endpoint.Scheme, Is.EqualTo("https"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("GET"));
            Assert.That(result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty)
                .OperationInfo.Method, Is.EqualTo("PUT"));
        }

        [Test]
        public void HttpMethodOverrideIsHonoured()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Action(
                    "run",
                    /*lang=json,strict*/
                    "{\"href\":\"http://d/run\",\"htv:methodName\":\"POST\"}"),
                "run");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].OperationInfo.Method, Is.EqualTo("POST"));
        }

        [Test]
        public void HttpInvalidMethodIsRejectedWithPointer()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"http://d/x\",\"htv:methodName\":\"FETCHY\"}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            WotBindingDiagnostic error = result.Diagnostics.First(d => d.IsError);
            Assert.That(error.Code, Is.EqualTo(WotBindingDiagnosticCode.InvalidFieldValue));
            Assert.That(error.JsonPointer, Does.Contain("htv:methodName"));
        }

        [Test]
        public void HttpMissingSchemeIsRejected()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", /*lang=json,strict*/ "{\"href\":\"relative/path\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidHref), Is.True);
        }

        [Test]
        public void MqttValidPropertyResolvesTopicAndQos()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"mqtt://broker:1883/things/temp\",\"mqv:qos\":1,\"mqv:retain\":true}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm write = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty);
            Assert.That(write.Addressing.Target, Is.EqualTo("things/temp"));
            Assert.That(write.Addressing.Metadata["qos"], Is.EqualTo("1"));
            Assert.That(write.Addressing.Metadata["retain"], Is.EqualTo("true"));
            Assert.That(write.OperationInfo.Method, Is.EqualTo("publish"));
        }

        [Test]
        public void MqttInvalidQosIsRejected()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"mqtt://b:1883/t\",\"mqv:qos\":5}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidFieldValue &&
                d.Term == "mqv:qos"), Is.True);
        }

        [Test]
        public void ModbusHoldingRegisterInt32Compiles()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("level",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                    "\"modv:address\":100,\"modv:quantity\":2,\"modv:type\":\"int32\"}"),
                "level");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm read = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Addressing.Metadata["entity"], Is.EqualTo("holdingRegister"));
            Assert.That(read.Addressing.Metadata["address"], Is.EqualTo("100"));
            Assert.That(read.Addressing.Metadata["quantity"], Is.EqualTo("2"));
            Assert.That(read.Addressing.Metadata["unitId"], Is.EqualTo("1"));
            Assert.That(read.Payload.Metadata["type"], Is.EqualTo("int32"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("readHoldingRegisters"));
        }

        [TestCase(3, "uint64")]
        [TestCase(2, null)]
        public void ModbusRegisterWriteWidthMismatchIsRejected(int quantity, string? type)
        {
            var planner = new ModbusBindingPlanner();
            string typeField = type is null ? string.Empty : ",\"modv:type\":\"" + type + "\"";
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("level",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                    "\"modv:address\":100,\"modv:quantity\":" +
                    quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    typeField +
                    ",\"op\":[\"writeproperty\"]}"),
                "level");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            WotBindingDiagnostic diagnostic = result.Diagnostics.Single(d =>
                d.Code == WotBindingDiagnosticCode.ConflictingFields &&
                d.Term == "modv:quantity");
            Assert.That(diagnostic.Message, Does.Contain("encoded width"));
            Assert.That(diagnostic.Message, Does.Contain($"modv:type '{type ?? "uint16"}'"));
        }

        [TestCase(1, "uint16", "writeSingleHoldingRegister")]
        [TestCase(2, "uint32", "writeMultipleHoldingRegisters")]
        [TestCase(4, "uint64", "writeMultipleHoldingRegisters")]
        public void ModbusRegisterWriteMatchingScalarWidthCompiles(
            int quantity,
            string type,
            string expectedMethod)
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("level",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                    "\"modv:address\":100,\"modv:quantity\":" +
                    quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"modv:type\":\"" +
                    type +
                    "\",\"op\":[\"writeproperty\"]}"),
                "level");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm write = result.Entries.Single();
            Assert.That(write.OperationInfo.Method, Is.EqualTo(expectedMethod));
        }

        [Test]
        public void ModbusQuantityBeyondBoundsIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("bulk",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                    "\"modv:address\":0,\"modv:quantity\":200}"),
                "bulk");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        [Test]
        public void ModbusWriteToReadOnlyEntityIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("sensor",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"inputRegister\"," +
                    "\"modv:address\":0,\"modv:quantity\":1}"),
                "sensor");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            // The read entry compiles; the write entry is rejected as read-only.
            Assert.That(result.Entries.Any(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [Test]
        public void ModbusAddressBeyond16BitIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("far",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                    "\"modv:address\":70000,\"modv:quantity\":1}"),
                "far");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Term == "modv:address"), Is.True);
        }

        [Test]
        public void ModbusAddressPlusQuantityOverflowIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("edge",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"holdingRegister\"," +
                    "\"modv:address\":65530,\"modv:quantity\":10}"),
                "edge");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        [Test]
        public void ModbusFunctionOnlyNumericCodeMapsEntityAndMethod()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("reg",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":3," +
                    "\"modv:address\":10,\"modv:quantity\":2,\"modv:type\":\"int32\"}"),
                "reg");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm read = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Addressing.Metadata["entity"], Is.EqualTo("holdingRegister"));
            Assert.That(read.Addressing.Metadata["functionCode"], Is.EqualTo("3"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("readHoldingRegisters"));
            // A read function drops the default write op with a diagnostic.
            Assert.That(result.Entries.Any(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty), Is.False);
        }

        [Test]
        public void ModbusFunctionOnlyMnemonicMapsCoilWrite()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("relay",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":\"writeSingleCoil\"," +
                    "\"modv:address\":5}"),
                "relay");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm write = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty);
            Assert.That(write.Addressing.Metadata["entity"], Is.EqualTo("coil"));
            Assert.That(write.OperationInfo.Method, Is.EqualTo("writeSingleCoil"));
            // A write function drops the default read op with a diagnostic.
            Assert.That(result.Entries.Any(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty), Is.False);
        }

        [Test]
        public void ModbusMultiBitReadCompilesBooleanArrayPayload()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("inputs",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"discreteInput\"," +
                    "\"modv:address\":5,\"modv:quantity\":9,\"op\":[\"readproperty\"]}"),
                "inputs");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm read = result.Entries.Single();
            Assert.That(read.OperationInfo.Method, Is.EqualTo("readDiscreteInput"));
            Assert.That(read.Payload.CodecId, Is.EqualTo(OctetStreamWotPayloadCodec.Instance.Id));
            Assert.That(read.Payload.Metadata["type"], Is.EqualTo("boolean[]"));
        }

        [Test]
        public void ModbusFunction15QuantityOneKeepsScalarPayload()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("relay",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":15," +
                    "\"modv:address\":5,\"modv:quantity\":1,\"op\":[\"writeproperty\"]}"),
                "relay");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm write = result.Entries.Single();
            Assert.That(write.OperationInfo.Method, Is.EqualTo("writeMultipleCoils"));
            Assert.That(write.Payload.CodecId, Is.EqualTo(OctetStreamWotPayloadCodec.Instance.Id));
            Assert.That(write.Payload.Metadata["type"], Is.EqualTo("boolean"));
        }

        [TestCase(5)]
        [TestCase(6)]
        public void ModbusSingleWriteFunctionRejectsMultipleQuantity(int function)
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("relay",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":" +
                    function.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "," +
                    "\"modv:address\":5,\"modv:quantity\":2,\"op\":[\"writeproperty\"]}"),
                "relay");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            WotBindingDiagnostic diagnostic = result.Diagnostics.Single(d =>
                d.Code == WotBindingDiagnosticCode.ConflictingFields &&
                d.Term == "modv:quantity");
            Assert.That(diagnostic.Message, Does.Contain("requires modv:quantity 1"));
        }

        [TestCase(1, 2000, true)]
        [TestCase(1, 2001, false)]
        [TestCase(15, 1968, true)]
        [TestCase(15, 1969, false)]
        [TestCase(16, 124, false)]
        public void ModbusFunctionProtocolMaximumIsEnforced(int function, int quantity, bool supported)
        {
            var planner = new ModbusBindingPlanner();
            string operation = function is 15 or 16 ? "writeproperty" : "readproperty";
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("bits",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":" +
                    function.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"modv:address\":0,\"modv:quantity\":" +
                    quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"op\":[\"" +
                    operation +
                    "\"]}"),
                "bits");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.EqualTo(supported));
            if (!supported)
            {
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.BoundsExceeded),
                    Is.True);
            }
        }

        [Test]
        public void ModbusEntityFunctionMismatchIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("bad",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:entity\":\"coil\"," +
                    "\"modv:function\":\"readHoldingRegisters\",\"modv:address\":0}"),
                "bad");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.IsError &&
                d.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [Test]
        public void ModbusInvalidFunctionIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "weird",
                    /*lang=json,strict*/
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":99,\"modv:address\":0}"),
                "weird");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidFieldValue &&
                d.Term == "modv:function"), Is.True);
        }

        [Test]
        public void ModbusExplicitWriteOpWithReadFunctionIsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("ro",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":\"readCoil\"," +
                    "\"modv:address\":0,\"op\":[\"writeproperty\"]}"),
                "ro");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            // The only op is a write against a read function; every entry is dropped.
            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.ConflictingFields), Is.True);
        }

        [Test]
        public void MqttsSchemeCompilesWithSecureEndpoint()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"mqtts://broker:8883/things/temp\",\"mqv:qos\":1}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm write = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty);
            Assert.That(write.Endpoint.Scheme, Is.EqualTo("mqtts"));
            Assert.That(write.Endpoint.Port, Is.EqualTo(8883));
        }

        [Test]
        public void CoapValidFormCompilesButNonExecutable()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"coap://d/temp\",\"cov:method\":\"GET\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False,
                "The CoAP planner declares its forms non-executable (Capability.IsExecutable == false).");
        }

        [Test]
        public void CoapInvalidMethodIsRejected()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"href\":\"coap://d/x\",\"cov:method\":\"NOPE\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Term == "cov:method"), Is.True);
        }

        [Test]
        public void BacnetValidObjectCompiles()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t",
                    "{\"bacv:objectType\":\"analogInput\",\"bacv:instanceNumber\":1," +
                    "\"bacv:propertyIdentifier\":\"presentValue\"}"),
                "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("analogInput:1:presentValue"));
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False);
        }

        [Test]
        public void BacnetMissingInstanceIsRejected()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "t",
                    /*lang=json,strict*/
                    "{\"bacv:objectType\":\"analogInput\",\"bacv:propertyIdentifier\":\"presentValue\"}"),
                "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.MissingRequiredField &&
                d.Term == "bacv:instanceNumber"), Is.True);
        }

        [Test]
        public void ProfinetValidSlotCompiles()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "t",
                    /*lang=json,strict*/
                    "{\"pnv:slot\":1,\"pnv:subslot\":2,\"pnv:index\":100}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False);
        }

        [Test]
        public void ProfinetMissingIndexIsRejected()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", /*lang=json,strict*/ "{\"pnv:slot\":1,\"pnv:subslot\":2}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Term == "pnv:index"), Is.True);
        }

        [Test]
        public void LoRaWanValidDeviceCompiles()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "t",
                    /*lang=json,strict*/
                    "{\"lorawan:DevEUI\":\"0011223344556677\",\"lorawan:fPort\":10}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["devEui"], Is.EqualTo("0011223344556677"));
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False);
        }

        [Test]
        public void LoRaWanInvalidDevEuiIsRejected()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", /*lang=json,strict*/ "{\"lorawan:DevEUI\":\"not-hex\"}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void OpcUaValidNodeIdCompiles()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "t",
                    /*lang=json,strict*/
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=5\"}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm read = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Addressing.Target, Is.EqualTo("ns=2;i=5"));
            Assert.That(read.Endpoint.Scheme, Is.EqualTo("opc.tcp"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("Read"));
        }

        [Test]
        public void OpcUaMissingNodeIdIsRejected()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", /*lang=json,strict*/ "{\"href\":\"opc.tcp://server:4840\"}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void OpcUaActionCarriesComponentOf()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Action(
                    "run",
                    /*lang=json,strict*/
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=9\",\"uav:componentOf\":\"ns=2;i=1\"}"),
                "run");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm invoke = result.Entries[0];
            Assert.That(invoke.Operation, Is.EqualTo(WoTBindingCapabilityEnum.InvokeAction));
            Assert.That(invoke.Addressing.Metadata["componentOf"], Is.EqualTo("ns=2;i=1"));
            Assert.That(invoke.OperationInfo.Method, Is.EqualTo("Call"));
        }

        [Test]
        public void OpcUaEventCarriesAuthoredEventFields()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Event("alarmActive",
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=42\"," +
                    "\"uav:eventFields\":[\"ActiveState/Id\",\"Severity\"],\"op\":[\"subscribeevent\"]}"),
                "alarmActive");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm subscribe = result.Entries.First(
                e => e.Operation == WoTBindingCapabilityEnum.SubscribeEvent);
            WotEventSelection selection = subscribe.EventSelection!;
            Assert.Multiple(() =>
            {
                Assert.That(selection.Origin, Is.EqualTo(WotEventSelectionOrigin.Legacy));
                Assert.That(
                    selection.Clauses.ToList().Select(c => c.BrowsePath),
                    Does.Contain("ActiveState/Id"),
                    "The superseded spelling adds to the implicit BaseEventType default.");
                Assert.That(
                    selection.Clauses.ToList().Count(c => c.BrowsePath == "Severity"),
                    Is.EqualTo(1),
                    "A field the default already selects is not selected twice.");
            });
        }

        [Test]
        public void OpcUaEventWithoutEventFieldsCompilesTheDocumentedDefault()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Event(
                    "trigger",
                    /*lang=json,strict*/
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=42\",\"op\":[\"subscribeevent\"]}"),
                "trigger");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm subscribe = result.Entries.First(
                e => e.Operation == WoTBindingCapabilityEnum.SubscribeEvent);
            Assert.Multiple(() =>
            {
                Assert.That(
                    subscribe.EventSelection!.Origin,
                    Is.EqualTo(WotEventSelectionOrigin.Default));
                Assert.That(subscribe.Addressing.Metadata.ContainsKey("eventFields"), Is.False);
            });
        }

        [Test]
        public void CapabilitiesPinSourcesAndNeverClaimRegistryCurrent()
        {
            foreach (IWotProtocolBinder binder in WotBuiltInBinders.CreateAll())
            {
                Assert.That(binder.Capability.Source.SpecificationUri, Is.Not.Empty,
                    $"{binder.Identity.Id} must pin a source URL.");
                Assert.That(binder.Capability.Source.Maturity, Is.Not.EqualTo(WotBindingMaturity.RegistryCurrent),
                    $"{binder.Identity.Id} must not claim W3C Registry Current status.");
                WoTBindingCapabilityDataType dataType = binder.Capability.ToDataType();
                Assert.That(dataType.BindingUri, Is.Not.Empty);
                Assert.That(dataType.DraftMaturity, Is.Not.Empty);
            }
        }

        [Test]
        public void DiagnosticsExposeJsonPointerViaSharedModel()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property(
                    "temp",
                    /*lang=json,strict*/
                    "{\"contentType\":\"application/json\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());
            WotBindingDiagnostic diagnostic = result.Diagnostics.First(d => d.IsError);
            var shared = diagnostic.ToWotDiagnostic();

            Assert.That(shared.Location?.JsonPointer, Does.StartWith("/properties/temp/forms/0"));
        }
    }
}
