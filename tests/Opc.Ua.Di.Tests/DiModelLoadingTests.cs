/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Smoke tests for <see cref="DiNodeManager"/> and the factory:
    /// model loads, namespace URIs are correct, generator output is wired.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class DiModelLoadingTests
    {
        [Test]
        public void DiNamespaceUriIsExposed()
        {
            Assert.That(DiNodeManager.DiNamespaceUri,
                Is.EqualTo("http://opcfoundation.org/UA/DI/"));
        }

        [Test]
        public void DiNamespacesConstantMatches()
        {
            Assert.That(global::Opc.Ua.Di.Namespaces.OpcUaDi,
                Is.EqualTo("http://opcfoundation.org/UA/DI/"));
        }

        [Test]
        public void DiHasIdentifiersGenerated()
        {
            // Sanity: a few well-known DI ObjectType ids should be available.
            Assert.That(global::Opc.Ua.Di.ObjectTypes.DeviceType, Is.Not.Zero);
            Assert.That(global::Opc.Ua.Di.ObjectTypes.TopologyElementType, Is.Not.Zero);
            Assert.That(global::Opc.Ua.Di.ObjectTypes.FunctionalGroupType, Is.Not.Zero);
        }

        [Test]
        public void AddOpcUaDiPopulatesPredefinedNodes()
        {
            var systemContext = new SystemContext(telemetry: null!)
            {
                NamespaceUris = new NamespaceTable()
            };
            systemContext.NamespaceUris.Append("http://opcfoundation.org/UA/DI/");

            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(systemContext);

            Assert.That(nodes, Is.Not.Empty,
                "DI model should contribute predefined nodes.");
            Assert.That(nodes, Has.Count.GreaterThan(10),
                "DI model should contribute non-trivial number of nodes.");
        }

        [Test]
        public void DiNodeManagerFactoryReportsDiNamespaceUri()
        {
            var factory = new DiNodeManagerFactory();
            Assert.That(factory.NamespacesUris, Is.Not.Empty);
            Assert.That(factory.NamespacesUris.ToArray(),
                Has.Member("http://opcfoundation.org/UA/DI/"));
        }
    }
}
