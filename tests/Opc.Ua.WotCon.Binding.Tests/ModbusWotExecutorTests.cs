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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Modbus;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Tests.Support;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>End-to-end tests for the Modbus TCP executor against an in-process simulator.</summary>
    [TestFixture]
    public sealed class ModbusWotExecutorTests
    {
        private static WotProtocolBinderRegistry Registry()
            => new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new ModbusBindingPlanner() },
                new IWotBindingExecutor[] { new ModbusWotBindingExecutor() });

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
            => registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));

        [Test]
        public async Task Modbus_ReadWriteHoldingRegisterInt32_EndToEnd()
        {
            using var server = new TestModbusServer();
            // 0x12345678 stored big-endian across two holding registers.
            server.HoldingRegisters[100] = 0x1234;
            server.HoldingRegisters[101] = 0x5678;

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"level\":{\"type\":\"number\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port + "/1\",\"modv:entity\":\"holdingRegister\",\"modv:address\":100," +
                "\"modv:quantity\":2,\"modv:type\":\"int32\"}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
            WotCompiledForm write = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(0x12345678));
            }

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(new DataValue(new Variant(1000042)));
                Assert.That(result.Success, Is.True);
            }

            Assert.That(server.HoldingRegisters[100], Is.EqualTo((ushort)(1000042 >> 16)));
            Assert.That(server.HoldingRegisters[101], Is.EqualTo((ushort)(1000042 & 0xFFFF)));
        }

        [Test]
        public async Task Modbus_ReadWriteCoil_EndToEnd()
        {
            using var server = new TestModbusServer();
            server.Coils[10] = true;

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relay\":{\"type\":\"boolean\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port + "/1\",\"modv:entity\":\"coil\",\"modv:address\":10,\"modv:quantity\":1}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
            WotCompiledForm write = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync();
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(true));
            }

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(new DataValue(new Variant(false)));
                Assert.That(result.Success, Is.True);
            }

            Assert.That(server.Coils[10], Is.False);
        }
    }
}
