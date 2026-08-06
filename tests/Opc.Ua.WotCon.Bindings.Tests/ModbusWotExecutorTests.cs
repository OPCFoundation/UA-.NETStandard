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
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// End-to-end tests for the Modbus TCP executor against an in-process simulator.
    /// </summary>
    [TestFixture]
    public sealed class ModbusWotExecutorTests
    {
        private static WotProtocolBinderRegistry Registry()
        {
            return new WotProtocolBinderRegistry(
                        [new ModbusBindingPlanner()],
                        [new ModbusWotBindingExecutor()],
                        endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
        {
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                        "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
        }

        [Test]
        public async Task ModbusReadWriteHoldingRegisterInt32EndToEnd()
        {
            using var server = new TestModbusServer();
            // 0x12345678 stored big-endian across two holding registers.
            server.HoldingRegisters[100] = 0x1234;
            server.HoldingRegisters[101] = 0x5678;

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"level\":{\"type\":\"number\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:entity\":\"holdingRegister\",\"modv:address\":100," +
                "\"modv:quantity\":2,\"modv:type\":\"int32\"}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(0x12345678));
            }

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(
                    new DataValue(new Variant(1000042))).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            Assert.That(server.HoldingRegisters[100], Is.EqualTo((ushort)(1000042 >> 16)));
            Assert.That(server.HoldingRegisters[101], Is.EqualTo((ushort)(1000042 & 0xFFFF)));
        }

        [Test]
        public async Task ModbusReadWriteCoilEndToEnd()
        {
            using var server = new TestModbusServer();
            server.Coils[10] = true;

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relay\":{\"type\":\"boolean\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:entity\":\"coil\",\"modv:address\":10,\"modv:quantity\":1}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            Assert.That(read.Payload.CodecId, Is.EqualTo(OctetStreamWotPayloadCodec.Instance.Id));
            Assert.That(read.Payload.Metadata["type"], Is.EqualTo("boolean"));

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.True);
            }

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(
                    new DataValue(new Variant(false))).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            Assert.That(server.Coils[10], Is.False);
        }

        [Test]
        public async Task ModbusSingleCoilRejectsNullAndNonBooleanValues()
        {
            using var server = new TestModbusServer();
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relay\":{\"type\":\"boolean\",\"forms\":[{" +
                "\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:entity\":\"coil\",\"modv:address\":11,\"modv:quantity\":1," +
                "\"op\":[\"writeproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotCompiledForm write = Plan(registry, td).CompiledForms.Single();
            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                Variant[] invalidValues =
                [
                    Variant.Null,
                    new Variant(1),
                    new Variant("true"),
                    new Variant((ArrayOf<bool>)[true])
                ];
                foreach (Variant invalidValue in invalidValues)
                {
                    WotWriteResult result = await channel
                        .WriteAsync(new DataValue(invalidValue))
                        .ConfigureAwait(false);
                    Assert.That(result.Status, Is.EqualTo(StatusCodes.BadTypeMismatch));
                    Assert.That(result.Error, Does.Contain("Boolean scalar"));
                }
            }

            Assert.That(server.LastFunctionCode, Is.Zero);
            Assert.That(server.Coils[11], Is.False);
        }

        [Test]
        public async Task ModbusReadsMultipleCoilsAsBooleanArrayEndToEnd()
        {
            using var server = new TestModbusServer();
            bool[] expected = [true, false, true, true, false, false, true, false, true, true];
            for (int i = 0; i < expected.Length; i++)
            {
                server.Coils[20 + i] = expected[i];
            }

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relays\":{\"type\":\"array\",\"items\":{\"type\":\"boolean\"}," +
                "\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:entity\":\"coil\",\"modv:address\":20,\"modv:quantity\":10," +
                "\"op\":[\"readproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.Single();
            Assert.That(read.OperationInfo.Method, Is.EqualTo("readCoil"));
            Assert.That(read.Payload.CodecId, Is.EqualTo(OctetStreamWotPayloadCodec.Instance.Id));
            Assert.That(read.Payload.Metadata["type"], Is.EqualTo("boolean[]"));

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(
                    result.Value.WrappedValue.TryGetValue(out ArrayOf<bool> actual),
                    Is.True);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        [Test]
        public async Task ModbusReadsMultipleDiscreteInputsAsBooleanArrayEndToEnd()
        {
            using var server = new TestModbusServer();
            bool[] expected = [false, true, true, false, true, false, false, true, true];
            for (int i = 0; i < expected.Length; i++)
            {
                server.DiscreteInputs[30 + i] = expected[i];
            }

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"inputs\":{\"type\":\"array\",\"items\":{\"type\":\"boolean\"}," +
                "\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:entity\":\"discreteInput\",\"modv:address\":30,\"modv:quantity\":9," +
                "\"op\":[\"readproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.Single();
            Assert.That(read.OperationInfo.Method, Is.EqualTo("readDiscreteInput"));
            Assert.That(read.Payload.Metadata["type"], Is.EqualTo("boolean[]"));

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(
                    result.Value.WrappedValue.TryGetValue(out ArrayOf<bool> actual),
                    Is.True);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        [Test]
        public async Task ModbusFunction15WritesMultipleCoilsEndToEnd()
        {
            using var server = new TestModbusServer();
            bool[] expected = [true, false, true, true, false, false, true, false, true, true];

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relays\":{\"type\":\"array\",\"items\":{\"type\":\"boolean\"}," +
                "\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:function\":15,\"modv:address\":40,\"modv:quantity\":10," +
                "\"op\":[\"writeproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm write = plan.CompiledForms.Single();
            Assert.That(write.OperationInfo.Method, Is.EqualTo("writeMultipleCoils"));
            Assert.That(write.Payload.CodecId, Is.EqualTo(OctetStreamWotPayloadCodec.Instance.Id));
            Assert.That(write.Payload.Metadata["type"], Is.EqualTo("boolean[]"));

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel
                    .WriteAsync(new DataValue(new Variant((ArrayOf<bool>)expected)))
                    .ConfigureAwait(false);
                Assert.That(result.Success, Is.True, result.Error);
            }

            Assert.That(server.LastFunctionCode, Is.EqualTo(0x0F));
            Assert.That(server.Coils.Skip(40).Take(expected.Length), Is.EqualTo(expected));
        }

        [Test]
        public async Task ModbusFunction15QuantityOneAcceptsScalarEndToEnd()
        {
            using var server = new TestModbusServer();
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relay\":{\"type\":\"boolean\",\"forms\":[{" +
                "\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:function\":15,\"modv:address\":45,\"modv:quantity\":1," +
                "\"op\":[\"writeproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotCompiledForm write = Plan(registry, td).CompiledForms.Single();
            Assert.That(write.OperationInfo.Method, Is.EqualTo("writeMultipleCoils"));
            Assert.That(write.Payload.Metadata["type"], Is.EqualTo("boolean"));

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult nullResult = await channel
                    .WriteAsync(new DataValue(Variant.Null))
                    .ConfigureAwait(false);
                Assert.That(nullResult.Status, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(server.LastFunctionCode, Is.Zero);

                WotWriteResult wrongType = await channel
                    .WriteAsync(new DataValue(new Variant(1)))
                    .ConfigureAwait(false);
                Assert.That(wrongType.Status, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(server.LastFunctionCode, Is.Zero);

                WotWriteResult arrayValue = await channel
                    .WriteAsync(new DataValue(new Variant((ArrayOf<bool>)[true])))
                    .ConfigureAwait(false);
                Assert.That(arrayValue.Status, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(server.LastFunctionCode, Is.Zero);

                WotWriteResult result = await channel
                    .WriteAsync(new DataValue(new Variant(true)))
                    .ConfigureAwait(false);
                Assert.That(result.Success, Is.True, result.Error);
            }

            Assert.That(server.LastFunctionCode, Is.EqualTo(0x0F));
            Assert.That(server.Coils[45], Is.True);
        }

        [Test]
        public async Task ModbusMultipleCoilWriteRejectsScalarAndWrongLength()
        {
            using var server = new TestModbusServer();
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"relays\":{\"type\":\"array\",\"items\":{\"type\":\"boolean\"}," +
                "\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                server.Port +
                "/1\",\"modv:entity\":\"coil\",\"modv:address\":50,\"modv:quantity\":3," +
                "\"op\":[\"writeproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotCompiledForm write = Plan(registry, td).CompiledForms.Single();
            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult scalar = await channel
                    .WriteAsync(new DataValue(new Variant(true)))
                    .ConfigureAwait(false);
                Assert.That(scalar.Status, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(scalar.Error, Does.Contain("array of 3 Boolean values"));

                WotWriteResult wrongLength = await channel
                    .WriteAsync(new DataValue(new Variant((ArrayOf<bool>)[true, false])))
                    .ConfigureAwait(false);
                Assert.That(wrongLength.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(wrongLength.Error, Does.Contain("exactly 3 Boolean values"));
            }

            Assert.That(server.LastFunctionCode, Is.Zero);
        }
    }
}
