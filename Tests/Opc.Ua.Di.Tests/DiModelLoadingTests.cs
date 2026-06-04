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
