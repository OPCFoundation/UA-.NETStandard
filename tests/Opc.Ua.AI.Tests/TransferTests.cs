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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies the chunked path for payloads too large to pass inline.
    /// </summary>
    /// <remarks>
    /// The property worth protecting is that nothing a caller is entitled to
    /// changes because the bytes arrived in chunks. A large payload is a transport
    /// concern; if the transfer path dropped <c>ModelUsed</c> the audit trail would
    /// have a hole exactly where the largest requests are.
    /// </remarks>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class TransferTests
    {
        [Test]
        public async Task AnOversizePayloadIsRefusedInlineAndNamesTheTransferAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);
            byte[] oversize = new byte[InlineLimit + 1];

            InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                nm.PrimaryDeploymentId,
                ByteString.From(oversize),
                string.Empty,
                "application/octet-stream",
                ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                5000,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.TransferRequired, Is.True);
                Assert.That(result.Transfer, Is.Not.EqualTo(NodeId.Null));

                // The refusal names the transfer that will carry it, so a caller
                // that reads the answer can act on it without a second round trip
                // to work out what to do next.
                Assert.That(
                    nm.FindPredefinedNode<InferenceTransferState>(result.Transfer),
                    Is.Not.Null);
            });
        }

        [Test]
        public async Task ATransferCarriesThePayloadAndReportsTheModelUsedAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            BeginTransferMethodStateResult begun = await deployment.BeginTransfer!.OnCallAsync!(
                nm.SystemContext,
                deployment.BeginTransfer,
                nm.PrimaryDeploymentId,
                "application/json",
                1024,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(begun.Accepted, Is.True);

            var transfer = nm.FindPredefinedNode<InferenceTransferState>(begun.Transfer);

            WriteRequest(nm, transfer, "{\"prompt\":\"hello\"}");

            ExecuteMethodStateResult executed = await transfer.Execute!.OnCallAsync!(
                nm.SystemContext,
                transfer.Execute,
                transfer.NodeId,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(executed.Accepted, Is.True);
                Assert.That(transfer.State!.Value, Is.EqualTo(TransferStateEnum.Completed));

                // The same output an inline call would have produced.
                Assert.That(transfer.ModelUsed!.Value, Is.Not.EqualTo(NodeId.Null));
                Assert.That(transfer.ResponseContentType!.Value, Is.EqualTo("application/json"));
                Assert.That(ReadResponse(nm, transfer), Does.Contain("primary"));
            });
        }

        [Test]
        public async Task ATransferLargerThanTheServerAcceptsIsRefusedBeforeAnyBytesArriveAsync()
        {
            using AINodeManager nm = await CreateAsync(
                new AIOptions { MaxTransferSize = 4096 })
                .ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            BeginTransferMethodStateResult begun = await deployment.BeginTransfer!.OnCallAsync!(
                nm.SystemContext,
                deployment.BeginTransfer,
                nm.PrimaryDeploymentId,
                "application/json",
                4097,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(begun.Accepted, Is.False);
                Assert.That(begun.Transfer, Is.EqualTo(NodeId.Null));
                Assert.That(
                    (StatusCode)begun.ServiceResult.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadRequestTooLarge));
            });
        }

        [Test]
        public async Task TheResponseFileIsNotWritableAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            BeginTransferMethodStateResult begun = await deployment.BeginTransfer!.OnCallAsync!(
                nm.SystemContext,
                deployment.BeginTransfer,
                nm.PrimaryDeploymentId,
                "application/json",
                16,
                CancellationToken.None).ConfigureAwait(false);

            var transfer = nm.FindPredefinedNode<InferenceTransferState>(begun.Transfer);

            uint handle = 0;
            const byte writeEraseExisting = 6;

            ServiceResult opened = transfer.Response!.Open!.OnCall!(
                nm.SystemContext,
                transfer.Response.Open,
                transfer.Response.NodeId,
                writeEraseExisting,
                ref handle);

            // A client that could overwrite a model's answer could forge one.
            Assert.That(ServiceResult.IsGood(opened), Is.False);
        }

        [Test]
        public async Task ATransferAbortedMidInferenceDoesNotWriteIntoDisposedBuffersAsync()
        {
            var backend = new BlockingFakeBackend();

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(backend),
                    new AIOptions { EnableFallback = false },
                    new InferenceBackendOptions { MaxInlinePayloadSize = InlineLimit })
                .ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            BeginTransferMethodStateResult begun = await deployment.BeginTransfer!.OnCallAsync!(
                nm.SystemContext,
                deployment.BeginTransfer,
                nm.PrimaryDeploymentId,
                "application/json",
                64,
                CancellationToken.None).ConfigureAwait(false);

            var transfer = nm.FindPredefinedNode<InferenceTransferState>(begun.Transfer);
            WriteRequest(nm, transfer, "{}");

            // Execute starts and blocks inside the backend.
            Task<ExecuteMethodStateResult> executing = transfer.Execute!.OnCallAsync!(
                nm.SystemContext,
                transfer.Execute,
                transfer.NodeId,
                CancellationToken.None).AsTask();

            await backend.Entered.ConfigureAwait(false);

            // Abort while it is in flight. This removes the entry and disposes the
            // buffers the completing call is about to write into.
            transfer.Abort!.OnCallMethod2Async!(
                nm.SystemContext,
                transfer.Abort,
                transfer.NodeId,
                ArrayOf<Variant>.Empty,
                [],
                CancellationToken.None).AsTask().Wait(TimeSpan.FromSeconds(5));

            backend.Release();

            ExecuteMethodStateResult result = await executing.ConfigureAwait(false);

            // The answer is dropped rather than written into a disposed buffer,
            // which is what the caller that aborted was asking for. Before the
            // liveness re-check this threw ObjectDisposedException out of a Method
            // call, which no client can do anything sensible with.
            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(
                    (StatusCode)result.ServiceResult.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            });
        }

        /// <summary>
        /// A backend that lets a test hold an inference open.
        /// </summary>
        private sealed class BlockingFakeBackend : IInferenceBackend
        {
            private readonly TaskCompletionSource m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Entered => m_entered.Task;

            public void Release()
            {
                m_release.TrySetResult();
            }

            public InferenceSite Site => InferenceSite.OnServer;

            public ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
                string? filter, uint maxResults, CancellationToken ct)
            {
                return ValueTask.FromResult<IReadOnlyList<BackendModel>>([]);
            }

            public async ValueTask<InferenceResult> InvokeAsync(
                InferenceRequest request, CancellationToken ct)
            {
                m_entered.TrySetResult();
                await m_release.Task.ConfigureAwait(false);

                return new InferenceResult
                {
                    Ok = true,
                    Payload = Encoding.UTF8.GetBytes("{\"ok\":true}"),
                    ContentType = "application/json"
                };
            }

            public ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
            {
                return ValueTask.FromResult(new BackendProbe { Reachable = true });
            }
        }

        private const uint InlineLimit = 512;

        private static Task<AINodeManager> CreateAsync(
            AIOptions? options = null)
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(new FakeInferenceBackend("primary")),
                options ?? new AIOptions { EnableFallback = false },
                new InferenceBackendOptions { MaxInlinePayloadSize = InlineLimit });
        }

        private static void WriteRequest(
            AINodeManager nm,
            InferenceTransferState transfer,
            string body)
        {
            uint handle = 0;
            const byte writeEraseExisting = 6;

            ServiceResult opened = transfer.Request!.Open!.OnCall!(
                nm.SystemContext,
                transfer.Request.Open,
                transfer.Request.NodeId,
                writeEraseExisting,
                ref handle);

            Assert.That(ServiceResult.IsGood(opened), Is.True);

            ServiceResult written = transfer.Request.Write!.OnCall!(
                nm.SystemContext,
                transfer.Request.Write,
                transfer.Request.NodeId,
                handle,
                ByteString.From(Encoding.UTF8.GetBytes(body)));

            Assert.That(ServiceResult.IsGood(written), Is.True);

            _ = transfer.Request.Close!.OnCall!(
                nm.SystemContext,
                transfer.Request.Close,
                transfer.Request.NodeId,
                handle);
        }

        private static string ReadResponse(
            AINodeManager nm,
            InferenceTransferState transfer)
        {
            uint handle = 0;
            const byte read = 1;

            _ = transfer.Response!.Open!.OnCall!(
                nm.SystemContext,
                transfer.Response.Open,
                transfer.Response.NodeId,
                read,
                ref handle);

            ByteString data = default;

            _ = transfer.Response.Read!.OnCall!(
                nm.SystemContext,
                transfer.Response.Read,
                transfer.Response.NodeId,
                handle,
                4096,
                ref data);

            _ = transfer.Response.Close!.OnCall!(
                nm.SystemContext,
                transfer.Response.Close,
                transfer.Response.NodeId,
                handle);

            return Encoding.UTF8.GetString(data.Span);
        }
    }
}
