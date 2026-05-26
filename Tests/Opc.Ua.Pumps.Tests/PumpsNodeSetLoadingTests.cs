/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.IO;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Smoke tests for the OPC 40223 Pumps + Machinery NodeSet2 XML
    /// files embedded in MinimalPumpServer. Verifies the NodeSet2 XMLs
    /// are present, parsable, and import without error.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpsNodeSetLoadingTests
    {
        [Test]
        public void PumpsNodeSetXmlEmbeddedResourceExists()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(
                "Opc.Ua.Pumps.NodeSet2.xml");
            Assert.That(stream, Is.Not.Null,
                "Pumps NodeSet2 XML embedded resource must be present.");
            Assert.That(stream!.Length, Is.GreaterThan(100_000),
                "Pumps NodeSet2 is a large resource (~6.5 MB).");
        }

        [Test]
        public void MachineryNodeSetXmlEmbeddedResourceExists()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(
                "Opc.Ua.Machinery.NodeSet2.xml");
            Assert.That(stream, Is.Not.Null,
                "Machinery NodeSet2 XML embedded resource must be present.");
        }

        [Test]
        public void PumpsNodeSetXmlIsReadable()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(
                "Opc.Ua.Pumps.NodeSet2.xml");
            Assert.That(stream, Is.Not.Null);

            Opc.Ua.Export.UANodeSet? nodeSet = Opc.Ua.Export.UANodeSet.Read(stream!);
            Assert.That(nodeSet, Is.Not.Null,
                "Pumps NodeSet2 XML must be a parsable UANodeSet.");
            Assert.That(nodeSet!.NamespaceUris, Is.Not.Null);
            Assert.That(nodeSet.NamespaceUris,
                Has.Member("http://opcfoundation.org/UA/Pumps/"));
        }

        [Test]
        public void PumpsModelDeclaresExpectedDependencies()
        {
            Assembly assembly = typeof(global::Pumps.PumpNodeManager).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(
                "Opc.Ua.Pumps.NodeSet2.xml");
            Opc.Ua.Export.UANodeSet? nodeSet = Opc.Ua.Export.UANodeSet.Read(stream!);

            // Pumps depends on Machinery and DI.
            Assert.That(nodeSet!.NamespaceUris,
                Has.Member("http://opcfoundation.org/UA/Machinery/"));
            Assert.That(nodeSet.NamespaceUris,
                Has.Member("http://opcfoundation.org/UA/DI/"));
        }
    }
}
