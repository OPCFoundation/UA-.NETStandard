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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SoftwareUpdateUploadOutcomeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task UploadPreservesCommitCompletionEvidenceAndDoesNotOwnTheInputAsync(bool asynchronous)
        {
            var fixture = new UploadResponses
            {
                CompletionId = asynchronous ? new NodeId("completion", 1) : NodeId.Null
            };
            using var stream = new MemoryStream(s_payload, writable: false);
            SoftwareUpdateUploadResult result = await fixture.Client.UploadPackageWithResultAsync(
                stream, "package", 2).ConfigureAwait(false);

            Assert.That(result.BytesUploaded, Is.EqualTo(5));
            Assert.That(result.CompletionStateMachine, Is.EqualTo(fixture.CompletionId));
            Assert.That(fixture.Payload, Is.EqualTo(s_payload));
            Assert.That(fixture.Methods, Is.EqualTo(s_successfulMethods));
            Assert.That(stream.CanRead, Is.True);
            fixture.Session.Verify(value => value.Dispose(), Times.Never);
        }

        [TestCase("GenerateFileForWrite")]
        [TestCase("Write")]
        [TestCase("Close")]
        [TestCase("CloseAndCommit")]
        public async Task BadServiceHeaderCannotBecomeSuccessfulUploadAsync(string method)
        {
            var fixture = new UploadResponses { HeaderFailure = method };
            using var stream = new MemoryStream(s_payload, writable: false);
            await Assert.ThatAsync(() => fixture.Client.UploadPackageWithResultAsync(stream).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
            if (method != "CloseAndCommit")
            {
                Assert.That(fixture.Methods, Does.Not.Contain("CloseAndCommit"));
            }
            Assert.That(stream.CanRead, Is.True);
        }

        [TestCase("empty")]
        [TestCase("type")]
        [TestCase("extra")]
        public async Task InvalidCommitOutputDoesNotInventSynchronousCompletionAsync(string invalid)
        {
            var fixture = new UploadResponses { InvalidCommit = invalid };
            using var stream = new MemoryStream(s_payload, writable: false);
            await Assert.ThatAsync(() => fixture.Client.UploadPackageWithResultAsync(stream).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
            Assert.That(fixture.Payload, Is.EqualTo(s_payload));
            Assert.That(fixture.Methods[^1], Is.EqualTo("CloseAndCommit"));
        }

        [Test]
        public async Task FailedTranslationCannotGenerateAFileEvenWhenItsResultsLookGoodAsync()
        {
            var fixture = new UploadResponses { TranslationFailure = true };
            using var stream = new MemoryStream(s_payload, writable: false);
            await Assert.ThatAsync(() => fixture.Client.UploadPackageWithResultAsync(stream).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTimeout)).ConfigureAwait(false);
            Assert.That(fixture.Methods, Is.Empty);
        }

        [Test]
        public async Task CancellationClosesTheOpenHandleWithAnIndependentTokenAndNeverCommitsAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var fixture = new UploadResponses { CancelOnWrite = cancellation };
            using var stream = new MemoryStream(s_payload, writable: false);
            await Assert.ThatAsync(() => fixture.Client.UploadPackageWithResultAsync(
                stream, ct: cancellation.Token).AsTask(), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(fixture.Methods, Is.EqualTo(s_canceledMethods));
            Assert.That(fixture.CloseWasCanceled, Is.False);
            Assert.That(fixture.Methods, Does.Not.Contain("CloseAndCommit"));
            Assert.That(stream.CanRead, Is.True);
        }

        [Test]
        public async Task AlreadyCanceledUploadDoesNotTranslateOrGenerateAsync()
        {
            var fixture = new UploadResponses();
            using var stream = new MemoryStream(s_payload, writable: false);
            await Assert.ThatAsync(() => fixture.Client.UploadPackageWithResultAsync(
                stream, ct: new CancellationToken(true)).AsTask(), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            fixture.Session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.That(fixture.Methods, Is.Empty);
        }

        private static readonly byte[] s_payload = [1, 3, 5, 7, 9];
        private static readonly string[] s_canceledMethods = ["GenerateFileForWrite", "Open", "Write", "Close"];
        private static readonly string[] s_successfulMethods =
            ["GenerateFileForWrite", "Open", "Write", "Write", "Write", "Close", "CloseAndCommit"];

        private sealed class UploadResponses
        {
            public UploadResponses()
            {
                var namespaces = new NamespaceTable();
                namespaces.GetIndexOrAppend(Namespaces.OpcUaDi);
                Session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                Session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    {
                        var results = new BrowsePathResult[paths.Count];
                        for (int i = 0; i < paths.Count; i++)
                        {
                            string name = paths[i].RelativePath.Elements[^1].TargetName.Name!;
                            results[i] = new BrowsePathResult
                            {
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = new ExpandedNodeId(new NodeId(name, 1)),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            };
                        }
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader
                                {
                                    ServiceResult = TranslationFailure ? StatusCodes.BadTimeout : StatusCodes.Good
                                },
                                Results = results
                            });
                    });
                Session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken token) =>
                    {
                        Assert.That(calls.Count, Is.EqualTo(1));
                        CallMethodRequest call = calls[0];
                        Assert.That(call.MethodId.TryGetValue(out string method), Is.True);
                        Methods.Add(method);
                        ArrayOf<Variant> output = [];
                        switch (method)
                        {
                            case "GenerateFileForWrite":
                                output = [Variant.From(new NodeId("upload", 1)), Variant.From(0u)];
                                break;
                            case "Open":
                                output = [Variant.From(17u)];
                                break;
                            case "Write":
                                if (CancelOnWrite is { } cancellation)
                                {
                                    cancellation.Cancel();
                                    throw new OperationCanceledException(cancellation.Token);
                                }
                                Assert.That(call.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                                Payload.AddRange(bytes.Span.ToArray());
                                break;
                            case "Close":
                                CloseWasCanceled = token.IsCancellationRequested;
                                break;
                            case "CloseAndCommit":
                                output = InvalidCommit switch
                                {
                                    "empty" => [],
                                    "type" => [Variant.From("not a NodeId")],
                                    "extra" => [Variant.From(CompletionId), Variant.From(1u)],
                                    _ => [Variant.From(CompletionId)]
                                };
                                break;
                            default:
                                throw new InvalidOperationException("Unexpected method " + call.MethodId);
                        }
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader
                            {
                                ServiceResult = method == HeaderFailure
                                    ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                            },
                            Results = [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output }]
                        });
                    });
                Client = new SoftwareUpdateClient(Session.Object, new NodeId("software", 1),
                    DefaultTelemetry.Create(static _ => { }));
            }

            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
            public SoftwareUpdateClient Client { get; }
            public List<string> Methods { get; } = [];
            public List<byte> Payload { get; } = [];
            public NodeId CompletionId { get; init; }
            public string? HeaderFailure { get; init; }
            public string? InvalidCommit { get; init; }
            public bool TranslationFailure { get; init; }
            public CancellationTokenSource? CancelOnWrite { get; init; }
            public bool CloseWasCanceled { get; private set; }
        }
    }
}
