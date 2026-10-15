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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    public sealed partial class TemporaryFileTransferClient
    {
        /// <summary>
        /// Generates one temporary read file and copies its contents to a
        /// caller-owned stream without exceeding a byte or request limit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reads request at most 64 KiB (or the smaller configured chunk size),
        /// with at most 1024 file reads, including the end-of-file probe. A short
        /// read is not treated as end-of-file. The first byte exceeding
        /// <paramref name="maxBytes"/> stops the transfer before that chunk is
        /// written. File size metadata is not used. Zero permits only an empty
        /// file. Asynchronous generation permits at most 64 state reads.
        /// </para>
        /// <para>
        /// The generation call is never replayed. Good service and method
        /// statuses and correctly typed outputs are required. Once a valid file
        /// NodeId and UInt32 handle have been returned, exactly one Close is
        /// attempted, even if another output, reading, writing or cancellation
        /// fails. Close uses the FileType declaration method directly, with a
        /// fresh, independent five-second deadline. A managed session uses one
        /// captured <see cref="SessionRequestContext"/> throughout the transfer
        /// and cleanup; recovery invalidates it rather than sending an old
        /// handle through a replacement session.
        /// </para>
        /// <para>
        /// The destination and borrowed session are never closed or disposed.
        /// On failure the destination may contain a partial download; the caller
        /// is responsible for discarding it. Cancellation is passed to generation
        /// rather than abandoning its task, so a late returned handle is still
        /// closed.
        /// </para>
        /// </remarks>
        /// <param name="generateOptions">Options identifying the file to generate.</param>
        /// <param name="destination">The writable, caller-owned destination.</param>
        /// <param name="maxBytes">The non-negative maximum number of payload bytes.</param>
        /// <param name="ct">Cancels generation and copying, but not cleanup.</param>
        /// <returns>The number of bytes written after successful cleanup.</returns>
        /// <exception cref="ArgumentNullException">The destination is null.</exception>
        /// <exception cref="ArgumentException">The destination is not writable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The byte limit is negative or a file-system option is outside its permitted range.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// A limit is exceeded, a response is invalid, a session is replaced,
        /// or a service or method reports a non-Good status.
        /// </exception>
        /// <exception cref="IOException">Generation or destination writing fails.</exception>
        /// <exception cref="OperationCanceledException">Copying or cleanup is cancelled.</exception>
        /// <exception cref="AggregateException">
        /// Transfer and cleanup both fail. The first inner exception is the
        /// transfer failure; the second is the cleanup failure.
        /// </exception>
        public async ValueTask<long> CopyToAsync(
            Variant generateOptions,
            Stream destination,
            long maxBytes,
            CancellationToken ct = default)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination must be writable.", nameof(destination));
            }
            if (maxBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
            FileSystemClientOptions options = Options.Clone();
            options.Validate();
            int chunkSize = Math.Min(options.ChunkSize, MaximumBoundedReadChunkSize);
            ct.ThrowIfCancellationRequested();

            SessionRequestContext? requestContext = Session is ManagedSession
                ? await SessionRequestContext.CaptureAsync(Session, ct).ConfigureAwait(false)
                : null;
            try
            {
                return await CopyGeneratedFileAsync(
                    requestContext?.Session ?? Session, generateOptions, destination, maxBytes, chunkSize, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (requestContext != null)
                {
                    await requestContext.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<long> CopyGeneratedFileAsync(
            ISession session,
            Variant generateOptions,
            Stream destination,
            long maxBytes,
            int chunkSize,
            CancellationToken ct)
        {
            NodeId sessionId = session.SessionId;
            ITelemetryContext telemetry = session.MessageContext.Telemetry;
            var transfer = new TemporaryFileTransferTypeClient(session, ObjectId, telemetry);
            NodeId generateMethod = await ResolveBoundedReadMethodAsync(
                transfer, sessionId, BrowseNames.GenerateFileForRead, ct).ConfigureAwait(false);
            CallResponse generatedResponse = await SendBoundedReadMethodAsync(
                transfer,
                sessionId,
                generateMethod,
                [generateOptions],
                ct).ConfigureAwait(false);
            ArrayOf<Variant> generated =
                generatedResponse.Results.Count > 0 && generatedResponse.Results[0] is { } generationResult
                    ? generationResult.OutputArguments
                    : default;

            if (generated.Count < 2 ||
                !generated[0].TryGetValue(out NodeId fileNodeId) ||
                fileNodeId.IsNull ||
                !generated[1].TryGetValue(out uint handle))
            {
                _ = GetBoundedReadOutputs(generatedResponse, 1);
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "GenerateFileForRead did not return a valid file NodeId and UInt32 handle.");
            }

            var file = new FileTypeClient(session, fileNodeId, telemetry);
            Exception? transferFailure = null;
            long copied = 0;
            try
            {
                ct.ThrowIfCancellationRequested();
                _ = GetBoundedReadOutputs(generatedResponse, 1);
                if (generated.Count != 3 ||
                    !generated[2].TryGetValue(out NodeId completionStateMachine))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "GenerateFileForRead did not return a valid completion state machine NodeId.");
                }
                if (!completionStateMachine.IsNull)
                {
                    await WaitForBoundedReadTransferAsync(session, sessionId, completionStateMachine, ct)
                        .ConfigureAwait(false);
                }
                copied = await CopyBoundedFileAsync(
                    file,
                    sessionId,
                    handle,
                    destination,
                    maxBytes,
                    chunkSize,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                transferFailure = ex;
            }

            using var cleanup = new CancellationTokenSource(s_boundedReadCleanupTimeout);
            try
            {
                await CloseBoundedReadFileAsync(file, sessionId, handle, cleanup.Token)
                    .AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception closeFailure) when (transferFailure is not null)
            {
                throw new AggregateException(
                    "The temporary file transfer and its cleanup both failed.",
                    transferFailure,
                    closeFailure);
            }

            if (transferFailure is not null)
            {
                ExceptionDispatchInfo.Capture(transferFailure).Throw();
            }
            return copied;
        }

        private static async ValueTask<long> CopyBoundedFileAsync(
            FileTypeClient file,
            NodeId sessionId,
            uint handle,
            Stream destination,
            long maxBytes,
            int chunkSize,
            CancellationToken ct)
        {
#if !NET
            var buffer = new byte[chunkSize];
#endif
            NodeId readMethod = await ResolveBoundedReadMethodAsync(
                file, sessionId, BrowseNames.Read, ct).ConfigureAwait(false);
            long copied = 0;
            for (int request = 0; request < MaximumBoundedFileReads; request++)
            {
                long remaining = maxBytes - copied;
                int length = remaining < chunkSize ? (int)remaining + 1 : chunkSize;
                CallResponse response = await SendBoundedReadMethodAsync(
                    file,
                    sessionId,
                    readMethod,
                    [Variant.From(handle), Variant.From(length)],
                    ct).ConfigureAwait(false);
                ArrayOf<Variant> output = GetBoundedReadOutputs(response, 2);
                EnsureBoundedReadSession(file.Session, sessionId);
                if (output.Count != 1 || !output[0].TryGetValue(out ByteString data))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "File Read did not return exactly one ByteString.");
                }
                if (data.Length > length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "File Read returned more bytes than requested.");
                }
                if (data.Length > remaining)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The temporary file exceeds the download byte limit.");
                }
                if (data.IsEmpty)
                {
                    return copied;
                }
                ct.ThrowIfCancellationRequested();
