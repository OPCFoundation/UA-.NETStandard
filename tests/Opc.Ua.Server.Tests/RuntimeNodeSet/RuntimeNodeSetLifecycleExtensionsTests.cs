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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Covers the argument contract and factory wiring of the runtime NodeSet
    /// lifecycle extension methods. The extensions are a thin, allocation-only
    /// wrapper over <see cref="INodeManagerLifecycle"/>, so they are verified
    /// against a mocked lifecycle rather than a live server.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable]
    public class RuntimeNodeSetLifecycleExtensionsTests
    {
        [Test]
        public void AddRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .AddRuntimeNodeSetAsync(null!, CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public void AddRuntimeNodeSetRejectsNullOptions()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .AddRuntimeNodeSetAsync(null!)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("options"));
        }

        [Test]
        public async Task AddRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .AddRuntimeNodeSetAsync(CreateOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.AddAsync(
                    It.IsAny<RuntimeNodeSetNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void ReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ReloadRuntimeNodeSetAsync(
                        null!,
                        CreateRegistration(),
                        CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public void ReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ReloadRuntimeNodeSetAsync(null!, CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("registration"));
        }

        [Test]
        public void ReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ReloadRuntimeNodeSetAsync(CreateRegistration(), null!)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
        }

        [Test]
        public async Task ReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .ReloadRuntimeNodeSetAsync(CreateRegistration(), CreateOptions())
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.ReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<RuntimeNodeSetNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void ShadowReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ShadowReloadRuntimeNodeSetAsync(
                        null!,
                        CreateRegistration(),
                        CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public void ShadowReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ShadowReloadRuntimeNodeSetAsync(null!, CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("registration"));
        }

        [Test]
        public void ShadowReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ShadowReloadRuntimeNodeSetAsync(CreateRegistration(), null!)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .ShadowReloadRuntimeNodeSetAsync(CreateRegistration(), CreateOptions())
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.ShadowReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<RuntimeNodeSetNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void ImmediateReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ImmediateReloadRuntimeNodeSetAsync(
                        null!,
                        CreateRegistration(),
                        CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public void ImmediateReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ImmediateReloadRuntimeNodeSetAsync(null!, CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("registration"));
        }

        [Test]
        public void ImmediateReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ImmediateReloadRuntimeNodeSetAsync(CreateRegistration(), null!)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .ImmediateReloadRuntimeNodeSetAsync(CreateRegistration(), CreateOptions())
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.ImmediateReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<RuntimeNodeSetNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static Mock<INodeManagerLifecycle> CreateLifecycle()
        {
            NodeManagerRegistration registration = CreateRegistration();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle
                .Setup(l => l.AddAsync(
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            lifecycle
                .Setup(l => l.ReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            lifecycle
                .Setup(l => l.ShadowReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            lifecycle
                .Setup(l => l.ImmediateReloadAsync(
                    It.IsAny<NodeManagerRegistration>(),
                    It.IsAny<IAsyncNodeManagerFactory>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerRegistration>(registration));
            return lifecycle;
        }

        private static RuntimeNodeSetOptions CreateOptions()
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "RuntimeNodeSetLifecycleExtensionsTests",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(Encoding.UTF8.GetBytes(kNodeSetXml))),
                        [kModelNamespaceUri])
                ]
            };
        }

        private static NodeManagerRegistration CreateRegistration()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .SetupGet(n => n.NamespaceUris)
                .Returns([kModelNamespaceUri]);
            return new NodeManagerRegistration(Guid.NewGuid(), 1, nodeManager.Object);
        }

        private const string kModelNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetExtensions";

        private const string kNodeSetXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:opcfoundation.org:Tests:RuntimeNodeSetExtensions</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:opcfoundation.org:Tests:RuntimeNodeSetExtensions"
                       Version="1.0.0" PublicationDate="2026-01-01T00:00:00Z" />
              </Models>
            </UANodeSet>
            """;
    }
}
