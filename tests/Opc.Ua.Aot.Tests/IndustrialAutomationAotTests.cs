/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server;
using Opc.Ua.IA;
using Opc.Ua.IA.Client;
using Opc.Ua.IA.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Roots IA model loading, encoding and public DI registrations for native execution.
    /// </summary>
    public sealed class IndustrialAutomationAotTests
    {
        /// <summary>
        /// Loads all IA concepts and resolves client registration without opening a connection.
        /// </summary>
        [Test]
        public async Task IndustrialAutomationModelEncodingAndHostingAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            ServiceMessageContext messages = ServiceMessageContext.Create(telemetry);
            messages.NamespaceUris.GetIndexOrAppend(IA.Namespaces.IA);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messages.NamespaceUris,
                EncodeableFactory = messages.Factory
            };
            var nodes = new NodeStateCollection();
            var model = new IaModelProvider();
            model.AddPredefinedNodes(nodes, context);
            int count = nodes.Count;
            model.AddPredefinedNodes(nodes, context);

            var color = new RGBWDataType
            {
                Red = 200,
                Green = 10,
                Blue = 20,
                White = 30,
                EncodingMask = (uint)RGBWDataTypeFields.White
            };
            byte[] encoded = BinaryEncoder.EncodeMessage(color, messages);
            RGBWDataType decoded = BinaryDecoder.DecodeMessage<RGBWDataType>(encoded, messages);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "IndustrialAutomationAotServer")
                .AddIndustrialAutomation();
            services.AddOpcUa().AddClient(options =>
            {
                options.Configuration = new ApplicationConfiguration(telemetry)
                {
                    ApplicationName = "IndustrialAutomationAotClient",
                    ApplicationType = ApplicationType.Client,
                    ClientConfiguration = new ClientConfiguration(),
                    SecurityConfiguration = new SecurityConfiguration(),
                    TransportQuotas = new TransportQuotas()
                };
            }).AddIndustrialAutomationClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            IndustrialAutomationClientFactory factory =
                provider.GetRequiredService<IndustrialAutomationClientFactory>();
            int registrations = provider.GetServices<IDiModelProvider>().OfType<IaModelProvider>().Count();

            await Assert.That(count > 0).IsTrue();
            await Assert.That(nodes.Count).IsEqualTo(count);
            await Assert.That(registrations).IsEqualTo(1);
            await Assert.That(factory).IsNotNull();
            await Assert.That(decoded.IsEqual(color)).IsTrue();
            await Assert.That(decoded.Red).IsEqualTo((byte)200);
            await Assert.That(decoded.Green).IsEqualTo((byte)10);
            await Assert.That(decoded.Blue).IsEqualTo((byte)20);
            await Assert.That(decoded.White).IsEqualTo((byte)30);
            await Assert.That(decoded.EncodingMask).IsEqualTo((uint)RGBWDataTypeFields.White);
            await Assert.That(nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.BasicStacklightType, messages.NamespaceUris))).IsTrue();
            await Assert.That(nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.IStatisticsType, messages.NamespaceUris))).IsTrue();
            await Assert.That(nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.CalibrationTargetType, messages.NamespaceUris))).IsTrue();
        }
    }
}
