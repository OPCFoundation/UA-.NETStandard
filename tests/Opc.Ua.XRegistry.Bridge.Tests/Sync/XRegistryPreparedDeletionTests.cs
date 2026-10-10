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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistryPreparedDeletionTests
    {
        [TestCase(false, "/groups/g/resources/r")]
        [TestCase(true, "/groups/g/resources/r")]
        [TestCase(false, "/groups/g")]
        [TestCase(true, "/groups/g")]
        [TestCase(false, "/groups/g/resources/r/versions/b")]
        [TestCase(true, "/groups/g/resources/r/versions/b")]
        public async Task PreparedDestinationSafelyPropagatesDeletionWithoutEchoAsync(bool nativeDestination,
            string path)
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination);
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            XRegistrySynchronizer engine = Engine(source, prepared, state, nativeDestination);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded),
                XRegistrySyncFixture.Details(baseline));
            await ExecuteAsync(source, XRegistryAction.Delete, path).ConfigureAwait(false);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            string observedPath = path.EndsWith("/r", StringComparison.Ordinal) ? path + "/meta" : path;
            XRegistryResponse absent = await ExecuteAsync(destination, XRegistryAction.Read, observedPath)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded),
                    XRegistrySyncFixture.Details(report));
                Assert.That(absent.StatusCode, Is.EqualTo(404));
                Assert.That(prepared.Prepared, Is.GreaterThan(0));
                Assert.That(prepared.Commits, Is.GreaterThan(0));
            });
            int committed = prepared.Commits;
            XRegistrySyncReport next = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(next.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), XRegistrySyncFixture.Details(next));
                Assert.That(next.Applied + next.Deleted, Is.Zero);
                Assert.That(prepared.Commits, Is.EqualTo(committed));
            });
        }

        [TestCase("/groups/g/resources/r/versions/a", /*lang=json,strict*/ """{"name":"newer"}""")]
        [TestCase("/groups/g/resources/r/versions/new", /*lang=json,strict*/ """{"name":"added"}""")]
        [TestCase("/groups/g/resources/r/meta", /*lang=json,strict*/ """{"defaultversionid":"a"}""")]
        public async Task DestinationChangeAfterPreparationAbortsWithoutDeletingAsync(string changedPath, string body)
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination);
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            XRegistrySynchronizer engine = Engine(source, prepared, state, false);
            await engine.RunOnceAsync().ConfigureAwait(false);
            await ExecuteAsync(source, XRegistryAction.Delete, "/groups/g/resources/r").ConfigureAwait(false);
            prepared.AfterPrepareAsync = () => ExecuteAsync(destination, XRegistryAction.Merge, changedPath, body);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse retained = await ExecuteAsync(destination, XRegistryAction.Read, changedPath)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Prepared, Is.GreaterThan(0));
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(retained.StatusCode, Is.EqualTo(200));
            });
        }

        [Test]
        public async Task GlobalChangeBetweenVerifiedInventoryAndCommitRejectsWithoutDeletingAsync()
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination);
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            XRegistrySynchronizer engine = Engine(source, prepared, state, false);
            await engine.RunOnceAsync().ConfigureAwait(false);
            await ExecuteAsync(source, XRegistryAction.Delete, "/groups/g/resources/r").ConfigureAwait(false);
            prepared.BeforeCommitAsync = () => ExecuteAsync(destination, XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"changed"}""");

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse retained = await ExecuteAsync(destination, XRegistryAction.Read,
                "/groups/g/resources/r/meta")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Commits, Is.EqualTo(1));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(retained.StatusCode, Is.EqualTo(200));
                Assert.That(report.Deleted, Is.Zero);
            });
        }

        [Test]
        public async Task LostPreparedCommitResponseUsesReadbackWithoutRepeatingTheDeletionAsync()
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination) { LoseCommitResponse = true };
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            XRegistrySynchronizer engine = Engine(source, prepared, state, false);
            await engine.RunOnceAsync().ConfigureAwait(false);
            await ExecuteAsync(source, XRegistryAction.Delete, "/groups/g/resources/r").ConfigureAwait(false);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport resumed = await Engine(source, prepared, state,
                false).RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded),
                    XRegistrySyncFixture.Details(report));
                Assert.That(resumed.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded),
                    XRegistrySyncFixture.Details(resumed));
                Assert.That(prepared.Commits, Is.EqualTo(1));
                Assert.That(resumed.Applied + resumed.Deleted, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DryRunNeverPreparesOrCommitsDeletionAsync(bool nativeDestination)
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination);
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            XRegistrySynchronizer engine = Engine(source, prepared, state, nativeDestination);
            await engine.RunOnceAsync().ConfigureAwait(false);
            await ExecuteAsync(source, XRegistryAction.Delete, "/groups/g/resources/r").ConfigureAwait(false);

            XRegistrySyncReport report = await engine.RunOnceAsync(dryRun: true).ConfigureAwait(false);
            XRegistryResponse retained = await ExecuteAsync(destination, XRegistryAction.Read,
                "/groups/g/resources/r/meta")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Prepared, Is.Zero);
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(report.Planned, Is.EqualTo(1));
                Assert.That(retained.StatusCode, Is.EqualTo(200));
            });
        }

        [Test]
        public async Task PreparationDeadlineDoesNotCommitAndReleasesALateLeaseAsync()
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination);
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            var clock = new SyncClock();
            XRegistrySynchronizer engine = Engine(source, prepared, state, false, clock);
            await engine.RunOnceAsync().ConfigureAwait(false);
            await ExecuteAsync(source, XRegistryAction.Delete, "/groups/g/resources/r").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delayed =
                new TaskCompletionSource<XRegistryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            prepared.AfterPrepareAsync = () =>
            {
                entered.TrySetResult(true);
                return delayed.Task;
            };
            Task<XRegistrySyncReport> pending = engine.RunOnceAsync().AsTask();
            await entered.Task.ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(30));
            XRegistrySyncReport report;
            try
            {
                report = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                delayed.TrySetResult(new XRegistryResponse(200));
            }
            await prepared.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            XRegistryResponse retained = await ExecuteAsync(destination, XRegistryAction.Read,
                "/groups/g/resources/r/meta")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(report.Pending, Is.GreaterThan(0));
                Assert.That(retained.StatusCode, Is.EqualTo(200));
            });
        }

        [Test]
        public async Task CommitDeadlineRetainsLeaseUntilTheLateResponseCompletesAsync()
        {
            using XRegistryTransactionalEndpoint source = Endpoint("source");
            using XRegistryTransactionalEndpoint destination = Endpoint("destination");
            var prepared = new ObservedPreparedEndpoint(destination);
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            await SeedAsync(source).ConfigureAwait(false);
            await SeedAsync(destination).ConfigureAwait(false);
            var clock = new SyncClock();
            XRegistrySynchronizer engine = Engine(source, prepared, state, false, clock);
            await engine.RunOnceAsync().ConfigureAwait(false);
            await ExecuteAsync(source, XRegistryAction.Delete, "/groups/g/resources/r").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            prepared.AfterCommitAsync = () =>
            {
                entered.TrySetResult(true);
                return release.Task;
            };
            Task<XRegistrySyncReport> pending = engine.RunOnceAsync().AsTask();
            await entered.Task.ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(30));
            try
            {
                XRegistrySyncReport report = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded),
                        XRegistrySyncFixture.Details(report));
                    Assert.That(prepared.Commits, Is.EqualTo(1));
                    Assert.That(prepared.Disposed.Task.IsCompleted, Is.False);
                });
            }
            finally
            {
                release.TrySetResult(true);
            }
            await prepared.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            XRegistrySyncReport resumed = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resumed.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded),
                    XRegistrySyncFixture.Details(resumed));
                Assert.That(prepared.Commits, Is.EqualTo(1));
            });
        }

        private static XRegistrySynchronizer Engine(
            IXRegistryEndpoint source, IXRegistryEndpoint destination, IXRegistrySyncStateStore state,
            bool nativeDestination, TimeProvider? clock = null)
        {
            return new XRegistrySynchronizer(
                nativeDestination ? destination : source, nativeDestination ? source : destination,
                state, new XRegistrySyncOptions("prepared-test", "native", "http")
                {
                    OpcUaContext = XRegistrySyncFixture.Writer,
                    HttpContext = XRegistrySyncFixture.Writer
                }, NUnitTelemetryContext.Create(), clock);
        }

        private static XRegistryTransactionalEndpoint Endpoint(string id)
        {
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = id,
                Model = XRegistrySyncFixture.Json(/*lang=json,strict*/ """
                    {"groups":{"groups":{"singular":"group","resources":{"resources":{
                    "singular":"resource","hasdocument":false}}}}}
                    """)
            }, new InMemoryXRegistryTransactionStore());
        }

        private static Task<XRegistryResponse> SeedAsync(IXRegistryEndpoint endpoint)
        {
            return ExecuteAsync(endpoint, XRegistryAction.Replace, "/groups/g/resources/r",
                                     /*lang=json,strict*/
                                     """{"versions":{"a":{"name":"first"},"b":{"name":"second"}}}""");
        }

        private static Task<XRegistryResponse> ExecuteAsync(
            IXRegistryEndpoint endpoint, XRegistryAction action, string path, string? body = null)
        {
            return endpoint.ExecuteAsync(new XRegistryRequest(action, path)
            {
                Context = XRegistrySyncFixture.Writer,
                View = XRegistryView.Metadata,
                Metadata = body is null ? default : XRegistrySyncFixture.Json(body)
            }).AsTask();
        }

        private sealed class ObservedPreparedEndpoint(XRegistryTransactionalEndpoint inner) : IXRegistryPreparedEndpoint
        {
            public Func<Task<XRegistryResponse>>? AfterPrepareAsync { get; set; }

            public Func<Task<XRegistryResponse>>? BeforeCommitAsync { get; set; }

            public Func<Task>? AfterCommitAsync { get; set; }

            public bool LoseCommitResponse { get; set; }

            public int Prepared { get; private set; }

            public int Commits { get; private set; }

            public TaskCompletionSource<bool> Disposed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<XRegistryEndpointDescription> InspectAsync(
                XRegistryCallContext context, CancellationToken cancellationToken = default)
            {
                return inner.InspectAsync(context, cancellationToken);
            }

            public ValueTask<XRegistryResponse> ExecuteAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                return inner.ExecuteAsync(request, cancellationToken);
            }

            public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                IXRegistryPreparedOperation operation = await inner.PrepareAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                Prepared++;
                if (AfterPrepareAsync is not null)
                {
                    Func<Task<XRegistryResponse>> update = AfterPrepareAsync;
                    AfterPrepareAsync = null;
                    await update().ConfigureAwait(false);
                }
                return new ObservedOperation(operation, this);
            }

            private sealed class ObservedOperation(
                IXRegistryPreparedOperation innerOperation,
                    ObservedPreparedEndpoint owner) : IXRegistryPreparedOperation
            {
                public XRegistryResponse Response => innerOperation.Response;

                public async ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
                {
                    owner.Commits++;
                    if (owner.BeforeCommitAsync is not null)
                    {
                        Func<Task<XRegistryResponse>> update = owner.BeforeCommitAsync;
                        owner.BeforeCommitAsync = null;
                        await update().ConfigureAwait(false);
                    }
                    XRegistryResponse response =
                        await innerOperation.CommitAsync(cancellationToken).ConfigureAwait(false);
                    if (owner.AfterCommitAsync is not null)
                    {
                        await owner.AfterCommitAsync().ConfigureAwait(false);
                    }
                    if (owner.LoseCommitResponse)
                    {
                        owner.LoseCommitResponse = false;
                        throw new IOException("Simulated lost prepared commit response.");
                    }
                    return response;
                }

                public async ValueTask DisposeAsync()
                {
                    await innerOperation.DisposeAsync().ConfigureAwait(false);
                    owner.Disposed.TrySetResult(true);
                }
            }
        }
    }
}
