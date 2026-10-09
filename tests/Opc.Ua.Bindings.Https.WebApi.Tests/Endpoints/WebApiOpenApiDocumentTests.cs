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

#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi.Endpoints;
using Opc.Ua.Schema;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Endpoints
{
    /// <summary>
    /// Tests of the cache of the OpenAPI document: a document is generated
    /// once, by one request at a time, and the cache stays within its bound
    /// however many requests miss it at the same time.
    /// </summary>
    [TestFixture]
    [Category("WebApiOpenApiDocument")]
    [Parallelizable]
    public class WebApiOpenApiDocumentTests
    {
        private const int kThreads = 8;
        private const int kCachedDocuments = 16;

        [Test]
        public async Task ConcurrentFirstRequestsGenerateTheDocumentOnceAsync()
        {
            int generationCalls = CountResolverCallsOfOneGeneration();
            var resolver = new CountingResolver();
            var generator = new WebApiOpenApiGenerator(resolver);
            var document = new WebApiOpenApiDocument(WebApiServiceSet.Sessionless, includeSchemas: true);

            ByteString[] results = await RunConcurrentlyAsync(
                kThreads,
                _ => document.Get(generator, "/opcua/")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolver.Calls, Is.EqualTo(generationCalls), "generations");
                Assert.That(resolver.MaxConcurrentCalls, Is.EqualTo(1), "concurrent resolver calls");
                Assert.That(results.Select(r => r.Length).Distinct().Count(), Is.EqualTo(1));
                Assert.That(results.All(r => r.Length > 0), Is.True);
            });
        }

        [Test]
        public async Task ConcurrentMissesOfDifferentUrlsDoNotGrowTheCachePastItsBoundAsync()
        {
            int generationCalls = CountResolverCallsOfOneGeneration();
            const int urls = kCachedDocuments + 4;
            var resolver = new CountingResolver();
            var generator = new WebApiOpenApiGenerator(resolver);
            var document = new WebApiOpenApiDocument(WebApiServiceSet.Sessionless, includeSchemas: true);

            await RunConcurrentlyAsync(urls, i => document.Get(generator, "/path" + i + "/")).ConfigureAwait(false);
            Assert.That(resolver.Calls, Is.EqualTo(urls * generationCalls), "first generations");

            // A second round regenerates exactly the documents that were not kept.
            resolver.Reset();
            for (int i = 0; i < urls; i++)
            {
                document.Get(generator, "/path" + i + "/");
            }

            Assert.That(resolver.Calls / generationCalls, Is.EqualTo(urls - kCachedDocuments), "regenerated");
            Assert.That(resolver.Calls % generationCalls, Is.Zero);
        }

        [Test]
        public void ARepeatedRequestForTheSameUrlIsServedFromTheCache()
        {
            var resolver = new CountingResolver();
            var generator = new WebApiOpenApiGenerator(resolver);
            var document = new WebApiOpenApiDocument(WebApiServiceSet.Sessionless, includeSchemas: true);

            ByteString first = document.Get(generator, null);
            int calls = resolver.Calls;
            ByteString second = document.Get(generator, null);

            Assert.That(resolver.Calls, Is.EqualTo(calls));
            Assert.That(second.Memory.ToArray(), Is.EqualTo(first.Memory.ToArray()));
        }

        private static int CountResolverCallsOfOneGeneration()
        {
            var resolver = new CountingResolver();
            new WebApiOpenApiDocument(WebApiServiceSet.Sessionless, includeSchemas: true)
                .Get(new WebApiOpenApiGenerator(resolver), null);
            Assert.That(resolver.Calls, Is.GreaterThan(0));
            return resolver.Calls;
        }

        private static async Task<ByteString[]> RunConcurrentlyAsync(int count, Func<int, ByteString> action)
        {
            using var start = new Barrier(count);
            Task<ByteString>[] tasks = [.. Enumerable.Range(0, count).Select(i => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait();
                    return action(i);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))];
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the standard types and counts the calls, and how many
        /// run at the same time, to show how often and how the generator
        /// runs.
        /// </summary>
        private sealed class CountingResolver : IDataTypeDefinitionResolver
        {
            public int Calls => Volatile.Read(ref m_calls);

            public int MaxConcurrentCalls => Volatile.Read(ref m_maxActive);

            public void Reset()
            {
                Interlocked.Exchange(ref m_calls, 0);
            }

            public bool TryResolve(ExpandedNodeId typeId, [NotNullWhen(true)] out UaTypeDescription? description)
            {
                Enter();
                try
                {
                    return m_standard.TryResolve(typeId, out description);
                }
                finally
                {
                    Interlocked.Decrement(ref m_active);
                }
            }

            public bool TryResolve(NodeId typeId, [NotNullWhen(true)] out UaTypeDescription? description)
            {
                Enter();
                try
                {
                    return m_standard.TryResolve(typeId, out description);
                }
                finally
                {
                    Interlocked.Decrement(ref m_active);
                }
            }

            public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
            {
                return m_standard.GetNamespaceTypes(namespaceUri);
            }

            private void Enter()
            {
                Interlocked.Increment(ref m_calls);
                int active = Interlocked.Increment(ref m_active);
                int seen;
                while (active > (seen = Volatile.Read(ref m_maxActive)) &&
                    Interlocked.CompareExchange(ref m_maxActive, active, seen) != seen)
                {
                }

                // Give a request that runs at the same time a chance to overlap.
                Thread.Sleep(1);
            }

            private readonly EncodeableFactoryDefinitionSource m_standard =
                new(EncodeableFactory.Create(), new NamespaceTable());

            private int m_calls;
            private int m_active;
            private int m_maxActive;
        }
    }
}

#endif
