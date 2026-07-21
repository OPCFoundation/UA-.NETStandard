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

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Modbus;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Tests.Support;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>
    /// Executor-level hardening tests for the Modbus binding: a function-only form
    /// maps end-to-end onto the exact function code, and the executor re-validates
    /// the address / quantity range before the ushort casts so a hand-built,
    /// out-of-range compiled form fails fast instead of silently truncating.
    /// </summary>
    [TestFixture]
    public sealed class ModbusWotExecutorHardeningTests
    {
        private static WotProtocolBinderRegistry Registry()
            => new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new ModbusBindingPlanner() },
                new IWotBindingExecutor[] { new ModbusWotBindingExecutor() });

        [Test]
        public async Task Modbus_FunctionOnlyForm_ReadsHoldingRegister_EndToEnd()
        {
            using var server = new TestModbusServer();
            server.HoldingRegisters[100] = 0x1234;
            server.HoldingRegisters[101] = 0x5678;

            // Function-only form: modv:function 3 (read holding registers), no
            // modv:entity. The planner must map it onto the holding-register space.
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"level\":{\"type\":\"number\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port + "/1\",\"modv:function\":3,\"modv:address\":100," +
                "\"modv:quantity\":2,\"modv:type\":\"int32\"}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
            Assert.That(read.Addressing.Metadata["entity"], Is.EqualTo("holdingRegister"));
            Assert.That(read.OperationInfo.Method, Is.EqualTo("readHoldingRegisters"));

            IWotBindingChannel channel = await registry.OpenChannelAsync(read);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(0x12345678));
            }
        }

        [Test]
        public void Modbus_Executor_RevalidatesOutOfRangeAddress_BeforeCast()
        {
            // A hand-built compiled form whose address is beyond the 16-bit Modbus
            // space would truncate to a valid ushort without the executor's
            // re-validation. The executor must refuse it before opening a socket.
            var addressing = new WotAddressingDescriptor(
                "holdingRegister:70000:1@1",
                ImmutableDictionary<string, string>.Empty
                    .Add("entity", "holdingRegister")
                    .Add("address", "70000")
                    .Add("quantity", "1")
                    .Add("unitId", "1"));
            var payload = new WotPayloadDescriptor(
                "application/octet-stream", "octet-stream",
                ImmutableDictionary<string, string>.Empty.Add("type", "uint16"));
            var form = new WotCompiledForm(
                new WotBindingIdentity("w3c.modbus", "1.0-ed", ModbusBindingPlanner.BindingUri),
                WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                new WotEndpointDescriptor("modbus+tcp", "127.0.0.1", 502, "modbus+tcp://127.0.0.1:502"),
                addressing,
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "readHoldingRegisters"),
                payload,
                ImmutableArray<WotCredentialReference>.Empty, isExecutable: true);

            var executor = new ModbusWotBindingExecutor();
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await executor.ActivateAsync(form, new WotExecutorContext()).ConfigureAwait(false));
        }

        [Test]
        public void Modbus_Executor_RevalidatesRangeOverflow_BeforeCast()
        {
            var addressing = new WotAddressingDescriptor(
                "holdingRegister:65530:10@1",
                ImmutableDictionary<string, string>.Empty
                    .Add("entity", "holdingRegister")
                    .Add("address", "65530")
                    .Add("quantity", "10")
                    .Add("unitId", "1"));
            var payload = new WotPayloadDescriptor(
                "application/octet-stream", "octet-stream",
                ImmutableDictionary<string, string>.Empty.Add("type", "uint16"));
            var form = new WotCompiledForm(
                new WotBindingIdentity("w3c.modbus", "1.0-ed", ModbusBindingPlanner.BindingUri),
                WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                new WotEndpointDescriptor("modbus+tcp", "127.0.0.1", 502, "modbus+tcp://127.0.0.1:502"),
                addressing,
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "readHoldingRegisters"),
                payload,
                ImmutableArray<WotCredentialReference>.Empty, isExecutable: true);

            var executor = new ModbusWotBindingExecutor();
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await executor.ActivateAsync(form, new WotExecutorContext()).ConfigureAwait(false));
        }

        [Test]
        public void Modbus_Executor_RejectsQuantityThatWouldTruncateToZero()
        {
            var addressing = new WotAddressingDescriptor(
                "holdingRegister:0:65536@1",
                ImmutableDictionary<string, string>.Empty
                    .Add("entity", "holdingRegister")
                    .Add("address", "0")
                    .Add("quantity", "65536")
                    .Add("unitId", "1"));
            var payload = new WotPayloadDescriptor(
                "application/octet-stream", "octet-stream",
                ImmutableDictionary<string, string>.Empty.Add("type", "uint16"));
            var form = new WotCompiledForm(
                new WotBindingIdentity("w3c.modbus", "1.0-ed", ModbusBindingPlanner.BindingUri),
                WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                new WotEndpointDescriptor(
                    "modbus+tcp", "127.0.0.1", 502, "modbus+tcp://127.0.0.1:502"),
                addressing,
                new WotOperationDescriptor(
                    WoTBindingCapabilityEnum.ReadProperty,
                    "readproperty",
                    "readHoldingRegisters"),
                payload,
                ImmutableArray<WotCredentialReference>.Empty,
                isExecutable: true);

            var executor = new ModbusWotBindingExecutor();
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await executor
                    .ActivateAsync(form, new WotExecutorContext())
                    .ConfigureAwait(false));
        }
    }
}
