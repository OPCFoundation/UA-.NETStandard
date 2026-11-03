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
using Microsoft.Extensions.Logging;
using Opc.Ua.PADIM;
using Opc.Ua.PADIM.Client;
using Opc.Ua.PADIM.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Roots standalone PA-DIM model loading and client/server dependency-injection registrations.
    /// </summary>
    public sealed class PadimAotTests
    {
        /// <summary>
        /// Decodes PAT values through the generated activators used by the PA-DIM client.
        /// </summary>
        [Test]
        public async Task PadimPatValuesDecodeWithGeneratedActivators()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:padim:aot:shift");
            context.NamespaceUris.GetIndexOrAppend(PADIM.Namespaces.PADIM);
            context.Factory.Builder.AddOpcUaPADIM().Commit();
            using var encoder = new BinaryEncoder(context);
            encoder.WriteVariant(null, Variant.From(new ExtensionObject(new PADIM.ChemicalSubstance
            {
                PatDictionary = PADIM.PatDictionaryEnum.CAS,
                Label = new LocalizedText("AOT substance"),
                Id = new LocalizedText("PAT-AOT-001")
            })));
            using var decoder = new BinaryDecoder(
                encoder.CloseAndReturnBuffer() ?? throw new InvalidOperationException("No encoded PAT sample."),
                context);
            Variant decoded = decoder.ReadVariant(null);
            bool extracted = decoded.TryGetStructure<PADIM.ChemicalSubstance>(
                out PADIM.ChemicalSubstance? substance);
            await Assert.That(extracted).IsTrue();
            await Assert.That(substance!.PatDictionary).IsEqualTo(PADIM.PatDictionaryEnum.CAS);
            await Assert.That(substance.Label.Text).IsEqualTo("AOT substance");
            await Assert.That(substance.Id.Text).IsEqualTo("PAT-AOT-001");
        }

        /// <summary>
        /// Loads the sole PA-DIM/IRDI producer with shifted indexes and resolves its client factory.
        /// </summary>
        [Test]
        public async Task PadimModelsAndHostingAreReachableWithoutMachinery()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:padim:aot:namespace-shift");
            messageContext.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            messageContext.NamespaceUris.GetIndexOrAppend(IRDI.Namespaces.IRDI);
            messageContext.NamespaceUris.GetIndexOrAppend(PADIM.Namespaces.PADIM);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = new TypeTable(messageContext.NamespaceUris)
            };
            var nodes = new NodeStateCollection();
            var model = new PadimModelProvider();
            model.AddPredefinedNodes(nodes, context);
            int originalCount = nodes.Count;
            model.AddPredefinedNodes(nodes, context);
            NodeId typeId = ExpandedNodeId.ToNodeId(PADIM.ObjectTypeIds.PADIMType, context.NamespaceUris);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "PadimAotServer";
                options.AutoAcceptUntrustedCertificates = true;
            }).AddPadim();
            services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "PadimAotClient";
            }).AddPadimClient();
            await using ServiceProvider container = services.BuildServiceProvider();
            PadimClientFactory factory = container.GetRequiredService<PadimClientFactory>();
            Func<CancellationToken, Task<PadimClient>> accessor =
                container.GetRequiredService<Func<CancellationToken, Task<PadimClient>>>();
            await Assert.That(originalCount > 0).IsTrue();
            await Assert.That(nodes.Count).IsEqualTo(originalCount);
            await Assert.That(nodes.Any(node => node.NodeId == typeId && node is BaseObjectTypeState)).IsTrue();
            await Assert.That(model.NamespaceUris.Count).IsEqualTo(2);
            await Assert.That(factory).IsNotNull();
            await Assert.That(accessor).IsNotNull();
        }
    }
}
