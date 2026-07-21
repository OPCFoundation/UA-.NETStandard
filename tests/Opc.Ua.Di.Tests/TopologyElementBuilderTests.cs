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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests the additive topology-element builder surface with generated
    /// Pump companion-model states.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("TopologyElementBuilder")]
    public sealed class TopologyElementBuilderTests
    {
        private ServerFixture<StandardServer> m_fixture = null!;
        private PumpNodeManager m_manager = null!;
        private StandardServer m_server = null!;
        private ushort m_pumpsNamespaceIndex;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_manager = new PumpNodeManager(m_server.CurrentInstance, m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await m_manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            m_pumpsNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                global::Opc.Ua.Pumps.Namespaces.Pumps);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            m_manager.Dispose();
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task WrapsExistingPumpByInstanceNodeIdAndBrowseNameAsync()
        {
            var browseName = new QualifiedName(
                "Topology Lookup Pump",
                m_pumpsNamespaceIndex);
            PumpState pump = await m_manager.CreatePumpAsync(browseName)
                .ConfigureAwait(false);

            ITopologyElementBuilder<PumpState> byInstance =
                m_manager.TopologyElement(pump);
            ITopologyElementBuilder<PumpState> byNodeId =
                m_manager.TopologyElement<PumpState>(pump.NodeId);
            ITopologyElementBuilder<PumpState> byBrowseName =
                m_manager.TopologyElementByBrowseName<PumpState>(browseName);

            Assert.That(byInstance.Element, Is.SameAs(pump));
            Assert.That(byNodeId.Element, Is.SameAs(pump));
            Assert.That(byBrowseName.Element, Is.SameAs(pump));
        }

        [Test]
        public async Task ConfiguresGroupsReferencesAndRawStateAsync()
        {
            PumpState source = await m_manager.CreatePumpAsync(
                new QualifiedName("Topology Source Pump", m_pumpsNamespaceIndex))
                .ConfigureAwait(false);
            PumpState target = await m_manager.CreatePumpAsync(
                new QualifiedName("Topology Target Pump", m_pumpsNamespaceIndex))
                .ConfigureAwait(false);
            ITopologyElementBuilder<PumpState> builder =
                m_manager.TopologyElement(source);

            FunctionalGroupState? first = null;
            FunctionalGroupState? second = null;
            PumpState? observed = null;
            builder.WithDiagnosticsGroup(group => first = group.Group)
                .WithDiagnosticsGroup(group => second = group.Group)
                .ConnectsTo(target.NodeId)
                .Configure((element, _) => observed = element);

            Assert.That(second, Is.SameAs(first));
            Assert.That(observed, Is.SameAs(source));

            NodeId connectsTo = NodeId.Create(
                global::Opc.Ua.Di.ReferenceTypes.ConnectsTo,
                DiNodeManager.DiNamespaceUri,
                m_server.CurrentInstance.NamespaceUris);
            Assert.That(
                source.ReferenceExists(connectsTo, false, target.NodeId),
                Is.True);
        }

        [Test]
        public void ByNodeIdRejectsNonMatchingStateType()
        {
            NodeId deviceSetId = NodeId.Create(
                global::Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                m_server.CurrentInstance.NamespaceUris);

            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => m_manager.TopologyElement<PumpState>(deviceSetId))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }
    }
}