#if NET
                await destination.WriteAsync(data.Memory, ct).ConfigureAwait(false);
#else
                data.Span.CopyTo(buffer);
                await destination.WriteAsync(buffer, 0, data.Length, ct).ConfigureAwait(false);
#endif
                copied += data.Length;
            }
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded,
                "The temporary file exceeds the download read-request limit.");
        }

        private static async ValueTask CloseBoundedReadFileAsync(
            FileTypeClient file,
            NodeId sessionId,
            uint handle,
            CancellationToken ct)
        {
            CallResponse response = await SendBoundedReadMethodAsync(
                file, sessionId, MethodIds.FileType_Close, [Variant.From(handle)], ct).ConfigureAwait(false);
            ArrayOf<Variant> closed = GetBoundedReadOutputs(response, 1);
            if (closed.Count != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "File Close returned unexpected output arguments.");
            }
        }

        private static ValueTask<NodeId> ResolveBoundedReadMethodAsync(
            ObjectTypeClient proxy,
            NodeId sessionId,
            string browseName,
            CancellationToken ct)
        {
            return ResolveBoundedReadPathAsync(
                proxy.Session,
                sessionId,
                proxy.ObjectId,
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(browseName)
                    }
                ],
                ct);
        }

        private static async ValueTask WaitForBoundedReadTransferAsync(
            ISession session,
            NodeId sessionId,
            NodeId stateMachine,
            CancellationToken ct)
        {
            NodeId stateNode = await ResolveBoundedReadPathAsync(
                session,
                sessionId,
                stateMachine,
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(BrowseNames.CurrentState)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(BrowseNames.Id)
                    }
                ],
                ct).ConfigureAwait(false);
            ArrayOf<ReadValueId> reads = [new ReadValueId { NodeId = stateNode, AttributeId = Attributes.Value }];
            for (int request = 0; request < MaximumBoundedStateReads; request++)
            {
                ct.ThrowIfCancellationRequested();
                EnsureBoundedReadSession(session, sessionId);
                ReadResponse? response = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    reads,
                    ct).ConfigureAwait(false);
                if (response is null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                }
                ValidateBoundedReadHeader(response.ResponseHeader);
                ClientBase.ValidateResponse(response.Results, reads);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, reads);
                DataValue value = response.Results[0];
                RequireBoundedReadGoodStatus(value.StatusCode);
                if (!value.WrappedValue.TryGetValue(out NodeId state) || state.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "The completion state is not a valid NodeId.");
                }
                if (state == ObjectIds.FileTransferStateMachineType_ReadTransfer)
                {
                    return;
                }
                if (state == ObjectIds.FileTransferStateMachineType_Error)
                {
                    throw new IOException("The server failed to generate the temporary read file.");
                }
                if (request + 1 < MaximumBoundedStateReads)
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded,
                "Temporary file generation exceeds the state-read request limit.");
        }

        private static async ValueTask<NodeId> ResolveBoundedReadPathAsync(
            ISessionClient session,
            NodeId sessionId,
            NodeId startingNode,
            ArrayOf<RelativePathElement> elements,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            EnsureBoundedReadSession(session, sessionId);
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath { StartingNode = startingNode, RelativePath = new RelativePath { Elements = elements } }
            ];
            TranslateBrowsePathsToNodeIdsResponse? response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct).ConfigureAwait(false);
            if (response is null)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }
            ValidateBoundedReadHeader(response.ResponseHeader);
            ClientBase.ValidateResponse(response.Results, paths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths);
            BrowsePathResult path = response.Results[0];
            RequireBoundedReadGoodStatus(path.StatusCode);
            if (path.Targets.Count != 1 ||
                path.Targets[0].RemainingPathIndex != uint.MaxValue ||
                path.Targets[0].TargetId.ServerIndex != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The file transfer path did not resolve to exactly one complete local node.");
            }
            NodeId node = ExpandedNodeId.ToNodeId(path.Targets[0].TargetId, session.MessageContext.NamespaceUris);
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            return node;
        }

        private static async ValueTask<CallResponse> SendBoundedReadMethodAsync(
            ObjectTypeClient proxy,
            NodeId sessionId,
            NodeId methodId,
            ArrayOf<Variant> arguments,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            EnsureBoundedReadSession(proxy.Session, sessionId);
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest
                {
                    ObjectId = proxy.ObjectId,
                    MethodId = methodId,
                    InputArguments = arguments
                }
            ];

            // Generated methods discard non-Bad status metadata and may retry.
            // This opt-in lifecycle must inspect the original response exactly
            // once, including partially malformed outputs that contain a handle.
            CallResponse? response = await proxy.Session.CallAsync(null, requests, ct).ConfigureAwait(false);
            if (response is null)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }
            return response;
        }

        private static ArrayOf<Variant> GetBoundedReadOutputs(CallResponse response, int inputCount)
        {
            ValidateBoundedReadHeader(response.ResponseHeader);
            if (response.Results.Count != 1 || response.Results[0] is not { } result)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The server did not return exactly one method result.");
            }
            RequireBoundedReadGoodStatus(result.StatusCode);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, response.Results);
            if (result.InputArgumentResults.Count != 0)
            {
                if (result.InputArgumentResults.Count != inputCount)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "The method returned an invalid number of input argument statuses.");
                }
                ClientBase.ValidateDiagnosticInfos(result.InputArgumentDiagnosticInfos, result.InputArgumentResults);
                for (int ii = 0; ii < result.InputArgumentResults.Count; ii++)
                {
                    RequireBoundedReadGoodStatus(result.InputArgumentResults[ii]);
                }
            }
            return result.OutputArguments;
        }

        private static void ValidateBoundedReadHeader(ResponseHeader? header)
        {
            if (header is null)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }
            RequireBoundedReadGoodStatus(header.ServiceResult);
        }

        private static void RequireBoundedReadGoodStatus(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status);
            }
        }

        private static void EnsureBoundedReadSession(ISessionClient session, NodeId sessionId)
        {
            if (session.SessionId != sessionId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSessionIdInvalid,
                    "The session that owns the temporary file handle has been replaced.");
            }
        }

        private const int MaximumBoundedReadChunkSize = 64 * 1024;
        private const int MaximumBoundedFileReads = 1024;
        private const int MaximumBoundedStateReads = 64;
        private static readonly TimeSpan s_boundedReadCleanupTimeout = TimeSpan.FromSeconds(5);
    }
}
