// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.Obsolete
{
    using FluentAssertions;
    using Moq;
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public class TypeTreeTests
    {
        private readonly TypeTree _typeTree;

        public TypeTreeTests()
        {
            var nodeCacheMock = new Mock<INodeCache>();
            _typeTree = new TypeTree(nodeCacheMock.Object);
        }

        [Fact]
        public void FindSuperTypeWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindSuperType(new ExpandedNodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindSuperType deprecated*");
        }

        [Fact]
        public void FindSuperTypeWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindSuperType(new NodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindSuperType deprecated*");
        }

        [Fact]
        public async Task FindSuperTypeAsyncWithExpandedNodeIdShouldThrowNotSupportedAsync()
        {
            // Act
            Func<Task> act = async () => await _typeTree.FindSuperTypeAsync(new ExpandedNodeId("test", 0), CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>()
                .WithMessage("*FindSuperTypeAsync deprecated*");
        }

        [Fact]
        public async Task FindSuperTypeAsyncWithNodeIdShouldThrowNotSupportedAsync()
        {
            // Act
            Func<Task> act = async () => await _typeTree.FindSuperTypeAsync(new NodeId("test", 0), CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>()
                .WithMessage("*FindSuperTypeAsync deprecated*");
        }

        [Fact]
        public void IsTypeOfWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsTypeOf(new ExpandedNodeId("test", 0), new ExpandedNodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsTypeOf deprecated*");
        }

        [Fact]
        public void IsTypeOfWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsTypeOf(new NodeId("test", 0), new NodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsTypeOf deprecated*");
        }

        [Fact]
        public void IsKnownWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsKnown(new ExpandedNodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsKnown deprecated*");
        }

        [Fact]
        public void IsKnownWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsKnown(new NodeId());

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsKnown deprecated*");
        }

        [Fact]
        public void FindSubTypesShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindSubTypes(new ExpandedNodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindSubTypes deprecated*");
        }

        [Fact]
        public void FindReferenceTypeNameShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindReferenceTypeName(new NodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindReferenceTypeName deprecated*");
        }

        [Fact]
        public void FindReferenceTypeShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindReferenceType(new QualifiedName("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindReferenceType deprecated*");
        }

        [Fact]
        public void IsEncodingOfShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsEncodingOf(new ExpandedNodeId("test", 0),
                new ExpandedNodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsEncodingOf deprecated*");
        }

        [Fact]
        public void IsEncodingForWithExtensionObjectShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsEncodingFor(new NodeId("test", 0),
                new ExtensionObject(new ExpandedNodeId("test", 0), new ReadAtTimeDetails()));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsEncodingFor deprecated*");
        }

        [Fact]
        public void IsEncodingForWithObjectShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.IsEncodingFor(new NodeId("test", 0), new object());

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*IsEncodingFor deprecated*");
        }

        [Fact]
        public void FindDataTypeIdWithExpandedNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindDataTypeId(new ExpandedNodeId("test", 0));

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindDataTypeId deprecated*");
        }

        [Fact]
        public void FindDataTypeIdWithNodeIdShouldThrowNotSupported()
        {
            // Act
            Action act = () => _typeTree.FindDataTypeId(new NodeId());

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*FindDataTypeId deprecated*");
        }
    }
}
