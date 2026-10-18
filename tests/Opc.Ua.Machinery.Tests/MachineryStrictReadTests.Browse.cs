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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Client;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// Zero, at-limit and over-limit reference responses are distinguished before accumulation.
        /// </summary>
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task BrowseReferenceBudgetHasExactBoundaries(int count)
        {
            var harness = new ReadHarness();
            var references = new ReferenceDescription[count];
            for (int ii = 0; ii < count; ii++)
            {
                references[ii] = Reference(harness, (uint)ii);
            }
            harness.Browse = _ => Page(references.ToArrayOf());
            MachineryClient client = harness.Client(new MachineryClientOptions { MaximumBrowseReferences = 2 });
            if (count > 2)
            {
                Assert.That(
                    Assert.ThrowsAsync<MachineryReadException>(async () =>
                        await client.DiscoverMachinesAsync().ConfigureAwait(false))!.StatusCode,
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                ArrayOf<NodeId> machines = await client.DiscoverMachinesAsync().ConfigureAwait(false);
                Assert.That(machines.Count, Is.EqualTo(count));
                Assert.That(machines.ToArray(),
                    Is.EqualTo(references.Select(r =>
                        ExpandedNodeId.ToNodeId(r.NodeId, harness.Namespaces)).ToArray()));
            }
            Assert.That(harness.BrowseLimits.ToArray(), Is.EqualTo(new uint[] { 2 }));
            Assert.That(harness.NextRequests, Is.Empty);
        }

        /// <summary>
        /// Duplicate references consume the raw reference budget even when identities are deduplicated.
        /// </summary>
        [Test]
        public void BrowseBudgetCountsDuplicateReferencesBeforeDeduplication()
        {
            var harness = new ReadHarness();
            ReferenceDescription duplicate = Reference(harness, 1);
            harness.Browse = _ => Page([duplicate, duplicate], Token(1));
            Assert.That(
                Assert.ThrowsAsync<MachineryReadException>(async () =>
                    await harness.Client(new MachineryClientOptions { MaximumBrowseReferences = 1 })
                        .DiscoverMachinesAsync()
                        .ConfigureAwait(false))!.StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.Releases.Select(r => r.Token).ToArray(), Is.EqualTo([Token(1)]));
            Assert.That(harness.NextRequests, Is.Empty);
        }

        /// <summary>
        /// Empty pages count against the page limit; an exact-limit terminal page succeeds.
        /// </summary>
        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(2, false)]
        public async Task BrowsePageBudgetCountsEmptyPagesAndExactTerminalBoundary(int pages, bool terminal)
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Next = (_, _) => Task.FromResult(NextPage([], terminal ? default : Token(2)))
            };
            MachineryClient client = harness.Client(new MachineryClientOptions { MaximumBrowsePages = pages });
            if (pages == 2 && terminal)
            {
                Assert.That((await client.DiscoverMachinesAsync().ConfigureAwait(false)).Count, Is.Zero);
                Assert.That(harness.Releases, Is.Empty);
            }
            else
            {
                Assert.That(
                    Assert.ThrowsAsync<MachineryReadException>(async () =>
                        await client.DiscoverMachinesAsync().ConfigureAwait(false))!.StatusCode,
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(harness.Releases.Single().Token, Is.EqualTo(Token((byte)pages)));
            }
            Assert.That(harness.NextRequests, Has.Count.EqualTo(pages - 1));
        }

        /// <summary>
        /// An empty initial page is followed, and unique identities retain order across pages.
        /// </summary>
        [Test]
        public async Task BrowseContinuationCompletesWithoutDroppingLaterReferences()
        {
            var harness = new ReadHarness();
            ReferenceDescription first = Reference(harness, 1);
            ReferenceDescription second = Reference(harness, 2);
            harness.Browse = _ => Page([], Token(1));
            harness.Next = (_, _) => Task.FromResult(NextPage([first, first, second]));
            ArrayOf<NodeId> machines = await harness.Client(
                new MachineryClientOptions { MaximumBrowsePages = 2, MaximumBrowseReferences = 3 })
                .DiscoverMachinesAsync()
                .ConfigureAwait(false);
            Assert.That(machines.ToArray(), Is.EqualTo(
            [
                ExpandedNodeId.ToNodeId(first.NodeId, harness.Namespaces),
                ExpandedNodeId.ToNodeId(second.NodeId, harness.Namespaces)
            ]));
            Assert.That(harness.NextRequests, Has.Count.EqualTo(1));
            Assert.That(harness.Releases, Is.Empty);
        }

        /// <summary>
        /// Repeated continuation points remain bounded by the page budget and are released once.
        /// </summary>
        [Test]
        public void RepeatedContinuationRemainsBoundedAndIsReleasedOnce()
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(7)),
                Next = (_, _) => Task.FromResult(NextPage([], Token(7)))
            };
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client(new MachineryClientOptions { MaximumBrowsePages = 3 })
                    .DiscoverMachinesAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.NextRequests, Has.Count.EqualTo(2));
            Assert.That(harness.Releases.Single().Token, Is.EqualTo(Token(7)));
        }

        /// <summary>
        /// Opaque continuation tokens may stay unchanged while the server advances through distinct pages.
        /// </summary>
        [Test]
        public async Task ProgressingPagesMayReuseTheSameContinuationToken()
        {
            var harness = new ReadHarness();
            ReferenceDescription first = Reference(harness, 1);
            ReferenceDescription second = Reference(harness, 2);
            ReferenceDescription third = Reference(harness, 3);
            harness.Browse = _ => Page([first], Token(7));
            harness.Next = (_, _) => Task.FromResult(harness.NextRequests.Count == 1
                ? NextPage([second], Token(7))
                : NextPage([third]));

            ArrayOf<NodeId> machines = await harness.Client(new MachineryClientOptions
            {
                MaximumBrowsePages = 3,
                MaximumBrowseReferences = 3
            }).DiscoverMachinesAsync().ConfigureAwait(false);

            Assert.That(machines.ToArray(), Is.EqualTo(new[] { first, second, third }
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, harness.Namespaces)).ToArray()));
            Assert.That(harness.NextRequests.ToArray(), Is.EqualTo(new[] { Token(7), Token(7) }));
            Assert.That(harness.Releases, Is.Empty);
        }

        /// <summary>
        /// An ordinary borrowed session cannot receive an old continuation after changing its server identity.
        /// </summary>
        [Test]
        public void ReplacedBorrowedSessionDoesNotReceiveBrowseNextOrRelease()
        {
            var harness = new ReadHarness();
            harness.Browse = _ =>
            {
                harness.SessionId = new NodeId("replacement-session", 1);
                return Page([], Token(9));
            };

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                async () => await harness.Client().DiscoverMachinesAsync().ConfigureAwait(false))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions, Has.All.InstanceOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(harness.NextRequests, Is.Empty);
            Assert.That(harness.Releases, Is.Empty);
        }

        /// <summary>
        /// Standard empty release responses preserve the original browse failure rather than inventing cleanup errors.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EmptyReleaseResponsePreservesThePrimaryFailure(bool invalidDiagnostics)
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Release = (_, _) => Task.FromResult(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [],
                    DiagnosticInfos = invalidDiagnostics ? [new DiagnosticInfo()] : []
                })
            };
            Task BrowseAsync() => harness.Client(new MachineryClientOptions { MaximumBrowsePages = 1 })
                .DiscoverMachinesAsync().AsTask();

            if (invalidDiagnostics)
            {
                AggregateException error = Assert.ThrowsAsync<AggregateException>(BrowseAsync)!;
                Assert.That(error.InnerExceptions[0], Is.TypeOf<MachineryReadException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(error.InnerExceptions[1], Is.TypeOf<MachineryReadException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError));
            }
            else
            {
                MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(BrowseAsync)!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            Assert.That(harness.Releases.Single().Token, Is.EqualTo(Token(1)));
        }

        /// <summary>
        /// A release implementation that ignores its token cannot hide the original failure indefinitely.
        /// </summary>
        [Test]
        public async Task UnresponsiveBrowseReleaseHasAnIndependentDeadline()
        {
            var release = new TaskCompletionSource<BrowseNextResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken cleanupToken = default;
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Release = (_, token) =>
                {
                    cleanupToken = token;
                    return release.Task;
                }
            };
            Task<ArrayOf<NodeId>> browsing = harness.Client(
                new MachineryClientOptions { MaximumBrowsePages = 1 }).DiscoverMachinesAsync().AsTask();
            try
            {
                AggregateException error = Assert.ThrowsAsync<AggregateException>(
                    async () => await browsing.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))!;
                Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(error.InnerExceptions[0], Is.TypeOf<MachineryReadException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(error.InnerExceptions[1], Is.InstanceOf<OperationCanceledException>());
                Assert.That(cleanupToken.IsCancellationRequested, Is.True);
                Assert.That(harness.Releases.Single().Cancelled, Is.False);
            }
            finally
            {
                release.TrySetResult(NextPage([]));
                await release.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Cancellation after receiving a continuation releases it using a fresh token.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CancelledBrowseReleasesOwnedPointWithIndependentToken(bool duringNext)
        {
            var harness = new ReadHarness();
            using var cancellation = new CancellationTokenSource();
            harness.Browse = _ =>
            {
                if (!duringNext)
                {
                    cancellation.Cancel();
                }
                return Page([], Token(3));
            };
            harness.Next = async (_, token) =>
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                throw new OperationCanceledException(token);
            };
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.Client().DiscoverMachinesAsync(cancellation.Token).ConfigureAwait(false));
            Assert.That(harness.Releases.Single().Token, Is.EqualTo(Token(3)));
            Assert.That(harness.Releases.Single().Cancelled, Is.False);
            Assert.That(harness.NextRequests, Has.Count.EqualTo(duringNext ? 1 : 0));
        }

        /// <summary>
        /// Pre-cancellation makes no browse request.
        /// </summary>
        [Test]
        public void PreCancelledBrowseDoesNotContactSession()
        {
            var harness = new ReadHarness();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.Client().DiscoverMachinesAsync(cancellation.Token).ConfigureAwait(false));
            Assert.That(harness.BrowseLimits, Is.Empty);
            Assert.That(harness.Releases, Is.Empty);
        }

        /// <summary>
        /// A malformed successor cannot hide either its returned tokens or the submitted token from cleanup.
        /// </summary>
        [Test]
        public void MalformedBrowseNextReleasesSubmittedAndUnexpectedReturnedPoints()
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Next = (_, _) => Task.FromResult(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult { ContinuationPoint = Token(2) },
                        new BrowseResult { ContinuationPoint = Token(3) }
                    ]
                })
            };
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().DiscoverMachinesAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(harness.Releases.Select(r => r.Token).ToArray(),
                Is.EqualTo([Token(1), Token(2), Token(3)]));
        }

        /// <summary>
        /// Both the original limit failure and a failed cleanup remain observable without retries.
        /// </summary>
        [Test]
        public void BrowseCleanupFailurePreservesPrimaryFailure()
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Release = (_, _) => Task.FromException<BrowseNextResponse>(
                    new ServiceResultException(StatusCodes.BadCommunicationError))
            };
            AggregateException? error = Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Client(new MachineryClientOptions { MaximumBrowsePages = 1 })
                    .DiscoverMachinesAsync().ConfigureAwait(false));
            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(((MachineryReadException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(((ServiceResultException)error.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(harness.Releases, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Browse service, operation and reference errors do not return an empty success.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("DeniedService", nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase("UncertainService", nameof(StatusCodes.Uncertain))]
        [TestCase("DeniedOperation", nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase("UncertainOperation", nameof(StatusCodes.Uncertain))]
        [TestCase("MissingResponse", nameof(StatusCodes.BadDecodingError))]
        [TestCase("ExtraResult", nameof(StatusCodes.BadDecodingError))]
        [TestCase("NullResult", nameof(StatusCodes.BadDecodingError))]
        [TestCase("NullReference", nameof(StatusCodes.BadDecodingError))]
        [TestCase("WrongNodeClass", nameof(StatusCodes.BadDecodingError))]
        [TestCase("MissingBrowseName", nameof(StatusCodes.BadDecodingError))]
        [TestCase("RemoteReference", nameof(StatusCodes.BadNotSupported))]
        public void StrictBrowseRejectsBadOrMalformedResponses(string kind, string statusName)
        {
            StatusCode expectedStatus = TestStatus(statusName);
            var harness = new ReadHarness();
            ReferenceDescription reference = Reference(harness, 1);
            BrowseResponse response = Page([reference], Token(8));
            switch (kind)
            {
                case "DeniedService":
                case "UncertainService":
                    response.ResponseHeader.ServiceResult = expectedStatus;
                    break;
                case "DeniedOperation":
                case "UncertainOperation":
                    response.Results[0].StatusCode = expectedStatus;
                    break;
                case "MissingResponse":
                    break;
                case "ExtraResult":
                    response.Results = [response.Results[0], response.Results[0]];
                    break;
                case "NullResult":
                    response.Results = [null!];
                    break;
                case "NullReference":
                    response.Results[0].References = [null!];
                    break;
                case "WrongNodeClass":
                    reference.NodeClass = NodeClass.Method;
                    break;
                case "MissingBrowseName":
                    reference.BrowseName = QualifiedName.Null;
                    break;
                case "RemoteReference":
                    reference.NodeId = new ExpandedNodeId(1u, "urn:machinery:strict-tests", 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            harness.Browse = _ => kind == "MissingResponse" ? null! : response;
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().DiscoverMachinesAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(harness.Releases, Has.Count.EqualTo(kind is "NullResult" or "MissingResponse" ? 0 : 1));
        }

        /// <summary>
        /// Calling an identity enumerator is lazy and never reads values or invokes methods.
        /// </summary>
        [Test]
        public async Task IdentityEnumerationIsLazyAndDoesNotReadDetails()
        {
            var harness = new ReadHarness();
            harness.Browse = _ => Page([Reference(harness, 1), Reference(harness, 2)]);
            IAsyncEnumerable<MachineEntry> enumeration = harness.Client().EnumerateMachinesAsync();
            Assert.That(harness.BrowseLimits, Is.Empty);
            var identities = new List<NodeId>();
            await foreach (MachineEntry entry in enumeration)
            {
                identities.Add(entry.NodeId);
                break;
            }
            Assert.That(identities, Has.Count.EqualTo(1));
            Assert.That(harness.BrowseLimits, Has.Count.EqualTo(1));
            Assert.That(harness.ReadTimestamps, Is.Empty);
            Assert.That(harness.Session.Invocations.Any(i => i.Method.Name == "CallAsync"), Is.False);
        }

        /// <summary>
        /// Process-value discovery shares budgets across its machine and Monitoring roots.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ProcessValueDiscoverySharesTraversalBudgets(bool references)
        {
            var harness = new ReadHarness();
            NodeId monitoring = harness.Add(harness.Machine, [Machinery(harness, BrowseNames.Monitoring)]);
            harness.Browse = nodes => Page(
                references ? [Reference(harness, nodes[0].NodeId == monitoring ? 2u : 1u)] : []);
            MachineryClient client = harness.Client(new MachineryClientOptions
            {
                MaximumBrowseReferences = references ? 1 : 10,
                MaximumBrowsePages = references ? 10 : 1
            });
            Assert.That(
                Assert.ThrowsAsync<MachineryReadException>(async () =>
                {
                    await foreach (MachineEntry _ in client.EnumerateProcessValuesAsync(harness.Machine))
                    {
                    }
                })!.StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.BrowseLimits, Has.Count.EqualTo(references ? 2 : 1));
            Assert.That(harness.ReadTimestamps, Is.Empty);
        }

        /// <summary>
        /// Over-budget successor pages release the successor, not the consumed predecessor.
        /// </summary>
        [Test]
        public void NextPageReferenceLimitReleasesTheLiveSuccessor()
        {
            var harness = new ReadHarness();
            harness.Browse = _ => Page([Reference(harness, 1)], Token(1));
            harness.Next = (_, _) => Task.FromResult(NextPage([Reference(harness, 2)], Token(2)));
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(async () =>
                await harness.Client(new MachineryClientOptions { MaximumBrowseReferences = 1 })
                    .DiscoverMachinesAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.Releases.Select(r => r.Token).ToArray(), Is.EqualTo([Token(2)]));
        }

        /// <summary>
        /// A missing continuation response leaves the submitted token owned for cleanup.
        /// </summary>
        [Test]
        public void MissingBrowseNextResponseReleasesSubmittedToken()
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Next = (_, _) => Task.FromResult<BrowseNextResponse>(null!)
            };
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().DiscoverMachinesAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(harness.Releases.Single().Token, Is.EqualTo(Token(1)));
        }

        /// <summary>
        /// An already-invalid point is safely released, while malformed release data is not ignored.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BrowseCleanupValidatesReleaseOutcomes(bool malformed)
        {
            var harness = new ReadHarness
            {
                Browse = _ => Page([], Token(1)),
                Release = (_, _) => Task.FromResult(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = malformed ? StatusCodes.Good : StatusCodes.BadContinuationPointInvalid,
                            ContinuationPoint = malformed ? Token(2) : default
                        }
                    ]
                })
            };
            MachineryClient client = harness.Client(new MachineryClientOptions { MaximumBrowsePages = 1 });
            if (malformed)
            {
                AggregateException? error = Assert.ThrowsAsync<AggregateException>(
                    async () => await client.DiscoverMachinesAsync().ConfigureAwait(false));
                Assert.That(((MachineryReadException)error!.InnerExceptions[1]).StatusCode,
                    Is.EqualTo(StatusCodes.BadDecodingError));
            }
            else
            {
                Assert.That(
                    Assert.ThrowsAsync<MachineryReadException>(async () =>
                        await client.DiscoverMachinesAsync().ConfigureAwait(false))!.StatusCode,
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            Assert.That(harness.Releases, Has.Count.EqualTo(1));
        }

        private static ReferenceDescription Reference(
            ReadHarness harness,
            uint id,
            NodeClass nodeClass = NodeClass.Object)
        {
            return new ReferenceDescription
            {
                NodeId = new NodeId(1000u + id, harness.Machine.NamespaceIndex),
                BrowseName = new QualifiedName($"Node{id}", harness.Machine.NamespaceIndex),
                DisplayName = new LocalizedText($"Node {id}"),
                NodeClass = nodeClass,
                IsForward = true,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinition = nodeClass == NodeClass.Object
                    ? Ua.ObjectTypeIds.BaseObjectType
                    : VariableTypeIds.BaseDataVariableType
            };
        }

        private static BrowseResponse Page(ArrayOf<ReferenceDescription> references, ByteString continuation = default)
        {
            return new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new BrowseResult { References = references, ContinuationPoint = continuation }]
            };
        }

        private static BrowseNextResponse NextPage(
            ArrayOf<ReferenceDescription> references,
            ByteString continuation = default)
        {
            return new BrowseNextResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new BrowseResult { References = references, ContinuationPoint = continuation }]
            };
        }

        private static ByteString Token(byte value)
        {
            return new ByteString(new[] { value });
        }
    }
}
