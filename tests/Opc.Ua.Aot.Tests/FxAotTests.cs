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

using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Di;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;
using Opc.Ua.Fx.Data;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises generated UAFX model activation and binary decoding after native trimming.
    /// </summary>
    public sealed class FxAotTests
    {
        /// <summary>
        /// Published models and derived reservation results require no reflection-based activation.
        /// </summary>
        [Test]
        public async Task GeneratedFxModelsAndReservationResultsAreAotSafeAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa();
            using ServiceProvider provider = services.BuildServiceProvider();
            ITelemetryContext telemetry = provider.GetRequiredService<ITelemetryContext>();
            ServiceMessageContext message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend("urn:uafx-aot:application");
            message.NamespaceUris.GetIndexOrAppend(Fx.Cm.Namespaces.FxCm);
            message.NamespaceUris.GetIndexOrAppend(Fx.Ac.Namespaces.FxAc);
            message.NamespaceUris.GetIndexOrAppend(Fx.Data.Namespaces.FxData);
            message.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            message.Factory.Builder.AddOpcUaDi().AddOpcUaFxData().AddOpcUaFxAc().AddOpcUaFxCm().Commit();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context).AddOpcUaFxData(context).AddOpcUaFxAc(context).AddOpcUaFxCm(context);
            await Assert.That(nodes.Any(node => node.NodeId ==
                NodeId.Create(2, Fx.Ac.Namespaces.FxAc, context.NamespaceUris))).IsTrue();
            await Assert.That(nodes.Any(node => node.NodeId ==
                NodeId.Create(1002, Fx.Cm.Namespaces.FxCm, context.NamespaceUris))).IsTrue();

            var result = new PubSubReserveCommunicationIdsResult2DataType
            {
                DefaultPublisherId = new Variant((uint)23),
                WriterGroupIds = [4],
                DataSetWriterIds = [5],
                TransportSpecificInfo = new Variant("opc.tcp://localhost:4840/uafx")
            };
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, message, true))
            {
                encoder.WriteExtensionObject("Result", new ExtensionObject(result));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, message, true);
            ExtensionObject decodedObject = decoder.ReadExtensionObject("Result");
            bool decoded = decodedObject.TryGetValue(out PubSubReserveCommunicationIdsResult2DataType? value);
            await Assert.That(decoded).IsTrue();
            await Assert.That(value!.DefaultPublisherId).IsEqualTo(new Variant((uint)23));
            await Assert.That(value.WriterGroupIds[0]).IsEqualTo((ushort)4);
            await Assert.That(value.DataSetWriterIds[0]).IsEqualTo((ushort)5);
            await Assert.That(value.TransportSpecificInfo)
                .IsEqualTo(new Variant("opc.tcp://localhost:4840/uafx"));
            await Assert.That(stream.Position).IsEqualTo(stream.Length);
        }
    }
}
