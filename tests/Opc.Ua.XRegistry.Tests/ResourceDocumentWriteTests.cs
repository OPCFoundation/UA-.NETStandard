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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    public sealed class ResourceDocumentWriteTests
    {
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(8, 2)]
        [TestCase(9, 3)]
        public async Task ExactChunksAreFollowedByOneFreshBoundedClose(int length, int writes)
        {
            var fixture = new WriteFixture();
            ByteString document = ByteString.From(Enumerable.Range(0, length).Select(value => (byte)value).ToArray());
            using var cancellation = new CancellationTokenSource();

            await fixture.Resource.WriteDocumentAsync(73, document, 4, cancellation.Token).ConfigureAwait(false);

            Assert.That(fixture.Chunks, Has.Count.EqualTo(writes));
            Assert.That(fixture.Chunks.SelectMany(chunk => chunk.ToArray()), Is.EqualTo(document.ToArray()));
            Assert.That(fixture.Chunks.All(chunk => chunk.Length is > 0 and <= 4), Is.True);
            Assert.That(fixture.Closes, Is.EqualTo(1));
            Assert.That(fixture.CloseToken.CanBeCanceled, Is.True);
            Assert.That(fixture.CloseToken, Is.Not.EqualTo(cancellation.Token));
            fixture.VerifyBorrowed();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedWritesCloseOnceWithoutReplacingTheOriginalFailure(bool closeFails)
        {
            var fixture = new WriteFixture();
            var writeFailure = new ServiceResultException(StatusCodes.BadTimeout, "write failure");
            var closeFailure = new IOException("close failure");
            fixture.Write = _ => new ValueTask(Task.FromException(writeFailure));
            fixture.Close = _ => closeFails ? new ValueTask(Task.FromException(closeFailure)) : default;

            if (closeFails)
            {
                AggregateException? observed = null;
                try
                {
                    await fixture.Resource.WriteDocumentAsync(73, ByteString.From([1, 2]), 1).ConfigureAwait(false);
                }
                catch (AggregateException error)
                {
                    observed = error;
                }
                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(observed.InnerExceptions[0], Is.SameAs(writeFailure));
                Assert.That(observed.InnerExceptions[1], Is.SameAs(closeFailure));
            }
            else
            {
                await Assert.ThatAsync(() => fixture.Resource.WriteDocumentAsync(
                    73, ByteString.From([1, 2]), 1).AsTask(),
                    Throws.Exception.SameAs(writeFailure)).ConfigureAwait(false);
            }
            Assert.That(fixture.Chunks, Has.Count.EqualTo(1));
            Assert.That(fixture.Closes, Is.EqualTo(1));
            Assert.That(fixture.CloseToken.CanBeCanceled, Is.True);
            fixture.VerifyBorrowed();
        }

        [Test]
        public async Task CanceledWritesDoNotCancelTheOwnedHandleCleanup()
        {
            var fixture = new WriteFixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Write = token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                cancellation.Cancel();
                return new ValueTask(Task.FromCanceled(token));
            };
            fixture.Close = token =>
            {
                Assert.That(token.CanBeCanceled, Is.True);
                Assert.That(token.IsCancellationRequested, Is.False);
                Assert.That(token, Is.Not.EqualTo(cancellation.Token));
                return default;
            };

            await Assert.ThatAsync(() => fixture.Resource.WriteDocumentAsync(
                73, ByteString.From([1, 2]), 1, cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(fixture.Closes, Is.EqualTo(1));
            Assert.That(fixture.Chunks, Has.Count.EqualTo(1));
            fixture.VerifyBorrowed();
        }

        [Test]
        public async Task AnUnresponsiveCloseIsCanceledByItsOwnFiniteDeadline()
        {
            var fixture = new WriteFixture();
            fixture.Close = async token =>
            {
                Assert.That(token.CanBeCanceled, Is.True);
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            };

            Task operation = fixture.Resource.WriteDocumentAsync(73, ByteString.Empty).AsTask();
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var expired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration =
                watchdog.Token.Register(() => expired.TrySetResult(true));
            Assert.That(await Task.WhenAny(operation, expired.Task).ConfigureAwait(false), Is.SameAs(operation));
            // Await the operation itself: the net48 WaitAsync polyfill maps inner cancellation to a timeout.
            await Assert.ThatAsync(() => operation, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);

            Assert.That(fixture.CloseToken.IsCancellationRequested, Is.True);
            Assert.That(fixture.Closes, Is.EqualTo(1));
        }

        [Test]
        public async Task CancelingAnEmptyWriteStillClosesTheAlreadyOwnedHandle()
        {
            var fixture = new WriteFixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(() => fixture.Resource.WriteDocumentAsync(
                73, ByteString.Empty, ct: cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(fixture.Chunks, Is.Empty);
            Assert.That(fixture.Closes, Is.EqualTo(1));
            Assert.That(fixture.CloseToken.IsCancellationRequested, Is.False);
            Assert.That(fixture.CloseToken.CanBeCanceled, Is.True);
        }

        private sealed class WriteFixture
        {
            public WriteFixture()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
                context.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
                Session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
                Session.SetupGet(value => value.MessageContext).Returns(context);
                Session.Setup(value => value.CallAsync(
                        It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                    {
                        Assert.That(requests, Has.Count.EqualTo(1));
                        CallMethodRequest request = requests[0];
                        Assert.That(request.ObjectId, Is.EqualTo(sNode));
                        Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
                        Assert.That(handle, Is.EqualTo(73));
                        if (request.MethodId == Opc.Ua.MethodIds.FileType_Write)
                        {
                            Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                            Chunks.Add(bytes.Copy());
                            await Write(token).ConfigureAwait(false);
                        }
                        else
                        {
                            Assert.That(request.MethodId, Is.EqualTo(Opc.Ua.MethodIds.FileType_Close));
                            Closes++;
                            CloseToken = token;
                            await Close(token).ConfigureAwait(false);
                        }
                        return new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                        };
                    });
                Resource = new ResourceTypeClient(Session.Object, sNode, telemetry);
            }

            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
            public ResourceTypeClient Resource { get; }
            public List<ByteString> Chunks { get; } = [];
            public int Closes { get; private set; }
            public CancellationToken CloseToken { get; private set; }
            public Func<CancellationToken, ValueTask> Write { get; set; } = static _ => default;
            public Func<CancellationToken, ValueTask> Close { get; set; } = static _ => default;

            public void VerifyBorrowed()
            {
                Session.Verify(session => session.Dispose(), Times.Never);
                Session.Verify(session => session.CloseAsync(
                    It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            }

            private static readonly NodeId sNode = new("resource-version", 1);
        }
    }
}
