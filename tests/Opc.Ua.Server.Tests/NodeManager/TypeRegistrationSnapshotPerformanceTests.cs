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
 *
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Measures lifecycle revision capture and verifies concurrent snapshot isolation.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public sealed class TypeRegistrationSnapshotPerformanceTests
    {
        /// <summary>
        /// Reports lifecycle revision capture and concurrent candidate-preparation costs.
        /// </summary>
        /// <param name="typeCount">The number of registered types in the serving model.</param>
        [TestCase(128)]
        [TestCase(2048)]
        [TestCase(16384)]
        [Explicit("Run alone on a quiet host to compare lifecycle revision scaling.")]
        public async Task CaptureRevisionAndConcurrentPreparationReportTimeAndAllocationAsync(int typeCount)
        {
            TypeTable types = CreateTypeTable(typeCount);
            (LifecycleWriter writer, Mock<IServerInternal> server, Mock<IDynamicNodeManagerHost> host) =
                CreateRevisionCapture(types);
            using (writer)
            {
                _ = writer.CaptureRevision(server.Object, host.Object);
                _ = await PrepareCandidateAsync(server.Object, (uint)(typeCount + 100_000)).ConfigureAwait(false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                const int captureCount = 5;
                var revisions = new LifecycleWriter.PublicationRevision[captureCount];
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                for (int ii = 0; ii < captureCount; ii++)
                {
                    revisions[ii] = writer.CaptureRevision(server.Object, host.Object);
                }
                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                TestContext.Out.WriteLine(
                    $"Types: {typeCount}; lifecycle revision captures: {captureCount}; " +
                    $"elapsed: {stopwatch.Elapsed.TotalMilliseconds:F3} ms; allocated: {allocated} bytes; " +
                    $"per capture: {allocated / captureCount} bytes.");
                Assert.That(revisions, Has.All.Not.Null);

                const int preparationCount = 4;
                var preparations = new (
                    ChangePreparer Preparation,
                    IAsyncNodeManager Manager,
                    ChangePreparer.StagingServer StagingServer)[preparationCount];
                var preparationAllocations = new long[preparations.Length];
                var preparationStopwatch = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, preparations.Length).Select(index => Task.Run(async () =>
                {
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    preparations[index] = await PrepareCandidateAsync(
                        server.Object, (uint)(typeCount + 200_000 + index)).ConfigureAwait(false);
                    preparationAllocations[index] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                }))).ConfigureAwait(false);
                preparationStopwatch.Stop();
                TestContext.Out.WriteLine(
                    $"Types: {typeCount}; concurrent candidate preparations with first type write: " +
                    $"{preparations.Length}; " +
                    $"elapsed: {preparationStopwatch.Elapsed.TotalMilliseconds:F3} ms; " +
                    $"worker allocations: {preparationAllocations.Sum()} bytes.");
            }
        }

        /// <summary>
        /// Verifies simultaneous lifecycle revision captures record the same serving source.
        /// </summary>
        [Test]
        public async Task ConcurrentRevisionCapturesObserveStableSourceAsync()
        {
            const int captureCount = 8;
            TypeTable types = CreateTypeTable(128);
            (LifecycleWriter writer, Mock<IServerInternal> server, Mock<IDynamicNodeManagerHost> host) =
                CreateRevisionCapture(types);
            using (writer)
            {
                LifecycleWriter.PublicationRevision[] revisions = await Task.WhenAll(
                    Enumerable.Range(0, captureCount)
                        .Select(_ => Task.Run(() => writer.CaptureRevision(server.Object, host.Object))))
                    .ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    for (int ii = 0; ii < revisions.Length; ii++)
                    {
                        Assert.That(revisions[ii].Types, Is.SameAs(types));
                        Assert.That(revisions[ii].TypeRevision, Is.EqualTo(revisions[0].TypeRevision));
                        Assert.That(revisions[ii].Factory, Is.SameAs(revisions[0].Factory));
                        Assert.That(revisions[ii].FactoryRevision, Is.EqualTo(revisions[0].FactoryRevision));
                        Assert.That(revisions[ii].RoutingRevision, Is.SameAs(revisions[0].RoutingRevision));
                    }
                }
            }
        }

        /// <summary>
        /// Verifies simultaneous candidate preparation keeps type-table writes private.
        /// </summary>
        [Test]
        public async Task ConcurrentPreparersKeepCandidateTypeWritesIsolatedAsync()
        {
            const int preparationCount = 8;
            TypeTable types = CreateTypeTable(128);
            var (writer, server, _) = CreateRevisionCapture(types);
            using (writer)
            {
                var preparations = await Task.WhenAll(Enumerable.Range(0, preparationCount)
                    .Select(index => Task.Run(() => PrepareCandidateAsync(
                        server.Object, (uint)(10_000 + index)))))
                    .ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    for (int ii = 0; ii < preparationCount; ii++)
                    {
                        Assert.That(
                            preparations[ii].StagingServer.TypeTree.IsKnown(new NodeId((uint)(10_000 + ii))),
                            Is.True);
                        for (int jj = 0; jj < preparationCount; jj++)
                        {
                            if (ii != jj)
                            {
                                Assert.That(
                                    preparations[ii].StagingServer.TypeTree.IsKnown(
                                        new NodeId((uint)(10_000 + jj))),
                                    Is.False);
                            }
                        }
                    }
                    Assert.That(types.IsKnown(new NodeId(10_000)), Is.False);
                }
            }
        }

        private static async Task<(
            ChangePreparer Preparation,
            IAsyncNodeManager Manager,
            ChangePreparer.StagingServer StagingServer)> PrepareCandidateAsync(
                IServerInternal server,
                uint typeId)
        {
            var preparation = new ChangePreparer(server);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    candidate.TypeTree.AddSubtype(new NodeId(typeId), NodeId.Null);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            _ = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, static _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer stagingServer = ChangePreparer.GetServer(manager)!;
            return (preparation, manager, stagingServer);
        }

        private static TypeTable CreateTypeTable(int typeCount)
        {
            var types = new TypeTable(new NamespaceTable());
            for (int ii = 1; ii <= typeCount; ii++)
            {
                types.AddSubtype(new NodeId((uint)ii), NodeId.Null);
            }
            return types;
        }

        private static (
            LifecycleWriter Writer,
            Mock<IServerInternal> Server,
            Mock<IDynamicNodeManagerHost> Host) CreateRevisionCapture(TypeTable types)
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            var serverUris = new StringTable();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            EncodeableFactory factory = (EncodeableFactory)EncodeableFactory.Create();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.SetupGet(value => value.TypeTree).Returns(types);
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MessageContext).Returns(new ServiceMessageContext(telemetry, factory)
            {
                NamespaceUris = namespaces,
                ServerUris = serverUris
            });
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(server.Object));
            var routing = new NodeManagerRoutingTable();
            NodeManagerRoutingTable.RoutingSnapshot routingRevision = routing.Revision;
            var host = new Mock<IDynamicNodeManagerHost>();
            host.SetupGet(value => value.RoutingRevision).Returns(routingRevision);
            return (new LifecycleWriter(), server, host);
        }
    }
}
