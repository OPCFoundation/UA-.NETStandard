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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for <see cref="Browser"/> continuation point handling.
    /// Verifies that null and empty ContinuationPoints are not treated
    /// as valid continuation points per OPC 10000-4 Section 7.9.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Browser")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class BrowserTests
    {
        /// <summary>
        /// Streaming discovery must send the configured page bound rather than requesting an unbounded first page.
        /// </summary>
        [TestCase(0u)]
        [TestCase(64u)]
        public async Task BrowseStreamHonorsConfiguredReferenceLimitAsync(uint maximumReferences)
        {
            using SessionMock session = SessionMock.Create();
            session.Channel
                .Setup(channel => channel.SendRequestAsync(
                    It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = [new BrowseResult { References = [CreateReferenceDescription("Bounded")] }],
                    DiagnosticInfos = []
                }));
            var browser = new Browser(session) { MaxReferencesReturned = maximumReferences };
            int pages = 0;

            await foreach (BrowseResult page in browser.BrowseStreamAsync(null, null,
                [new BrowseDescription { NodeId = ObjectIds.ObjectsFolder }]).ConfigureAwait(false))
            {
                Assert.That(page.References.Count, Is.EqualTo(1));
                pages++;
            }

            Assert.That(pages, Is.EqualTo(1));
            session.Channel.Verify(channel => channel.SendRequestAsync(
                It.Is<BrowseRequest>(request => request.RequestedMaxReferencesPerNode == maximumReferences),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// When the server returns a default (null) ContinuationPoint,
        /// BrowseNext must not be called.
        /// </summary>
        [Test]
        public async Task BrowseAsyncWithNullContinuationPointDoesNotCallBrowseNext()
        {
            using var session = SessionMock.Create();
            ReferenceDescription expectedRef = CreateReferenceDescription("Object1");

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = [expectedRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session) { ContinueUntilDone = true };

            // Act
            ArrayOf<ReferenceDescription> result = await browser.BrowseAsync(
                ObjectIds.ObjectsFolder).ConfigureAwait(false);

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].BrowseName, Is.EqualTo(expectedRef.BrowseName));

            session.Channel.Verify(
                c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never,
                "BrowseNext must not be called when ContinuationPoint is null.");
        }

        /// <summary>
        /// When the server returns an empty (zero-length) ContinuationPoint,
        /// BrowseNext must not be called.
        /// </summary>
        [Test]
        public async Task BrowseAsyncWithEmptyContinuationPointDoesNotCallBrowseNext()
        {
            using var session = SessionMock.Create();
            ReferenceDescription expectedRef = CreateReferenceDescription("Object1");

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = ByteString.Empty,
                            References = [expectedRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session) { ContinueUntilDone = true };

            // Act
            ArrayOf<ReferenceDescription> result = await browser.BrowseAsync(
                ObjectIds.ObjectsFolder).ConfigureAwait(false);

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].BrowseName, Is.EqualTo(expectedRef.BrowseName));

            session.Channel.Verify(
                c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never,
                "BrowseNext must not be called when ContinuationPoint is empty.");
        }

        /// <summary>
        /// When the server returns a valid ContinuationPoint, BrowseNext
        /// must be called to retrieve the remaining references.
        /// </summary>
        [Test]
        public async Task BrowseAsyncWithValidContinuationPointCallsBrowseNext()
        {
            using var session = SessionMock.Create();
            ReferenceDescription firstRef = CreateReferenceDescription("Object1");
            ReferenceDescription secondRef = CreateReferenceDescription("Object2");

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = new ByteString(new byte[] { 0x01, 0x02 }),
                            References = [firstRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = [secondRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session) { ContinueUntilDone = true };

            // Act
            ArrayOf<ReferenceDescription> result = await browser.BrowseAsync(
                ObjectIds.ObjectsFolder).ConfigureAwait(false);

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].BrowseName, Is.EqualTo(firstRef.BrowseName));
            Assert.That(result[1].BrowseName, Is.EqualTo(secondRef.BrowseName));

            session.Channel.Verify(
                c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "BrowseNext must be called exactly once for a valid ContinuationPoint.");
        }

        /// <summary>
        /// BrowseStreamAsync yields each result as it arrives and does
        /// not call BrowseNext when ContinuationPoint is null/empty.
        /// </summary>
        [Test]
        public async Task BrowseStreamAsyncWithoutContinuationDoesNotCallBrowseNext()
        {
            using var session = SessionMock.Create();
            ReferenceDescription firstRef = CreateReferenceDescription("Object1");
            ReferenceDescription secondRef = CreateReferenceDescription("Object2");

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = [firstRef]
                        },
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = ByteString.Empty,
                            References = [secondRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session);
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription { NodeId = ObjectIds.ObjectsFolder },
                new BrowseDescription { NodeId = ObjectIds.RootFolder }
            ];

            // Act
            var results = new List<BrowseResult>();
            await foreach (BrowseResult result in browser.BrowseStreamAsync(
                null, null, nodesToBrowse, default).ConfigureAwait(false))
            {
                results.Add(result);
            }

            // Assert
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].References[0].BrowseName, Is.EqualTo(firstRef.BrowseName));
            Assert.That(results[1].References[0].BrowseName, Is.EqualTo(secondRef.BrowseName));

            session.Channel.Verify(
                c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never,
                "BrowseNext must not be called when no ContinuationPoint is set.");
        }

        /// <summary>
        /// BrowseStreamAsync follows continuation points and yields the
        /// follow-up results.
        /// </summary>
        [Test]
        public async Task BrowseStreamAsyncFollowsContinuationPoints()
        {
            using var session = SessionMock.Create();
            ReferenceDescription firstRef = CreateReferenceDescription("Object1");
            ReferenceDescription secondRef = CreateReferenceDescription("Object2");

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = new ByteString(new byte[] { 0x01, 0x02 }),
                            References = [firstRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = [secondRef]
                        }
                    ],
                    DiagnosticInfos = []
                }));

            var browser = new Browser(session);
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription { NodeId = ObjectIds.ObjectsFolder }
            ];

            // Act
            var results = new List<BrowseResult>();
            await foreach (BrowseResult result in browser.BrowseStreamAsync(
                null, null, nodesToBrowse, default).ConfigureAwait(false))
            {
                results.Add(result);
            }

            // Assert — first the initial page, then the follow-up.
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].References[0].BrowseName, Is.EqualTo(firstRef.BrowseName));
            Assert.That(results[1].References[0].BrowseName, Is.EqualTo(secondRef.BrowseName));

            session.Channel.Verify(
                c => c.SendRequestAsync(
                    It.IsAny<BrowseNextRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// BrowseStreamAsync throws ServiceResultException when the
        /// browser has no attached session.
        /// </summary>
        [Test]
        public void BrowseStreamAsyncThrowsWhenNotAttached()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var browser = new Browser(telemetry);
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription { NodeId = ObjectIds.ObjectsFolder }
            ];

            Assert.That(
                async () =>
                {
                    await foreach (BrowseResult _ in browser.BrowseStreamAsync(
                        null, null, nodesToBrowse, default).ConfigureAwait(false))
                    {
                    }
                },
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadServerNotConnected));
        }

        /// <summary>
        /// Cancellation is checked before sending requests, even for an empty
        /// node list and when the transport does not observe the token.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ManagedBrowseRejectsPreCancelledTokenAsync(bool empty, bool useSessionExtension)
        {
            using SessionMock session = SessionMock.Create();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ArrayOf<NodeId> nodes = empty ? [] : [ObjectIds.RootFolder];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = empty ? [] : [new BrowseResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = []
                }));

            await Assert.ThatAsync(
                async () =>
                {
                    if (useSessionExtension)
                    {
                        await session.ManagedBrowseAsync(
                            null, null, nodes, 0, BrowseDirection.Forward,
                            ReferenceTypeIds.HierarchicalReferences, true, 0, cancellation.Token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        var browser = new Browser(session);
                        await browser.BrowseAsync(nodes, cancellation.Token).ConfigureAwait(false);
                    }
                },
                Throws.InstanceOf<OperationCanceledException>()
                    .With.Property(nameof(OperationCanceledException.CancellationToken))
                    .EqualTo(cancellation.Token)).ConfigureAwait(false);

            session.Channel.Verify(
                c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Request failures and cancellation must propagate rather than return
        /// an empty or partially populated successful result.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ManagedBrowsePropagatesRequestFailuresAsync(bool browseNext, bool cancel)
        {
            using SessionMock session = SessionMock.Create();
            using var cancellation = new CancellationTokenSource();
            Exception expected = cancel
                ? new OperationCanceledException(cancellation.Token)
                : new ServiceResultException(StatusCodes.BadSecureChannelClosed);

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken ct) =>
                {
                    Assert.That(ct, Is.EqualTo(cancellation.Token));
                    if (browseNext && request is BrowseRequest)
                    {
                        return new ValueTask<IServiceResponse>(new BrowseResponse
                        {
                            Results =
                            [
                                new BrowseResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    References = [CreateReferenceDescription("FirstPage")],
                                    ContinuationPoint = new ByteString(new byte[] { 1 })
                                }
                            ],
                            DiagnosticInfos = []
                        });
                    }
                    if (cancel)
                    {
                        cancellation.Cancel();
                    }
                    throw expected;
                });

            await Assert.ThatAsync(
                () => session.ManagedBrowseAsync(
                    null, null, [ObjectIds.RootFolder], 1, BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences, true, 0, cancellation.Token),
                Throws.Exception.With.SameAs(expected)).ConfigureAwait(false);

            session.Channel.Verify(
                c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            session.Channel.Verify(
                c => c.SendRequestAsync(It.IsAny<BrowseNextRequest>(), It.IsAny<CancellationToken>()),
                browseNext ? Times.Once() : Times.Never());
        }

        private static ReferenceDescription CreateReferenceDescription(string name)
        {
            return new ReferenceDescription
            {
                NodeId = new NodeId(name, 0),
                BrowseName = new QualifiedName(name),
                DisplayName = new LocalizedText(name),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType
            };
        }
    }
}
