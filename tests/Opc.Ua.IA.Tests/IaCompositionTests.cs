// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            Assert.That(nodes, Has.Some.Property(nameof(NodeState.NodeId)).EqualTo(
                ExpandedNodeId.ToNodeId(ObjectTypeIds.BasicStacklightType, fixture.Context.NamespaceUris)));
            Assert.That(nodes, Has.Some.Property(nameof(NodeState.NodeId)).EqualTo(
                ExpandedNodeId.ToNodeId(ObjectTypeIds.IStatisticsType, fixture.Context.NamespaceUris)));
            Assert.That(nodes, Has.Some.Property(nameof(NodeState.NodeId)).EqualTo(
                ExpandedNodeId.ToNodeId(ObjectTypeIds.CalibrationTargetType, fixture.Context.NamespaceUris)));
            provider.AddPredefinedNodes(nodes, fixture.Context);
            Assert.That(nodes, Has.Count.EqualTo(count));
            Assert.That(nodes.Select(node => node.NodeId).Distinct().ToArray(), Has.Length.EqualTo(count));
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
            services.AddOpcUa().AddServer(options => options.ApplicationName = "IaCompositionTest").AddMachinery()
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
            ServiceMessageContext messageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            session.SetupGet(value => value.MessageContext)
                .Returns(messageContext);
            var client = new IndustrialAutomationClient(session.Object, NUnitTelemetryContext.Create());
            Assert.That(() => client.Stacklight(NodeId.Null), Throws.ArgumentNullException);
            Assert.That(() => client.DiscoverAsync(NodeId.Null), Throws.ArgumentNullException);
            Assert.That(() => client.DiscoverAsync(new NodeId(1), maximumNodes: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
