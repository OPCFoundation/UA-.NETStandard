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

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Tests.Nodes
{
    [TestFixture]
    [Category("NodeSource")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable]
    public sealed class NodeSourceLifecycleExtensionsTests
    {
        private const string kNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeSourceLifecycleExtensions";

        [Test]
        public async Task AddNodeSourceForwardsInternalFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .AddNodeSourceAsync(new EmptySource(), null, CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(provider => provider.AddAsync(
                It.Is<NodeSourceNodeManagerFactory>(factory =>
                    factory.NamespacesUris.Count == 1 &&
                    factory.NamespacesUris[0] == kNamespaceUri),
                null,
                CancellationToken.None), Times.Once);
        }

        [Test]
        public async Task ReloadNodeSourceForwardsInternalFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();
            NodeManagerRegistration registration = CreateRegistration();

            await lifecycle.Object
                .ReloadNodeSourceAsync(
                    registration,
                    new EmptySource(),
                    null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(provider => provider.ReloadAsync(
                registration,
                It.IsAny<NodeSourceNodeManagerFactory>(),
                null,
                CancellationToken.None), Times.Once);
        }

        [Test]
        public async Task ShadowReloadNodeSourceForwardsInternalFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();
            NodeManagerRegistration registration = CreateRegistration();

            await lifecycle.Object
                .ShadowReloadNodeSourceAsync(
                    registration,
                    new EmptySource(),
                    CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(provider => provider.ShadowReloadAsync(
                registration,
                It.IsAny<NodeSourceNodeManagerFactory>(),
                CancellationToken.None), Times.Once);
        }

        [Test]
        public async Task ImmediateReloadNodeSourceForwardsInternalFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();
            NodeManagerRegistration registration = CreateRegistration();

            await lifecycle.Object
                .ImmediateReloadNodeSourceAsync(
                    registration,
                    new EmptySource(),
                    CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(provider => provider.ImmediateReloadAsync(
                registration,
                It.IsAny<NodeSourceNodeManagerFactory>(),
                CancellationToken.None), Times.Once);
        }

        [Test]
        public void LifecycleExtensionsRejectNullArguments()
        {
            NodeManagerRegistration registration = CreateRegistration();
            INodeSource source = new EmptySource();
            INodeManagerLifecycle lifecycle = CreateLifecycle().Object;

            Assert.Multiple(() =>
            {
                Assert.That(
                    async () => await NodeSourceLifecycleExtensions
                        .AddNodeSourceAsync(null!, source)
                        .ConfigureAwait(false),
                    Throws.ArgumentNullException.With.Property("ParamName")
                        .EqualTo("lifecycle"));
                Assert.That(
                    async () => await lifecycle
                        .AddNodeSourceAsync(null!)
                        .ConfigureAwait(false),
                    Throws.ArgumentNullException.With.Property("ParamName")
                        .EqualTo("source"));
                Assert.That(
                    async () => await lifecycle
                        .ReloadNodeSourceAsync(null!, source)
                        .ConfigureAwait(false),
                    Throws.ArgumentNullException.With.Property("ParamName")
                        .EqualTo("registration"));
                Assert.That(
                    async () => await lifecycle
                        .ShadowReloadNodeSourceAsync(registration, null!)
                        .ConfigureAwait(false),
                    Throws.ArgumentNullException.With.Property("ParamName")
                        .EqualTo("replacement"));
            });
        }

        private static Mock<INodeManagerLifecycle> CreateLifecycle()
        {
            NodeManagerRegistration registration = CreateRegistration();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle
                .Setup(provider => provider.AddAsync(
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<IOperationContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            lifecycle
                .Setup(provider => provider.ReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<IOperationContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            lifecycle
                .Setup(provider => provider.ShadowReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            lifecycle
                .Setup(provider => provider.ImmediateReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            return lifecycle;
        }

        private static NodeManagerRegistration CreateRegistration()
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(value => value.NamespaceUris).Returns([kNamespaceUri]);
            return new NodeManagerRegistration(Guid.NewGuid(), 1, manager.Object);
        }

        private sealed class EmptySource : INodeSource
        {
            public ArrayOf<string> NamespaceUris => [kNamespaceUri];

            public ValueTask BuildAsync(
                INodeGraphBuilder builder,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }
    }
}
