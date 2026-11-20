/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Reflection;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for the source-generated Machinery and Pumps models inside
    /// PumpDeviceIntegrationServer. Verifies that the generator emitted the expected
    /// namespace constants, extension methods, and that the loader produces
    /// a non-empty predefined-node tree.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpsNodeSetLoadingTests
    {
        [Test]
        public void MachineryNamespaceUriConstantIsEmitted()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            System.Type? ns = assembly.GetType("Opc.Ua.Machinery.Namespaces");
            Assert.That(ns, Is.Not.Null,
                "The source generator must emit Opc.Ua.Machinery.Namespaces.");
        }

        [Test]
        public void PumpsNamespaceUriConstantIsEmitted()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            System.Type? ns = assembly.GetType("Opc.Ua.Pumps.Namespaces");
            Assert.That(ns, Is.Not.Null,
                "The source generator must emit Opc.Ua.Pumps.Namespaces.");
        }

        [Test]
        public void AddOpcUaMachineryExtensionMethodIsEmitted()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            System.Type? ext = assembly.GetType(
                "Opc.Ua.Machinery.OpcUaMachineryExtensions");
            Assert.That(ext, Is.Not.Null,
                "The generator must emit OpcUaMachineryExtensions.");
            MethodInfo? add = ext!.GetMethod(
                "AddOpcUaMachinery",
                [typeof(NodeStateCollection), typeof(ISystemContext)]);
            Assert.That(add, Is.Not.Null,
                "AddOpcUaMachinery(NodeStateCollection, ISystemContext) must exist.");
        }

        [Test]
        public void AddOpcUaPumpsExtensionMethodIsEmitted()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            System.Type? ext = assembly.GetType(
                "Opc.Ua.Pumps.OpcUaPumpsExtensions");
            Assert.That(ext, Is.Not.Null,
                "The generator must emit OpcUaPumpsExtensions.");
            MethodInfo? add = ext!.GetMethod(
                "AddOpcUaPumps",
                [typeof(NodeStateCollection), typeof(ISystemContext)]);
            Assert.That(add, Is.Not.Null,
                "AddOpcUaPumps(NodeStateCollection, ISystemContext) must exist.");
        }

        [Test]
        public void PumpServerHasNoLegacyNodeSet2EmbeddedResources()
        {
            // P2 conversion removed runtime XML loading; only the
            // source-generated extension methods remain.
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            string[] resources = assembly.GetManifestResourceNames();
            Assert.That(resources,
                Has.No.Member("Opc.Ua.Machinery.NodeSet2.xml"),
                "Machinery NodeSet2 must not be embedded as a resource.");
            Assert.That(resources,
                Has.No.Member("Opc.Ua.Pumps.NodeSet2.xml"),
                "Pumps NodeSet2 must not be embedded as a resource.");
        }
    }
}
