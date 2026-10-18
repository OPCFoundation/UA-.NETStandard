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
using NUnit.Framework;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryResultTransferTests
    {
        [TestCaseSource(nameof(NonGoodPathStatuses))]
        public void NonGoodTransferPathIsRejectedBeforeGeneration(StatusCode status, bool serviceStatus)
        {
            TranslateBrowsePathsToNodeIdsResponse response = TransferPathResponse();
            if (serviceStatus)
            {
                response.ResponseHeader.ServiceResult = status;
            }
            else
            {
                response.Results[0].StatusCode = status;
            }
            m_transfer.Translate = _ => response;
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8))!;

            Assert.That(error.StatusCode, Is.EqualTo(status));
            Assert.That(m_transfer.Paths, Has.Count.EqualTo(1));
            Assert.That(m_transfer.Calls, Is.Empty);
            Assert.That(m_transfer.OwnsHandle, Is.False);
        }

        [TestCase("missing")]
        [TestCase("multiple")]
        [TestCase("partial")]
        [TestCase("remote")]
        [TestCase("null")]
        [TestCase("null-response")]
        [TestCase("result-count")]
        public void InvalidTransferTargetNeverTriggersGenerationOrAnotherSearch(string kind)
        {
            TranslateBrowsePathsToNodeIdsResponse response = TransferPathResponse();
            BrowsePathTarget target = response.Results[0].Targets[0];
            switch (kind)
            {
                case "missing":
                    response.Results[0].Targets = [];
                    break;
                case "multiple":
                    response.Results[0].Targets = [target, target];
                    break;
                case "partial":
                    target.RemainingPathIndex = 1;
                    break;
                case "remote":
                    target.TargetId = new ExpandedNodeId("file", 1, null, 1);
                    break;
                case "null":
                    target.TargetId = ExpandedNodeId.Null;
                    break;
                case "result-count":
                    response.Results = [];
                    break;
            }
            m_transfer.Translate = _ => kind == "null-response" ? null! : response;
            using var destination = new MemoryStream();

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8));

            Assert.That(m_transfer.Paths, Has.Count.EqualTo(1));
            Assert.That(m_transfer.Calls, Is.Empty);
            Assert.That(m_transfer.OwnsHandle, Is.False);
        }

        [Test]
        public void MissingResultNamespaceDoesNotSearchOtherNamespaces()
        {
            m_transfer.Session.SetupGet(session => session.NamespaceUris).Returns(new NamespaceTable());
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            AssertNoRequests();
        }

        /// <summary>
        /// Replacing an ordinary session while resolving the transfer path cannot begin generation on its successor.
        /// </summary>
        [Test]
        public void SessionReplacementDuringTransferTranslationPreventsGeneration()
        {
            m_transfer.Translate = path =>
            {
                m_transfer.SessionId = new NodeId("replacement-session", 1);
                return TransferHarness.PathResponse(path);
            };
            using var destination = new MemoryStream();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 1).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(m_transfer.Paths, Has.Count.EqualTo(1));
            Assert.That(m_transfer.Calls, Is.Empty);
            Assert.That(destination.Length, Is.Zero);
            Assert.That(m_transfer.OwnsHandle, Is.False);
        }

        [Test]
        public void ReadMethodResolutionFailureStillClosesTheAcquiredFile()
        {
            m_transfer.Translate = path =>
            {
                TranslateBrowsePathsToNodeIdsResponse response = TransferHarness.PathResponse(path);
                if (path.StartingNode == TransferHarness.FileId &&
                    path.RelativePath.Elements[0].TargetName.Name == "Read")
                {
                    response.Results[0].StatusCode = StatusCodes.BadNoMatch;
                }
                return response;
            };
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 8))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task CloseMethodResolutionFailureStillAttemptsTheDeclarationClose()
        {
            m_transfer.Translate = path =>
            {
                TranslateBrowsePathsToNodeIdsResponse response = TransferHarness.PathResponse(path);
                if (path.StartingNode == TransferHarness.FileId &&
                    path.RelativePath.Elements[0].TargetName.Name == "Close")
                {
                    response.Results[0].StatusCode = StatusCodes.Uncertain;
                }
                return response;
            };
            using var destination = new MemoryStream();

            long copied = await m_transfer.Client.DownloadResultAsync(
                TransferHarness.MachineId, "selected", destination, 0);

            Assert.That(copied, Is.Zero);
            Assert.That(destination.Length, Is.Zero);
            Assert.That(m_transfer.Calls[^1].MethodId, Is.EqualTo(MethodIds.FileType_Close));
            AssertSuccessfulCleanup();
        }

        [Test]
        public async Task AsynchronousGenerationWaitsForReadTransferAndClosesAsync()
        {
            EnableCompletionStateMachine();
            m_transfer.Payload = MakePayload(2);
            m_transfer.ReadState = _ => new ValueTask<ReadResponse>(StateResponse(
                m_transfer.AttributeReads == 1
                    ? Opc.Ua.ObjectIds.FileTransferStateMachineType_Idle
                    : Opc.Ua.ObjectIds.FileTransferStateMachineType_ReadTransfer));
            using var destination = new MemoryStream();

            long copied = await m_transfer.Client.DownloadResultAsync(
                TransferHarness.MachineId, "selected", destination, 2);

            Assert.That(copied, Is.EqualTo(2));
            Assert.That(destination.ToArray(), Is.EqualTo(m_transfer.Payload.ToArray()));
            Assert.That(m_transfer.AttributeReads, Is.EqualTo(2));
            Assert.That(m_transfer.Paths, Has.Count.EqualTo(4));
            Assert.That(m_transfer.Paths[2].StartingNode, Is.EqualTo(TransferHarness.StateMachineId));
            Assert.That(m_transfer.Paths[2].RelativePath.Elements[0].TargetName.Name, Is.EqualTo("CurrentState"));
            Assert.That(m_transfer.Paths[2].RelativePath.Elements[1].TargetName.Name, Is.EqualTo("Id"));
            AssertSuccessfulCleanup();
        }

        [Test]
        public void GenerationErrorClosesWithoutReadingTheFile()
        {
            EnableCompletionStateMachine();
            m_transfer.ReadState = _ => new ValueTask<ReadResponse>(
                StateResponse(Opc.Ua.ObjectIds.FileTransferStateMachineType_Error));
            using var destination = new MemoryStream();

            Assert.ThrowsAsync<IOException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 2));

            Assert.That(m_transfer.AttributeReads, Is.EqualTo(1));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            AssertSuccessfulCleanup();
        }

        [Test]
        public void GenerationStateRequestBudgetIsFinite()
        {
            EnableCompletionStateMachine();
            m_transfer.ReadState = _ => new ValueTask<ReadResponse>(
                StateResponse(Opc.Ua.ObjectIds.FileTransferStateMachineType_Idle));
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 2))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(m_transfer.AttributeReads, Is.EqualTo(64));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            AssertSuccessfulCleanup();
        }

        [TestCaseSource(nameof(NonGoodPathStatuses))]
        public void NonGoodGenerationStateClosesTheAcquiredHandle(StatusCode status, bool serviceStatus)
        {
            EnableCompletionStateMachine();
            ReadResponse response = StateResponse(Opc.Ua.ObjectIds.FileTransferStateMachineType_ReadTransfer);
            if (serviceStatus)
            {
                response.ResponseHeader.ServiceResult = status;
            }
            else
            {
                response.Results = [DataValue.FromStatusCode(status)];
            }
            m_transfer.ReadState = _ => new ValueTask<ReadResponse>(response);
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 2))!;

            Assert.That(error.StatusCode, Is.EqualTo(status));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            AssertSuccessfulCleanup();
        }

        [Test]
        public void InvalidGenerationStateClosesTheAcquiredHandle()
        {
            EnableCompletionStateMachine();
            m_transfer.ReadState = _ => new ValueTask<ReadResponse>(new ReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new DataValue(Variant.From("ready"))]
            });
            using var destination = new MemoryStream();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 2))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(m_transfer.ReadLengths, Is.Empty);
            AssertSuccessfulCleanup();
        }

        [Test]
        public void GenerationWaitCancellationStillClosesUnderAnIndependentToken()
        {
            EnableCompletionStateMachine();
            using var cancellation = new CancellationTokenSource();
            m_transfer.ReadState = _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };
            using var destination = new MemoryStream();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_transfer.Client.DownloadResultAsync(
                    TransferHarness.MachineId, "selected", destination, 2, cancellation.Token));

            Assert.That(m_transfer.ReadLengths, Is.Empty);
            Assert.That(m_transfer.CloseToken, Is.Not.EqualTo(cancellation.Token));
            AssertSuccessfulCleanup();
        }

        private void EnableCompletionStateMachine()
        {
            m_transfer.GenerateResponse = TransferHarness.Response(
            [
                Variant.From(TransferHarness.FileId),
                Variant.From(37u),
                Variant.From(TransferHarness.StateMachineId)
            ]);
        }

        private static ReadResponse StateResponse(NodeId state)
        {
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new DataValue(Variant.From(state))]
            };
        }

        private static TranslateBrowsePathsToNodeIdsResponse TransferPathResponse()
        {
            return new TranslateBrowsePathsToNodeIdsResponse
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
                                TargetId = TransferHarness.TransferId,
                                RemainingPathIndex = uint.MaxValue
                            }
                        ]
                    }
                ]
            };
        }

        private static IEnumerable<TestCaseData> NonGoodPathStatuses()
        {
            foreach (StatusCode status in new StatusCode[] { StatusCodes.Uncertain, StatusCodes.BadNoMatch })
            {
                yield return new TestCaseData(status, true);
                yield return new TestCaseData(status, false);
            }
        }
    }
}
