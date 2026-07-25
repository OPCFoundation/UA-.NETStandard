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
using Opc.Ua.ISA95;
using Opc.Ua.ISA95.Client;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT roots for the ISA-95 generated models and DI surfaces.
    /// </summary>
    public class Isa95AotTests
    {
        [Test]
        public async Task GeneratedModelsAndDiRegistrationAreAotSafeAsync()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "Isa95AotServer";
                    options.ApplicationUri =
                        "urn:localhost:OPCFoundation:Isa95AotServer";
                })
                .AddIsa95Server();
            services.AddOpcUa().AddIsa95Client(options =>
                options.LazyConnect = false);

            using ServiceProvider serviceProvider =
                services.BuildServiceProvider();
            Isa95NodeManagerFactory nodeManagerFactory =
                serviceProvider.GetService<Isa95NodeManagerFactory>();
            IIsa95JobOrderReceiverV1 receiverV1 =
                serviceProvider.GetService<IIsa95JobOrderReceiverV1>();
            IIsa95JobOrderReceiverV2 receiverV2 =
                serviceProvider.GetService<IIsa95JobOrderReceiverV2>();
            Func<Client.ISession, Isa95Client> clientFactory =
                serviceProvider.GetService<Func<Client.ISession, Isa95Client>>();

            await Assert.That(nodeManagerFactory).IsNotNull();
            await Assert.That(receiverV1).IsNotNull();
            await Assert.That(receiverV2).IsSameReferenceAs(receiverV1);
            await Assert.That(clientFactory).IsNotNull();

            ITelemetryContext telemetry =
                serviceProvider.GetRequiredService<ITelemetryContext>();
            var messageContext =
                ServiceMessageContext.Create(telemetry);
            IEncodeableFactoryBuilder factoryBuilder =
                messageContext.Factory.Builder
                    .AddOpcUaISA95();
            factoryBuilder = V1.OpcUaISA95JobControlV1Extensions
                .AddOpcUaISA95JobControlV1(factoryBuilder);
            factoryBuilder = V2.OpcUaISA95JobControlV2Extensions
                .AddOpcUaISA95JobControlV2(factoryBuilder);
            factoryBuilder.Commit();

            var order = new V2.ISA95JobOrderDataType
            {
                JobOrderID = "aot-job"
            };
            var encoded = Variant.FromStructure(order);
            await Assert.That(
                encoded.TryGetStructure(out V2.ISA95JobOrderDataType decoded))
                .IsTrue();
            await Assert.That(decoded.JobOrderID).IsEqualTo(order.JobOrderID);

            messageContext.NamespaceUris.GetIndexOrAppend(
                V2.Namespaces.ISA95JobControlV2);
            var registry = new EventRecordDecoderRegistry();
            V2.ISA95JobControlV2EventRecordDecoders
                .RegisterISA95JobControlV2Decoders(
                    registry,
                    messageContext.NamespaceUris);
            await Assert.That(registry.StandardFields.Length).IsGreaterThan(0);
        }
    }
}
