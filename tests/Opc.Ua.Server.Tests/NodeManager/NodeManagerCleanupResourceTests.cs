/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies the exact resource representations used by trusted cleanup verification.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerCleanupResourceTests
    {
        /// <summary>
        /// The default value cannot resolve either kind of resource.
        /// </summary>
        [Test]
        public void DefaultResourceHasNoValue()
        {
            NodeManagerCleanupResource resource = default;

            Assert.That(resource.TryGetDisposable(out _), Is.False);
            Assert.That(resource.TryGetAsyncDisposable(out _), Is.False);
        }

        /// <summary>
        /// A synchronous resource is returned by exact reference and not as an asynchronous resource.
        /// </summary>
        [Test]
        public void SynchronousResourcePreservesExactReference()
        {
            IDisposable original = Mock.Of<IDisposable>();
            NodeManagerCleanupResource resource = NodeManagerCleanupResource.FromDisposable(original);

            Assert.That(resource.TryGetDisposable(out IDisposable? resolved), Is.True);
            Assert.That(resolved, Is.SameAs(original));
            Assert.That(resource.TryGetAsyncDisposable(out _), Is.False);
        }

        /// <summary>
        /// An asynchronous resource is returned by exact reference and not as a synchronous resource.
        /// </summary>
        [Test]
        public void AsynchronousResourcePreservesExactReference()
        {
            IAsyncDisposable original = Mock.Of<IAsyncDisposable>();
            NodeManagerCleanupResource resource = NodeManagerCleanupResource.FromAsyncDisposable(original);

            Assert.That(resource.TryGetAsyncDisposable(out IAsyncDisposable? resolved), Is.True);
            Assert.That(resolved, Is.SameAs(original));
            Assert.That(resource.TryGetDisposable(out _), Is.False);
        }

        /// <summary>
        /// The representation records the disposal path even if the resource supports both interfaces.
        /// </summary>
        [Test]
        public void DualInterfaceResourceUsesChosenDisposalPath()
        {
            var original = new Mock<IDisposable>();
            Mock<IAsyncDisposable> asynchronous = original.As<IAsyncDisposable>();
            NodeManagerCleanupResource synchronous = NodeManagerCleanupResource.FromDisposable(original.Object);
            NodeManagerCleanupResource asyncValue =
                NodeManagerCleanupResource.FromAsyncDisposable(asynchronous.Object);

            Assert.That(synchronous.TryGetDisposable(out IDisposable? syncReference), Is.True);
            Assert.That(syncReference, Is.SameAs(original.Object));
            Assert.That(synchronous.TryGetAsyncDisposable(out _), Is.False);
            Assert.That(asyncValue.TryGetAsyncDisposable(out IAsyncDisposable? asyncReference), Is.True);
            Assert.That(asyncReference, Is.SameAs(original.Object));
            Assert.That(asyncValue.TryGetDisposable(out _), Is.False);
        }

        /// <summary>
        /// Construction rejects a missing resource rather than creating an empty verification value.
        /// </summary>
        [Test]
        public void ResourceFactoriesRejectNull()
        {
            Assert.That(
                () => NodeManagerCleanupResource.FromDisposable(null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => NodeManagerCleanupResource.FromAsyncDisposable(null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
