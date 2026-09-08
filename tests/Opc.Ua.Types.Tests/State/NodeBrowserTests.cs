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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests the iteration seam of <see cref="NodeBrowser"/>: the synchronous
    /// <see cref="NodeBrowser.Next"/> and the asynchronous
    /// <see cref="NodeBrowser.NextAsync"/> drain the same sequence, and a derived
    /// browser may take over either one.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeBrowserTests
    {
        private SystemContext m_context = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task NextAsyncDefaultsToTheSynchronousSequenceAsync()
        {
            IReference[] references = CreateReferences(3);

            using NodeBrowser syncBrowser = CreateBrowser(references);
            using NodeBrowser asyncBrowser = CreateBrowser(references);

            var viaNext = new List<IReference>();
            for (IReference? reference = syncBrowser.Next();
                reference != null;
                reference = syncBrowser.Next())
            {
                viaNext.Add(reference);
            }

            var viaNextAsync = new List<IReference>();
            for (IReference? reference = await asyncBrowser.NextAsync().ConfigureAwait(false);
                reference != null;
                reference = await asyncBrowser.NextAsync().ConfigureAwait(false))
            {
                viaNextAsync.Add(reference);
            }

            Assert.Multiple(() =>
            {
                Assert.That(viaNext, Is.EqualTo(references));
                Assert.That(viaNextAsync, Is.EqualTo(references));
            });
        }

        [Test]
        public void DefaultNextAsyncCompletesSynchronously()
        {
            using NodeBrowser browser = CreateBrowser(CreateReferences(1));

            ValueTask<IReference?> pending = browser.NextAsync();

            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            Assert.That(pending.Result, Is.Not.Null);
        }

        [Test]
        public async Task NextAsyncReturnsPushedReferenceFirstAsync()
        {
            IReference[] references = CreateReferences(2);
            using NodeBrowser browser = CreateBrowser(references);

            IReference? first = await browser.NextAsync().ConfigureAwait(false);
            Assert.That(first, Is.SameAs(references[0]));

            browser.Push(first!);

            IReference? again = await browser.NextAsync().ConfigureAwait(false);
            IReference? second = await browser.NextAsync().ConfigureAwait(false);
            IReference? end = await browser.NextAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.SameAs(references[0]));
                Assert.That(second, Is.SameAs(references[1]));
                Assert.That(end, Is.Null);
            });
        }

        [Test]
        public async Task NextAsyncObservesADerivedNextOverrideAsync()
        {
            IReference[] references = CreateReferences(1);
            var extra = new NodeStateReference(
                ReferenceTypeIds.HasComponent,
                false,
                new ExpandedNodeId("extra", 0));

            using var browser = new AppendingBrowser(m_context, references, extra);

            var seen = new List<IReference>();
            for (IReference? reference = await browser.NextAsync().ConfigureAwait(false);
                reference != null;
                reference = await browser.NextAsync().ConfigureAwait(false))
            {
                seen.Add(reference);
            }

            Assert.That(seen, Is.EqualTo(new[] { references[0], extra }));
        }

        [Test]
        public async Task ADerivedNextAsyncOverrideIsAwaitedAsync()
        {
            IReference[] references = CreateReferences(2);
            using var browser = new AsyncOnlyBrowser(m_context, references);

            var seen = new List<IReference>();
            for (IReference? reference = await browser.NextAsync().ConfigureAwait(false);
                reference != null;
                reference = await browser.NextAsync().ConfigureAwait(false))
            {
                seen.Add(reference);
            }

            Assert.Multiple(() =>
            {
                Assert.That(seen, Is.EqualTo(references));
                Assert.That(browser.NextAsyncCalls, Is.EqualTo(3));
                Assert.That(browser.NextCalls, Is.Zero);
            });
        }

        [Test]
        public void ADerivedNextAsyncOverrideHonorsCancellation()
        {
            using var browser = new AsyncOnlyBrowser(m_context, CreateReferences(1));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(
                async () => await browser.NextAsync(cts.Token).ConfigureAwait(false));
        }

        private NodeBrowser CreateBrowser(IEnumerable<IReference> references)
        {
            return new NodeBrowser(
                m_context,
                null,
                NodeId.Null,
                true,
                BrowseDirection.Both,
                QualifiedName.Null,
                references,
                false);
        }

        private static IReference[] CreateReferences(int count)
        {
            var references = new IReference[count];
            for (int i = 0; i < count; i++)
            {
                references[i] = new NodeStateReference(
                    ReferenceTypeIds.HasComponent,
                    false,
                    new ExpandedNodeId("target" + i, 0));
            }
            return references;
        }

        /// <summary>
        /// Overrides only <see cref="NodeBrowser.Next"/>, as every pre-2.0 browser does,
        /// appending one reference once the in-memory set is exhausted.
        /// </summary>
        private sealed class AppendingBrowser : NodeBrowser
        {
            private IReference? m_extra;

            public AppendingBrowser(
                ISystemContext context,
                IEnumerable<IReference> references,
                IReference extra)
                : base(context, null, NodeId.Null, true, BrowseDirection.Both,
                    QualifiedName.Null, references, false)
            {
                m_extra = extra;
            }

            public override IReference? Next()
            {
                IReference? reference = base.Next();
                if (reference != null)
                {
                    return reference;
                }

                reference = m_extra;
                m_extra = null;
                return reference;
            }
        }

        /// <summary>
        /// Produces its references only through <see cref="NodeBrowser.NextAsync"/>, with
        /// a real asynchronous hop per call, and counts how each member is driven.
        /// </summary>
        private sealed class AsyncOnlyBrowser : NodeBrowser
        {
            private readonly Queue<IReference> m_pending;

            public AsyncOnlyBrowser(ISystemContext context, IEnumerable<IReference> references)
                : base(context, null, NodeId.Null, true, BrowseDirection.Both,
                    QualifiedName.Null, null, false)
            {
                m_pending = new Queue<IReference>(references);
            }

            public int NextAsyncCalls { get; private set; }

            public int NextCalls { get; private set; }

            public override IReference? Next()
            {
                NextCalls++;
                return NextAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            public override async ValueTask<IReference?> NextAsync(
                CancellationToken cancellationToken = default)
            {
                NextAsyncCalls++;
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                return m_pending.Count > 0 ? m_pending.Dequeue() : null;
            }
        }
    }
}
