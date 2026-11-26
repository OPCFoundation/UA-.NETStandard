// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            Assert.That(resolver.TryResolve(new ExpandedNodeId(1), out UaTypeDescription? expanded), Is.False);
            Assert.That(expanded, Is.Null);
            Assert.That(resolver.TryResolve(new NodeId(1), out UaTypeDescription? node), Is.False);
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

            Assert.That(resolver.TryResolve(new ExpandedNodeId(1), out UaTypeDescription? expanded), Is.True);
            Assert.That(expanded, Is.SameAs(description));
            Assert.That(resolver.TryResolve(new NodeId(1), out UaTypeDescription? node), Is.True);
            Assert.That(node, Is.SameAs(description));
            Assert.That(resolver.GetNamespaceTypes("urn:test"), Is.SameAs(namespaceTypes));
        }
    }
}
