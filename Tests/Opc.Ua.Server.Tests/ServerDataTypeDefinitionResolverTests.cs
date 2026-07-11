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
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ComplexTypes")]
    [Parallelizable]
    public class ServerDataTypeDefinitionResolverTests
    {
        [Test]
        public void EmptyResolverReturnsFalseAndNoNamespaceTypes()
        {
            var resolver = new ServerDataTypeDefinitionResolver();

            Assert.That(resolver.TryResolve(new ExpandedNodeId(1), out UaTypeDescription expanded), Is.False);
            Assert.That(expanded, Is.Null);
            Assert.That(resolver.TryResolve(new NodeId(1), out UaTypeDescription node), Is.False);
            Assert.That(node, Is.Null);
            Assert.That(resolver.GetNamespaceTypes("urn:test"), Is.Empty);
        }

        [Test]
        public void SetResolverDelegatesAllOperations()
        {
            var description = new UaTypeDescription(
                new ExpandedNodeId(1),
                new QualifiedName("Type", 2),
                new StructureDefinition(),
                "urn:test");
            var namespaceTypes = new List<UaTypeDescription> { description };
            var inner = new Mock<IDataTypeDefinitionResolver>();
            inner.Setup(r => r.TryResolve(new ExpandedNodeId(1), out description)).Returns(true);
            inner.Setup(r => r.TryResolve(new NodeId(1), out description)).Returns(true);
            inner.Setup(r => r.GetNamespaceTypes("urn:test")).Returns(namespaceTypes);
            var resolver = new ServerDataTypeDefinitionResolver();

            resolver.SetResolver(inner.Object);

            Assert.That(resolver.TryResolve(new ExpandedNodeId(1), out UaTypeDescription expanded), Is.True);
            Assert.That(expanded, Is.SameAs(description));
            Assert.That(resolver.TryResolve(new NodeId(1), out UaTypeDescription node), Is.True);
            Assert.That(node, Is.SameAs(description));
            Assert.That(resolver.GetNamespaceTypes("urn:test"), Is.SameAs(namespaceTypes));
        }
    }
}
