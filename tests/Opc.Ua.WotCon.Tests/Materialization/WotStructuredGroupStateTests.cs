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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises <see cref="WotStructuredGroupState"/> directly: a failed
    /// resolution is never cached and retries against the (possibly
    /// by-then-populated) factory, and concurrent first use resolves exactly
    /// once under the lock.
    /// </summary>
    [TestFixture]
    public sealed class WotStructuredGroupStateTests
    {
        [Test]
        public void EnsureResolvedTypeUnavailableFailsWithoutCachingThenSucceedsAfterRegistration()
        {
            var namespaceUris = new NamespaceTable();
            ushort ns = (ushort)namespaceUris.Append(TestStructureNamespace.Uri);
            IEncodeableFactory factory = ServiceMessageContext.CreateEmpty(null!).Factory;
            var dataTypeId = new NodeId(TestRootType.NumericId, ns);
            var targetNodeId = new NodeId("Struct", ns);

            var state = new WotStructuredGroupState(
                factory,
                namespaceUris,
                dataTypeId,
                targetNodeId,
                readSlots: [],
                writeSlots: []);

            WotStructuredGroupResolution first = state.EnsureResolved();
            Assert.That(first.Success, Is.False);
            Assert.That(first.Error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(first.RootType, Is.Null);

            // Simulate NodeManagerLifecycle.RefreshComplexTypesAsync completing
            // against the very same factory instance.
            factory.Builder.AddEncodeableType(TestRootType.EncodingId, new TestRootType()).Commit();

            WotStructuredGroupResolution second = state.EnsureResolved();
            Assert.That(
                second.Success, Is.True, "The failed attempt must not have been cached; a retry must resolve now.");
            Assert.That(second.RootType, Is.Not.Null);
        }

        [Test]
        public void EnsureResolvedConcurrentFirstCallsResolveExactlyOnceAndAllSucceed()
        {
            var namespaceUris = new NamespaceTable();
            ushort ns = (ushort)namespaceUris.Append(TestStructureNamespace.Uri);
            IEncodeableFactory inner = ServiceMessageContext.CreateEmpty(null!).Factory;
            inner.Builder.AddEncodeableType(TestRootType.EncodingId, new TestRootType()).Commit();
            var counting = new CountingEncodeableFactory(inner);
            var dataTypeId = new NodeId(TestRootType.NumericId, ns);
            var targetNodeId = new NodeId("Struct", ns);

            var state = new WotStructuredGroupState(
                counting,
                namespaceUris,
                dataTypeId,
                targetNodeId,
                readSlots: [],
                writeSlots: []);

            const int callerCount = 8;
            var barrier = new Barrier(callerCount);
            var tasks = new Task<WotStructuredGroupResolution>[callerCount];
            for (int i = 0; i < callerCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    return state.EnsureResolved();
                });
            }
            Task.WaitAll(tasks);

            foreach (Task<WotStructuredGroupResolution> task in tasks)
            {
                Assert.That(task.Result.Success, Is.True);
            }
            Assert.That(counting.TryGetEncodeableTypeCallCount, Is.EqualTo(1),
                "Concurrent first use must resolve exactly once under the lock, not once per caller.");
        }

        /// <summary>
        /// A minimal <see cref="IEncodeableFactory"/> decorator that forwards
        /// every lookup to an inner factory while counting
        /// <see cref="TryGetEncodeableType"/> calls, so a test can assert how
        /// many times resolution actually ran.
        /// </summary>
        private sealed class CountingEncodeableFactory : IEncodeableFactory
        {
            public CountingEncodeableFactory(IEncodeableFactory inner)
            {
                m_inner = inner;
            }

            public IEnumerable<ExpandedNodeId> KnownTypeIds => m_inner.KnownTypeIds;

            public IEncodeableFactoryBuilder Builder => m_inner.Builder;

            public int TryGetEncodeableTypeCallCount => m_count;

            public bool TryGetEncodeableType(
                ExpandedNodeId typeId,
                [NotNullWhen(true)] out IEncodeableType? encodeableType)
            {
                Interlocked.Increment(ref m_count);
                return m_inner.TryGetEncodeableType(typeId, out encodeableType);
            }

            public bool TryGetEnumeratedType(
                ExpandedNodeId typeId,
                [NotNullWhen(true)] out IEnumeratedType? enumeratedType)
            {
                return m_inner.TryGetEnumeratedType(typeId, out enumeratedType);
            }

            public bool TryGetType(
                XmlQualifiedName xmlName,
                [NotNullWhen(true)] out IType? type)
            {
                return m_inner.TryGetType(xmlName, out type);
            }

            private readonly IEncodeableFactory m_inner;
            private int m_count;
        }
    }
}
