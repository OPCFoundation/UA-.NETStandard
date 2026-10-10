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
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Tests.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests
{
    /// <summary>
    /// Exercises caller lease ownership at dispatch, candidate, journal, subscription and cleanup boundaries.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryScopedEndpointBoundaryTests
    {
        /// <summary>
        /// Rejects each mismatched caller-scope component before contacting the selected endpoint.
        /// </summary>
        [TestCase("subject")]
        [TestCase("authority")]
        [TestCase("session")]
        [TestCase("roles")]
        [TestCase("authentication")]
        public async Task ResolverScopeMismatchReleasesLeaseWithoutDispatchAsync(string mismatch)
        {
            await using var fixture = new XRegistrySyncFixture();
            var backend = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            XRegistryCallContext caller = Writer with { Authority = "issuer", SessionId = "session-one" };
            XRegistryCallContext other = mismatch switch
            {
                "subject" => new XRegistryCallContext("other")
                {
                    Authority = "issuer",
                    SessionId = "session-one",
                    IsAuthenticated = true,
                    Roles = ["xregistry.write"]
                },
                "authority" => caller with { Authority = "other-issuer" },
                "session" => caller with { SessionId = "session-two" },
                "roles" => caller with { Roles = ["xregistry.read"] },
                _ => caller with { IsAuthenticated = false }
            };
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((_, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend.Object, other, () =>
                {
                    releases++;
                    return default;
                })));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);

            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await endpoint.InspectAsync(caller).ConfigureAwait(false));

            Assert.That(releases, Is.EqualTo(1));
            backend.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A resolver cannot revive an already released lease by returning it again.
        /// </summary>
        [Test]
        public async Task DisposedResolverLeaseIsRejectedWithoutASecondReleaseAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var backend = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            int releases = 0;
            var lease = new XRegistryEndpointLease(backend.Object, Writer, () =>
            {
                releases++;
                return default;
            });
            await lease.DisposeAsync().ConfigureAwait(false);
            var resolver = new XRegistryEndpointResolver((_, _) => new ValueTask<IXRegistryEndpointLease>(lease));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read)).ConfigureAwait(false));

            Assert.That(releases, Is.EqualTo(1));
            backend.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Candidate inspection and reads revalidate both signaled and dynamically denied credentials.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RevokedCandidatesCannotBeInspectedReadOrCommittedAsync(bool signal)
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint provider = Provider();
            using var revoked = new CancellationTokenSource();
            bool authorized = true;
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(provider, caller, () =>
                {
                    releases++;
                    return default;
                }, _ => new ValueTask<bool>(authorized), revoked.Token)));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            IXRegistryPreparedOperation operation =
                await endpoint.PrepareAsync(Request(XRegistryAction.Merge)).ConfigureAwait(false);
            await using (operation.ConfigureAwait(false))
            {
                Assert.That(operation, Is.InstanceOf<IXRegistryPreparedSnapshot>());
                var snapshot = (IXRegistryPreparedSnapshot)operation;
                XRegistryEndpointDescription candidate = await snapshot.InspectCandidateAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(candidate.RegistryId, Is.EqualTo("scoped-boundary"));
                    Assert.That(snapshot.HasCandidate, Is.True);
                    Assert.That(releases, Is.Zero);
                });

                authorized = false;
                if (signal)
                {
                    await CancelAsync(revoked).ConfigureAwait(false);
                    Assert.That(snapshot.HasCandidate, Is.False);
                }
                Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await snapshot.InspectCandidateAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await snapshot.ReadCandidateAsync(Request(XRegistryAction.Read)).ConfigureAwait(false));
                Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await operation.CommitAsync().ConfigureAwait(false));
                Assert.That(snapshot.HasCandidate, Is.False);
            }
            await operation.DisposeAsync().ConfigureAwait(false);
            XRegistryResponse root = await provider.ExecuteAsync(Request(XRegistryAction.Read)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(releases, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// A consumed candidate cannot be read or committed again and releasing it is idempotent.
        /// </summary>
        [Test]
        public async Task CommittedAndDisposedCandidatesCannotBeReusedAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint provider = Provider();
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(provider, caller, () =>
                {
                    releases++;
                    return default;
                })));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            IXRegistryPreparedOperation operation =
                await endpoint.PrepareAsync(Request(XRegistryAction.Merge)).ConfigureAwait(false);
            var snapshot = (IXRegistryPreparedSnapshot)operation;
            await using (operation.ConfigureAwait(false))
            {
                XRegistryResponse committed = await operation.CommitAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(committed.StatusCode, Is.EqualTo(200));
                    Assert.That(snapshot.HasCandidate, Is.False);
                });
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await operation.CommitAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await snapshot.InspectCandidateAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await snapshot.ReadCandidateAsync(Request(XRegistryAction.Read)).ConfigureAwait(false));
            }
            await operation.DisposeAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await snapshot.InspectCandidateAsync().ConfigureAwait(false));
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await snapshot.ReadCandidateAsync(Request(XRegistryAction.Read)).ConfigureAwait(false));
            XRegistryResponse root = await provider.ExecuteAsync(Request(XRegistryAction.Read)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(releases, Is.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("name").GetString(), Is.EqualTo("candidate"));
            });
        }

        /// <summary>
        /// Ordinary preparation does not promise candidate snapshots when the selected endpoint has none.
        /// </summary>
        [Test]
        public async Task NonSnapshotPreparationRejectsCandidateReadsButRetainsItsCommitAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var operation = new Mock<IXRegistryPreparedOperation>(MockBehavior.Strict);
            var response = new XRegistryResponse(200);
            operation.SetupGet(value => value.Response).Returns(response);
            operation.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response);
            operation.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var backend = new Mock<IXRegistryPreparedEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.PrepareAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation.Object);
            var endpoint = new XRegistryScopedEndpoint(
                XRegistryEndpointResolver.Borrow(backend.Object), fixture.Telemetry);

            IXRegistryPreparedOperation prepared =
                await endpoint.PrepareAsync(Request(XRegistryAction.Merge)).ConfigureAwait(false);
            await using (prepared.ConfigureAwait(false))
            {
                var candidate = (IXRegistryPreparedSnapshot)prepared;
                Assert.That(candidate.HasCandidate, Is.False);
                Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await candidate.InspectCandidateAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await candidate.ReadCandidateAsync(Request(XRegistryAction.Read)).ConfigureAwait(false));
                Assert.That(await prepared.CommitAsync().ConfigureAwait(false), Is.SameAs(response));
            }
            operation.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Journal lookup forwards the trusted scope and releases the acquisition without repeating a mutation.
        /// </summary>
        [Test]
        public async Task JournalLookupForwardsCallerAndReleasesExactlyOnceAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var backend = new Mock<IXRegistryOperationJournalEndpoint>(MockBehavior.Strict);
            var outcome = new XRegistryOperationOutcome(XRegistryOperationState.Committed, new XRegistryResponse(201));
            backend.Setup(value => value.GetOperationOutcomeAsync("saved-operation", Writer,
                It.Is<CancellationToken>(token => token.CanBeCanceled && !token.IsCancellationRequested)))
                .ReturnsAsync(outcome);
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend.Object, caller, () =>
                {
                    releases++;
                    return default;
                })));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);

            XRegistryOperationOutcome actual =
                await endpoint.GetOperationOutcomeAsync("saved-operation", Writer).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(outcome));
                Assert.That(releases, Is.EqualTo(1));
            });
            backend.Verify(value => value.GetOperationOutcomeAsync(
                "saved-operation", Writer, It.IsAny<CancellationToken>()), Times.Once);
            backend.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Unsupported optional services fail explicitly and still release their acquired caller leases.
        /// </summary>
        [Test]
        public async Task MissingJournalAndFeedReleaseTheirIndependentLeasesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var backend = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend.Object, caller, () =>
                {
                    releases++;
                    return default;
                })));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);

            Assert.ThrowsAsync<NotSupportedException>(async () =>
                await endpoint.GetOperationOutcomeAsync("absent", Writer).ConfigureAwait(false));
            await using IAsyncEnumerator<XRegistryChangeHint> watch = endpoint.WatchAsync(Writer).GetAsyncEnumerator();
            Assert.ThrowsAsync<NotSupportedException>(async () => await watch.MoveNextAsync().ConfigureAwait(false));

            Assert.That(releases, Is.EqualTo(2));
            backend.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Revocation cancels an already dispatched read through the linked token and releases its lease.
        /// </summary>
        [Test]
        public async Task RevocationCancelsAnInFlightReadAndReleasesItsLeaseAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using var revoked = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.ExecuteAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .Returns((XRegistryRequest _, CancellationToken ct) => ReadAsync(ct));
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend.Object, caller, () =>
                {
                    Interlocked.Increment(ref releases);
                    return default;
                }, revoked: revoked.Token)));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            Task<XRegistryResponse> read = endpoint.ExecuteAsync(Request(XRegistryAction.Read)).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                Assert.That(releases, Is.Zero);
                await CancelAsync(revoked).ConfigureAwait(false);
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await read.WaitAsync(s_wait).ConfigureAwait(false));
                Assert.That(releases, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await CancelAsync(revoked).ConfigureAwait(false);
            }

            async ValueTask<XRegistryResponse> ReadAsync(CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return new XRegistryResponse(200);
            }
        }

        /// <summary>
        /// Each hint is authorized again while a single caller lease owns the whole subscription.
        /// </summary>
        [Test]
        public async Task FeedRevalidatesBeforePublishingTheNextHintAndReleasesOnDenialAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var feed = new BoundaryFeed(fixture.Native);
            bool authorized = true;
            int acquisitions = 0;
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
            {
                acquisitions++;
                return new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(feed, caller, () =>
                {
                    releases++;
                    return default;
                }, _ => new ValueTask<bool>(authorized)));
            });
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            await using IAsyncEnumerator<XRegistryChangeHint> watch = endpoint.WatchAsync(Writer).GetAsyncEnumerator();
            Assert.That(await watch.MoveNextAsync().AsTask().WaitAsync(s_wait).ConfigureAwait(false), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(watch.Current, Is.EqualTo(new XRegistryChangeHint(Group, "first")));
                Assert.That(acquisitions, Is.EqualTo(1));
                Assert.That(releases, Is.Zero);
            });

            authorized = false;
            feed.Next.TrySetResult(true);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await watch.MoveNextAsync().AsTask().WaitAsync(s_wait).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(feed.Released.Task.IsCompleted, Is.True);
                Assert.That(acquisitions, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Cleanup errors remain visible before commit but cannot turn a known success into a rejection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparationCleanupFailureReleasesLeaseAndPreservesOnlyKnownCommitsAsync(bool commit)
        {
            await using var fixture = new XRegistrySyncFixture();
            var operation = new Mock<IXRegistryPreparedOperation>(MockBehavior.Strict);
            var response = new XRegistryResponse(200);
            operation.SetupGet(value => value.Response).Returns(response);
            operation.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response);
            operation.Setup(value => value.DisposeAsync())
                .Throws(new IOException("Injected candidate cleanup failure."));
            var backend = new Mock<IXRegistryPreparedEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.PrepareAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation.Object);
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend.Object, caller, () =>
                {
                    releases++;
                    return default;
                })));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            IXRegistryPreparedOperation prepared =
                await endpoint.PrepareAsync(Request(XRegistryAction.Merge)).ConfigureAwait(false);

            if (commit)
            {
                Assert.That(await prepared.CommitAsync().ConfigureAwait(false), Is.SameAs(response));
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                Assert.ThrowsAsync<IOException>(async () => await prepared.DisposeAsync().ConfigureAwait(false));
            }
            await prepared.DisposeAsync().ConfigureAwait(false);

            Assert.That(releases, Is.EqualTo(1));
            operation.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()),
                commit ? Times.Once() : Times.Never());
            operation.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Disposal cannot release an endpoint lease while its commit is still in flight.
        /// </summary>
        [Test]
        public async Task DisposalWaitsForTheActiveCommitBeforeReleasingOwnershipAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<XRegistryResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = new Mock<IXRegistryPreparedOperation>(MockBehavior.Strict);
            operation.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.TrySetResult(true);
                return new ValueTask<XRegistryResponse>(release.Task);
            });
            operation.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var backend = new Mock<IXRegistryPreparedEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.PrepareAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation.Object);
            int releases = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend.Object, caller, () =>
                {
                    Interlocked.Increment(ref releases);
                    return default;
                })));
            var endpoint = new XRegistryScopedEndpoint(resolver, fixture.Telemetry);
            IXRegistryPreparedOperation prepared =
                await endpoint.PrepareAsync(Request(XRegistryAction.Merge)).ConfigureAwait(false);
            Task<XRegistryResponse> commit = prepared.CommitAsync().AsTask();
            Task? disposal = null;
            try
            {
                await entered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                Task pendingDisposal = prepared.DisposeAsync().AsTask();
                disposal = pendingDisposal;
                Assert.Multiple(() =>
                {
                    Assert.That(pendingDisposal.IsCompleted, Is.False);
                    Assert.That(releases, Is.Zero);
                });
            }
            finally
            {
                release.TrySetResult(new XRegistryResponse(200));
                Assert.That((await commit.WaitAsync(s_wait).ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
                if (disposal is not null)
                {
                    await disposal.WaitAsync(s_wait).ConfigureAwait(false);
                }
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(releases, Is.EqualTo(1));
            operation.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Dependency injection exposes one caller-scoped adapter through all registered endpoint contracts.
        /// </summary>
        [Test]
        public async Task DependencyInjectionUsesOneAdapterAndTheSuppliedResolverAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint provider = Provider();
            IXRegistryEndpointResolver resolver = XRegistryEndpointResolver.Borrow(provider);
            var services = new ServiceCollection();
            services.AddSingleton(fixture.Telemetry);
            Assert.That(services.AddXRegistryCallerEndpoints(resolver), Is.SameAs(services));
            using ServiceProvider container = services.BuildServiceProvider();
            XRegistryScopedEndpoint scoped = container.GetRequiredService<XRegistryScopedEndpoint>();

            XRegistryEndpointDescription description = await container.GetRequiredService<IXRegistryEndpoint>()
                .InspectAsync(Writer).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(container.GetRequiredService<IXRegistryEndpointResolver>(), Is.SameAs(resolver));
                Assert.That(container.GetRequiredService<IXRegistryEndpoint>(), Is.SameAs(scoped));
                Assert.That(container.GetRequiredService<IXRegistryPreparedEndpoint>(), Is.SameAs(scoped));
                Assert.That(container.GetRequiredService<IXRegistryOperationJournalEndpoint>(), Is.SameAs(scoped));
                Assert.That(description.RegistryId, Is.EqualTo("scoped-boundary"));
                Assert.That(description.SupportsPreparedSnapshots, Is.True);
            });
        }

        private static XRegistryRequest Request(XRegistryAction action)
        {
            return new XRegistryRequest(action, "/")
            {
                Context = Writer,
                View = XRegistryView.Metadata,
                Metadata = action == XRegistryAction.Merge
                    ? Json(/*lang=json,strict*/ """{"name":"candidate"}""") : default
            };
        }

        private static XRegistryTransactionalEndpoint Provider()
        {
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "scoped-boundary",
                Model = Json(/*lang=json,strict*/ """{"groups":{}}""")
            }, new InMemoryXRegistryTransactionStore());
        }

        private static Task CancelAsync(CancellationTokenSource source)
        {
#if NET8_0_OR_GREATER
            return source.CancelAsync();
#else
            return Task.Run(source.Cancel);
#endif
        }

        private sealed class BoundaryFeed(IXRegistryEndpoint endpoint) : IXRegistryEndpoint, IXRegistryChangeFeed
        {
            public TaskCompletionSource<bool> Next { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Released { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<XRegistryEndpointDescription> InspectAsync(
                XRegistryCallContext context, CancellationToken cancellationToken = default)
            {
                return endpoint.InspectAsync(context, cancellationToken);
            }

            public ValueTask<XRegistryResponse> ExecuteAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                return endpoint.ExecuteAsync(request, cancellationToken);
            }

            public async IAsyncEnumerable<XRegistryChangeHint> WatchAsync(
                XRegistryCallContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                try
                {
                    await Task.Yield();
                    yield return new XRegistryChangeHint(Group, "first");
                    await Next.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    yield return new XRegistryChangeHint(Resource, "must-not-escape");
                }
                finally
                {
                    Released.TrySetResult(true);
                }
            }
        }

        private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);
    }
}
