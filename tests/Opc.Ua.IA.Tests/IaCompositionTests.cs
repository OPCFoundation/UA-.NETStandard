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

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Server;
using Opc.Ua.IA.Client;
using Opc.Ua.IA.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.IA.Tests
{
    /// <summary>
    /// Checks model ownership and DI registration independently of a live connection.
    /// </summary>
    [TestFixture]
    public sealed class IaCompositionTests
    {
        /// <summary>
        /// Repeated model registration does not create a second producer or duplicate declarations.
        /// </summary>
        [Test]
        public void IaModelRegistrationIsIdempotent()
        {
            var fixture = new IaTestContext();
            var provider = new IaModelProvider();
            var nodes = new NodeStateCollection();
            provider.AddPredefinedNodes(nodes, fixture.Context);
            int count = nodes.Count;
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(nodes.Select(node => node.NodeId), Is.SupersetOf(new[]
            {
                ExpandedNodeId.ToNodeId(ObjectTypeIds.BasicStacklightType, fixture.Context.NamespaceUris),
                ExpandedNodeId.ToNodeId(ObjectTypeIds.IStatisticsType, fixture.Context.NamespaceUris),
                ExpandedNodeId.ToNodeId(ObjectTypeIds.CalibrationTargetType, fixture.Context.NamespaceUris)
            }));
            provider.AddPredefinedNodes(nodes, fixture.Context);
            Assert.That(nodes.Count, Is.EqualTo(count));
            Assert.That(nodes.Select(node => node.NodeId).Distinct().Count(), Is.EqualTo(count));
            Assert.That(typeof(BasicStacklightState).Assembly, Is.SameAs(typeof(ObjectTypeIds).Assembly));
            Assert.That(typeof(IaStacklight).Assembly, Is.Not.SameAs(typeof(BasicStacklightState).Assembly));
        }

        /// <summary>
        /// The IA registration adds models to one Machinery owner without registering another node manager.
        /// </summary>
        [Test]
        public void HostingComposesOneModelProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer().AddMachinery()
                .AddIndustrialAutomation().AddIndustrialAutomation();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<IDiModelProvider>().OfType<IaModelProvider>().Count(), Is.EqualTo(1));
            Assert.That(provider.GetServices<Opc.Ua.Server.Hosting.OpcUaServerNodeManagerRegistration>().Count(),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Direct construction rejects missing ownership and identity without touching a session.
        /// </summary>
        [Test]
        public void ClientRejectsMissingSessionAndNode()
        {
            Assert.That(() => new IndustrialAutomationClient(null!, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.MessageContext)
                .Returns(new ServiceMessageContext(NUnitTelemetryContext.Create(), new EncodeableFactory()));
            var client = new IndustrialAutomationClient(session.Object, NUnitTelemetryContext.Create());
            Assert.That(() => client.Stacklight(NodeId.Null), Throws.ArgumentNullException);
            Assert.That(() => client.DiscoverAsync(NodeId.Null), Throws.ArgumentNullException);
            Assert.That(() => client.DiscoverAsync(new NodeId(1), maximumNodes: 0), Throws.ArgumentOutOfRangeException);
        }
    }
}
