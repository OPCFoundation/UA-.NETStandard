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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Machinery.Client;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Uses the real managed-session dispatch guard to verify Machinery ownership across service calls.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachinerySessionOwnershipTests
    {
        /// <summary>
        /// Normal cleanup uses the captured authentication token without closing the owning session.
        /// </summary>
        [Test]
        public async Task ManagedBrowseReleasesOnTheOriginalSession()
        {
            await using var fixture = new SessionRequestContextTestContext();
            AddNamespace(fixture, Namespaces.Machinery);
            NodeId authenticationToken = fixture.InnerSession.SaveSessionConfiguration().AuthenticationToken;
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(request is BrowseRequest
                ? new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = new ByteString(new byte[] { 3 }) }]
                }
                : new BrowseNextResponse { ResponseHeader = new ResponseHeader(), Results = [] });
            var client = new MachineryClient(fixture.ManagedSession, fixture.Telemetry,
                new MachineryClientOptions { MaximumBrowsePages = 1 });

            MachineryReadException failure = Assert.ThrowsAsync<MachineryReadException>(
                async () => await client.DiscoverMachinesAsync().ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            Assert.That(fixture.Requests[1], Is.TypeOf<BrowseNextRequest>()
                .With.Property(nameof(BrowseNextRequest.ReleaseContinuationPoints)).True);
            Assert.That(fixture.Requests.Select(request => request.RequestHeader.AuthenticationToken),
                Is.All.EqualTo(authenticationToken));
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        /// <summary>
        /// Recovery cannot forward continuation points or their cleanup to a replacement session generation.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ManagedBrowseNeverForwardsOwnedTokensAfterRecovery(bool duringNext, bool reuseIdentity)
        {
            await using var fixture = new SessionRequestContextTestContext();
            AddNamespace(fixture, Namespaces.Machinery);
            NodeId originalId = fixture.InnerSession.SessionId;
            NodeId originalToken = fixture.InnerSession.SaveSessionConfiguration().AuthenticationToken;
            var token = new ByteString(new byte[] { 7 });
            int nextCount = 0;
            fixture.Handler = (request, _) =>
            {
                if (request is BrowseRequest)
                {
                    if (!duringNext)
                    {
                        Recreate(fixture, reuseIdentity, originalId, originalToken);
                    }
                    return new ValueTask<IServiceResponse>(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowseResult { ContinuationPoint = token }]
                    });
                }
                if (request is BrowseNextRequest next)
                {
                    if (!next.ReleaseContinuationPoints && duringNext && ++nextCount == 1)
                    {
                        Recreate(fixture, reuseIdentity, originalId, originalToken);
                        return new ValueTask<IServiceResponse>(new BrowseNextResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { ContinuationPoint = token }]
                        });
                    }
                    return new ValueTask<IServiceResponse>(new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = next.ReleaseContinuationPoints ? [] : [new BrowseResult()]
                    });
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType().Name);
            };
            var client = new MachineryClient(fixture.ManagedSession, fixture.Telemetry, new MachineryClientOptions());

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                async () => await client.DiscoverMachinesAsync().ConfigureAwait(false))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions, Has.All.InstanceOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(duringNext ? 2 : 1));
            Assert.That(fixture.Requests.Select(request => request.RequestHeader.AuthenticationToken),
                Is.All.EqualTo(originalToken));
            Assert.That(fixture.Requests.OfType<BrowseNextRequest>().Any(request => request.ReleaseContinuationPoints),
                Is.False);
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        /// <summary>
        /// A target resolved before recovery cannot be reused to generate a result on the replacement session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagedDownloadPinsBeforeResolvingTheTransferPath(bool reuseIdentity)
        {
            await using var fixture = new SessionRequestContextTestContext();
            AddNamespace(fixture, Result.Namespaces.MachineryResult);
            NodeId originalId = fixture.InnerSession.SessionId;
            NodeId originalToken = fixture.InnerSession.SaveSessionConfiguration().AuthenticationToken;
            int translations = 0;
            fixture.Handler = (request, _) =>
            {
                if (request is TranslateBrowsePathsToNodeIdsRequest)
                {
                    if (++translations == 1)
                    {
                        Recreate(fixture, reuseIdentity, originalId, originalToken);
                        return new ValueTask<IServiceResponse>(new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new BrowsePathResult
                                {
                                    Targets =
                                    [
                                        new BrowsePathTarget
                                        {
                                            TargetId = new NodeId("old-transfer", 1),
                                            RemainingPathIndex = uint.MaxValue
                                        }
                                    ]
                                }
                            ]
                        });
                    }
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType().Name);
            };
            var client = new MachineryClient(fixture.ManagedSession, fixture.Telemetry);
            using var destination = new MemoryStream();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.DownloadResultAsync(
                    new NodeId("machine", 1), "selected", destination, 1).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            Assert.That(fixture.Requests[0].RequestHeader.AuthenticationToken, Is.EqualTo(originalToken));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        private static void AddNamespace(SessionRequestContextTestContext fixture, string uri)
        {
            if (fixture.InnerSession.NamespaceUris.GetIndex(uri) < 0)
            {
                fixture.InnerSession.NamespaceUris.Append(uri);
            }
            if (fixture.MessageContext.NamespaceUris.GetIndex(uri) < 0)
            {
                fixture.MessageContext.NamespaceUris.Append(uri);
            }
        }

        private static void Recreate(
            SessionRequestContextTestContext fixture, bool reuseIdentity, NodeId sessionId, NodeId token)
        {
            if (reuseIdentity)
            {
                fixture.InnerSession.SessionCreated(sessionId, token);
            }
            else
            {
                fixture.Recreate();
            }
        }
    }
}
