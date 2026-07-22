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

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.TestFramework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Validates generated PumpType instances against the mandatory Pumps
    /// companion-model structure used by PumpDeviceIntegrationServer.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpTypeInstanceTests
    {
        [Test]
        public async Task PumpInstanceHasPumpTypeAndMandatoryIdentificationAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            PumpNodeManager? manager = null;
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                manager = new PumpNodeManager(server.CurrentInstance, fixture.Config);
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

                ushort pumpsNamespaceIndex = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                    global::Opc.Ua.Pumps.Namespaces.Pumps);
                PumpState pump = await manager.CreatePumpAsync(
                    new QualifiedName("Pump #2", pumpsNamespaceIndex),
                    default).ConfigureAwait(false);

                Assert.That(
                    pump.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(
                        global::Opc.Ua.Pumps.ObjectTypes.PumpType,
                        global::Opc.Ua.Pumps.Namespaces.Pumps,
                        server.CurrentInstance.NamespaceUris)));
                Assert.That(
                    pump.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(pump.Identification, Is.Not.Null);
                Assert.That(
                    pump.Identification!.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(
                        global::Opc.Ua.Pumps.ObjectTypes.PumpIdentificationType,
                        global::Opc.Ua.Pumps.Namespaces.Pumps,
                        server.CurrentInstance.NamespaceUris)));
                Assert.That(
                    pump.Identification.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));

                ITopologyElementBuilder<PumpState> builder =
                    manager.TopologyElement(pump);
                builder.WithIdentificationGroup(group => group.Configure(node =>
                    node.WithProperty(
                        new QualifiedName("Manufacturer", manager.DiNamespaceIndex),
                        Variant.From(new LocalizedText("Acme Pumps Inc.")))));

                NodeState? manufacturer = pump.Identification.FindChild(
                    manager.SystemContext,
                    new QualifiedName("Manufacturer", manager.DiNamespaceIndex));
                Assert.That(manufacturer, Is.InstanceOf<PropertyState>());
                Assert.That(
                    ((PropertyState)manufacturer!).TypeDefinitionId,
                    Is.EqualTo(Opc.Ua.Types.VariableTypeIds.PropertyType));
                Assert.That(
                    pump.FindChild(
                        manager.SystemContext,
                        new QualifiedName(
                            "SoftwareUpdate",
                            manager.DiNamespaceIndex)),
                    Is.Null);
            }
            finally
            {
                manager?.Dispose();
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
