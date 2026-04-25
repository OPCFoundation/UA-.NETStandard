// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Nodes.Obsolete
{
    [TestFixture]
    public class TypeTreeTests
    {
        private TypeTree m_typeTree;

        [SetUp]
        public void SetUp()
        {
            var nodeCacheMock = new Mock<INodeCache>();
            m_typeTree = new TypeTree(nodeCacheMock.Object);
        }

        [Test]
        public void FindSuperTypeWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindSuperType(new ExpandedNodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindSuperType deprecated*"));
        }

        [Test]
        public void FindSuperTypeWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindSuperType(new NodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindSuperType deprecated*"));
        }

        [Test]
        public async Task FindSuperTypeAsyncWithExpandedNodeIdShouldThrowNotSupportedAsync()
        {
            // Act
            Func<Task> act = async () => await m_typeTree.FindSuperTypeAsync(new ExpandedNodeId("test", 0), CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.Message, Does.Match("*FindSuperTypeAsync deprecated*"));
        }

        [Test]
        public async Task FindSuperTypeAsyncWithNodeIdShouldThrowNotSupportedAsync()
        {
            // Act
            Func<Task> act = async () => await m_typeTree.FindSuperTypeAsync(new NodeId("test", 0), CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.Message, Does.Match("*FindSuperTypeAsync deprecated*"));
        }

        [Test]
        public void IsTypeOfWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsTypeOf(new ExpandedNodeId("test", 0), new ExpandedNodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsTypeOf deprecated*"));
        }

        [Test]
        public void IsTypeOfWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsTypeOf(new NodeId("test", 0), new NodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsTypeOf deprecated*"));
        }

        [Test]
        public void IsKnownWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsKnown(new ExpandedNodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsKnown deprecated*"));
        }

        [Test]
        public void IsKnownWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsKnown(new NodeId());

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsKnown deprecated*"));
        }

        [Test]
        public void FindSubTypesShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindSubTypes(new ExpandedNodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindSubTypes deprecated*"));
        }

        [Test]
        public void FindReferenceTypeNameShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindReferenceTypeName(new NodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindReferenceTypeName deprecated*"));
        }

        [Test]
        public void FindReferenceTypeShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindReferenceType(new QualifiedName("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindReferenceType deprecated*"));
        }

        [Test]
        public void IsEncodingOfShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsEncodingOf(new ExpandedNodeId("test", 0),
                new ExpandedNodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsEncodingOf deprecated*"));
        }

        [Test]
        public void IsEncodingForWithExtensionObjectShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsEncodingFor(new NodeId("test", 0),
                new ExtensionObject(new ReadAtTimeDetails()));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsEncodingFor deprecated*"));
        }

        [Test]
        public void IsEncodingForWithVariantShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.IsEncodingFor(new NodeId("test", 0), new Variant());

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*IsEncodingFor deprecated*"));
        }

        [Test]
        public void FindDataTypeIdWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindDataTypeId(new ExpandedNodeId("test", 0));

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindDataTypeId deprecated*"));
        }

        [Test]
        public void FindDataTypeIdWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => m_typeTree.FindDataTypeId(new NodeId());

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*FindDataTypeId deprecated*"));
        }
    }
}
