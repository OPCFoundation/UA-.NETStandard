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
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.V2;

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
        // ---- HTTP -------------------------------------------------------------

        [Test]
        public void Http_ValidProperty_CompilesReadAndWrite()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp",
                    "{\"href\":\"https://d.example.com/temp\",\"contentType\":\"application/json\"}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Entries.Select(e => e.Operation),
                Is.EquivalentTo(new[] { WoTBindingCapabilityEnum.ReadProperty, WoTBindingCapabilityEnum.WriteProperty }));
            WotCompiledForm read = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Endpoint.Scheme, Is.EqualTo("https"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("GET"));
            Assert.That(result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty)
                .OperationInfo.Method, Is.EqualTo("PUT"));
        }

        [Test]
        public void Http_MethodOverride_IsHonoured()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Action("run", "{\"href\":\"http://d/run\",\"htv:methodName\":\"POST\"}"),
                "run");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].OperationInfo.Method, Is.EqualTo("POST"));
        }

        [Test]
        public void Http_InvalidMethod_IsRejectedWithPointer()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", "{\"href\":\"http://d/x\",\"htv:methodName\":\"FETCHY\"}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            WotBindingDiagnostic error = result.Diagnostics.First(d => d.IsError);
            Assert.That(error.Code, Is.EqualTo(WotBindingDiagnosticCode.InvalidFieldValue));
            Assert.That(error.JsonPointer, Does.Contain("htv:methodName"));
        }

        [Test]
        public void Http_MissingScheme_IsRejected()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", "{\"href\":\"relative/path\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidHref), Is.True);
        }

        // ---- MQTT -------------------------------------------------------------

        [Test]
        public void Mqtt_ValidProperty_ResolvesTopicAndQos()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp",
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
        public void Mqtt_InvalidQos_IsRejected()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", "{\"href\":\"mqtt://b:1883/t\",\"mqv:qos\":5}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidFieldValue &&
                d.Term == "mqv:qos"), Is.True);
        }

        // ---- Modbus -----------------------------------------------------------

        [Test]
        public void Modbus_HoldingRegisterInt32_Compiles()
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

        [Test]
        public void Modbus_QuantityBeyondBounds_IsRejected()
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
        public void Modbus_WriteToReadOnlyEntity_IsRejected()
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
        public void Modbus_AddressBeyond16Bit_IsRejected()
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
        public void Modbus_AddressPlusQuantityOverflow_IsRejected()
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
        public void Modbus_FunctionOnlyNumericCode_MapsEntityAndMethod()
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
        public void Modbus_FunctionOnlyMnemonic_MapsCoilWrite()
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
        public void Modbus_EntityFunctionMismatch_IsRejected()
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
        public void Modbus_InvalidFunction_IsRejected()
        {
            var planner = new ModbusBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("weird",
                    "{\"href\":\"modbus+tcp://plc:502/1\",\"modv:function\":99,\"modv:address\":0}"),
                "weird");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidFieldValue &&
                d.Term == "modv:function"), Is.True);
        }

        [Test]
        public void Modbus_ExplicitWriteOpWithReadFunction_IsRejected()
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
        public void Mqtts_Scheme_CompilesWithSecureEndpoint()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp",
                    "{\"href\":\"mqtts://broker:8883/things/temp\",\"mqv:qos\":1}"),
                "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm write = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.WriteProperty);
            Assert.That(write.Endpoint.Scheme, Is.EqualTo("mqtts"));
            Assert.That(write.Endpoint.Port, Is.EqualTo(8883));
        }

        // ---- CoAP (planner only, non-executable) ------------------------------

        [Test]
        public void Coap_ValidForm_CompilesButNonExecutable()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", "{\"href\":\"coap://d/temp\",\"cov:method\":\"GET\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False,
                "The CoAP planner declares its forms non-executable (Capability.IsExecutable == false).");
        }

        [Test]
        public void Coap_InvalidMethod_IsRejected()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", "{\"href\":\"coap://d/x\",\"cov:method\":\"NOPE\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Term == "cov:method"), Is.True);
        }

        // ---- BACnet (schema only) --------------------------------------------

        [Test]
        public void Bacnet_ValidObject_Compiles()
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
        public void Bacnet_MissingInstance_IsRejected()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t",
                    "{\"bacv:objectType\":\"analogInput\",\"bacv:propertyIdentifier\":\"presentValue\"}"),
                "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.MissingRequiredField &&
                d.Term == "bacv:instanceNumber"), Is.True);
        }

        // ---- PROFINET (schema only) ------------------------------------------

        [Test]
        public void Profinet_ValidSlot_Compiles()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", "{\"pnv:slot\":1,\"pnv:subslot\":2,\"pnv:index\":100}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False);
        }

        [Test]
        public void Profinet_MissingIndex_IsRejected()
        {
            var planner = new ProfinetBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", "{\"pnv:slot\":1,\"pnv:subslot\":2}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Term == "pnv:index"), Is.True);
        }

        // ---- LoRaWAN (schema only) -------------------------------------------

        [Test]
        public void LoRaWan_ValidDevice_Compiles()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t",
                    "{\"lorawan:DevEUI\":\"0011223344556677\",\"lorawan:fPort\":10}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["devEui"], Is.EqualTo("0011223344556677"));
            Assert.That(result.Entries.All(e => e.IsExecutable), Is.False);
        }

        [Test]
        public void LoRaWan_InvalidDevEui_IsRejected()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", "{\"lorawan:DevEUI\":\"not-hex\"}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        // ---- OPC UA -----------------------------------------------------------

        [Test]
        public void OpcUa_ValidNodeId_Compiles()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t",
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=5\"}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm read = result.Entries.First(e => e.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Addressing.Target, Is.EqualTo("ns=2;i=5"));
            Assert.That(read.Endpoint.Scheme, Is.EqualTo("opc.tcp"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("Read"));
        }

        [Test]
        public void OpcUa_MissingNodeId_IsRejected()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("t", "{\"href\":\"opc.tcp://server:4840\"}"), "t");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void OpcUa_ActionCarriesComponentOf()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Action("run",
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
        public void OpcUa_EventCarriesAuthoredEventFields()
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
            Assert.That(subscribe.Addressing.Metadata["eventFields"], Is.EqualTo("ActiveState/Id|Severity"));
        }

        [Test]
        public void OpcUa_EventWithoutEventFields_OmitsMetadataKey()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Event("trigger",
                    "{\"href\":\"opc.tcp://server:4840\",\"uav:id\":\"ns=2;i=42\",\"op\":[\"subscribeevent\"]}"),
                "trigger");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());

            Assert.That(result.IsSupported, Is.True);
            WotCompiledForm subscribe = result.Entries.First(
                e => e.Operation == WoTBindingCapabilityEnum.SubscribeEvent);
            Assert.That(subscribe.Addressing.Metadata.ContainsKey("eventFields"), Is.False);
        }

        // ---- Capability metadata ---------------------------------------------

        [Test]
        public void Capabilities_PinSourcesAndNeverClaimRegistryCurrent()
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
        public void Diagnostics_ExposeJsonPointerViaSharedModel()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = WotBindingTestSupport.Form(
                WotBindingTestSupport.Property("temp", "{\"contentType\":\"application/json\"}"), "temp");

            WotBindingCompilation result = planner.Compile(form, WotBindingTestSupport.Context());
            WotBindingDiagnostic diagnostic = result.Diagnostics.First(d => d.IsError);
            WotDiagnostic shared = diagnostic.ToWotDiagnostic();

            Assert.That(shared.Location?.JsonPointer, Does.StartWith("/properties/temp/forms/0"));
        }
    }
}
