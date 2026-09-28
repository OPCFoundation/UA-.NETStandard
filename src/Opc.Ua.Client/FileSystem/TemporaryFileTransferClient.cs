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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Client for the OPC UA <c>TemporaryFileTransferType</c>
    /// (Part 5 §C.5). Wraps the
    /// <see cref="TemporaryFileTransferTypeClient"/> proxy and exposes
    /// the read/commit lifecycles as <see cref="UaFileStream"/> /
    /// <see cref="UaTemporaryWriteFile"/> values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The temporary-file-transfer pattern is intentionally separate
    /// from <see cref="FileSystemClient"/> because its lifecycle does
    /// not fit the <see cref="System.IO"/> abstraction: the server
    /// allocates a transient file, the client streams data through it,
    /// and a final commit (or rollback) signals the server to either
    /// publish or discard the result.
    /// </para>
    /// <para>
    /// <see cref="UaTemporaryWriteFile"/> owns the close lifecycle for
    /// the underlying handle: exactly one terminal call is made,
    /// either <see cref="UaTemporaryWriteFile.CommitAsync"/> (which
    /// invokes <c>CloseAndCommit</c>) or
    /// <see cref="UaTemporaryWriteFile.DisposeAsync"/> (which falls
    /// back to <c>Close</c>). The wrapped <see cref="System.IO.Stream"/>
    /// itself does NOT close the server handle on disposal.
    /// </para>
    /// </remarks>
    public sealed class TemporaryFileTransferClient
    {
        /// <summary>
        /// Creates a new client rooted at the supplied
        /// <c>TemporaryFileTransferType</c> object.
        /// </summary>
        /// <param name="session">The OPC UA session.</param>
        /// <param name="temporaryFileTransferObjectId">NodeId of the
        /// <c>TemporaryFileTransferType</c> instance.</param>
        /// <param name="options">Optional configuration; defaults are
        /// applied when <c>null</c>.</param>
        public TemporaryFileTransferClient(
            ISession session,
            NodeId temporaryFileTransferObjectId,
            FileSystemClientOptions? options = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (temporaryFileTransferObjectId.IsNull)
            {
                throw new ArgumentNullException(nameof(temporaryFileTransferObjectId));
            }
            ObjectId = temporaryFileTransferObjectId;
            Options = (options ?? new FileSystemClientOptions()).Clone();
            Options.Validate();
            Proxy = new TemporaryFileTransferTypeClient(
                session,
                temporaryFileTransferObjectId,
                session.MessageContext.Telemetry);
        }

        /// <summary>The session.</summary>
        public ISession Session { get; }

        /// <summary>The NodeId of the underlying
        /// <c>TemporaryFileTransferType</c> object.</summary>
        public NodeId ObjectId { get; }

        /// <summary>The (cloned, immutable) configuration.</summary>
        public FileSystemClientOptions Options { get; }

        /// <summary>The underlying generated proxy.</summary>
        public TemporaryFileTransferTypeClient Proxy { get; }

        /// <summary>
        /// Asks the server to generate a temporary file for reading.
        /// Returns a <see cref="UaFileStream"/> wrapping the server
        /// handle; disposing the stream issues <c>Close</c> per the
        /// regular <c>FileType</c> lifecycle.
        /// </summary>
        /// <remarks>
        /// When the server generates the file asynchronously it returns a
        /// <c>FileTransferStateMachineType</c> object, and <c>Read</c> fails
        /// until its state reaches <c>ReadTransfer</c> (Part 20 §4.4.3). This
        /// method waits for that transition (bounded by
        /// <paramref name="ct"/>) before returning the stream.
        /// </remarks>
        /// <param name="generateOptions">Server-defined generation
        /// options; pass <c>default</c> for "no options".</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="IOException">The server reported that
        /// generating the file failed (the state machine entered
        /// <c>Error</c>).</exception>
        public async ValueTask<UaFileStream> GenerateFileForReadAsync(
            Variant generateOptions = default,
            CancellationToken ct = default)
        {
            (NodeId fileNodeId, uint handle, NodeId completionStateMachine) = await Proxy
                .GenerateFileForReadAsync(generateOptions, ct)
                .ConfigureAwait(false);

            var fileProxy = new FileTypeClient(
                Session,
                fileNodeId,
                Session.MessageContext.Telemetry);

            try
            {
                if (!completionStateMachine.IsNull)
                {
                    await WaitForReadTransferAsync(completionStateMachine, ct)
                        .ConfigureAwait(false);
                }
                return new UaFileStream(
                    fileProxy,
                    handle,
                    UaFileMode.Read,
                    initialLength: 0,
                    initialPosition: 0,
                    chunkSize: Options.ChunkSize);
            }
            catch
            {
                try
                {
                    await fileProxy.CloseAsync(handle, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup.
                }
                throw;
            }
        }

        /// <summary>
        /// Polls <c>CurrentState/Id</c> of the completion state machine until
        /// the asynchronous generation reaches <c>ReadTransfer</c>.
        /// </summary>
        private async ValueTask WaitForReadTransferAsync(
            NodeId stateMachine,
            CancellationToken ct)
        {
            ArrayOf<BrowsePath> browsePaths = new[]
            {
                new BrowsePath
                {
                    StartingNode = stateMachine,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(BrowseNames.CurrentState)
                            },
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(BrowseNames.Id)
                            }
                        ]
                    }
                }
            }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, browsePaths);
            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode) || result.Targets.Count == 0)
            {
                // The state cannot be observed; Read reports the outcome.
                return;
            }

            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId
                {
                    NodeId = ExpandedNodeId.ToNodeId(
                        result.Targets[0].TargetId,
                        Session.MessageContext.NamespaceUris),
                    AttributeId = Attributes.Value
                }
            }.ToArrayOf();
            TimeSpan delay = TimeSpan.FromMilliseconds(50);
            while (true)
            {
                ReadResponse read = await Session.ReadAsync(
                    null,
                    0.0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct).ConfigureAwait(false);
                ClientBase.ValidateResponse(read.Results, nodesToRead);
                DataValue value = read.Results[0];
                if (StatusCode.IsBad(value.StatusCode) ||
                    !value.WrappedValue.TryGetValue(out NodeId state))
                {
                    return;
                }
                // Part 20 4.4.3: Read fails until the state reaches
                // ReadTransfer. Idle means generation has not started yet,
                // so keep waiting (bounded by the caller's token).
                if (state.Equals(ObjectIds.FileTransferStateMachineType_ReadTransfer))
                {
                    return;
                }
                if (state.Equals(ObjectIds.FileTransferStateMachineType_Error))
                {
                    throw new IOException(
                        "The server failed to generate the temporary file for reading " +
                        "(the FileTransferStateMachine entered the Error state).");
                }
                await Task.Delay(delay, ct).ConfigureAwait(false);
                if (delay < s_maxStatePollDelay)
                {
                    delay = TimeSpan.FromTicks(Math.Min(
                        delay.Ticks * 2,
                        s_maxStatePollDelay.Ticks));
                }
            }
        }

        private static readonly TimeSpan s_maxStatePollDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Asks the server to allocate a temporary file for writing.
        /// The returned <see cref="UaTemporaryWriteFile"/> owns the
        /// close lifecycle: call
        /// <see cref="UaTemporaryWriteFile.CommitAsync"/> to publish
        /// the file or rely on <see cref="UaTemporaryWriteFile.DisposeAsync"/>
        /// to roll back.
        /// </summary>
        /// <param name="generateOptions">Server-defined generation
        /// options.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<UaTemporaryWriteFile> GenerateFileForWriteAsync(
            Variant generateOptions = default,
            CancellationToken ct = default)
        {
            (NodeId fileNodeId, uint handle) = await Proxy
                .GenerateFileForWriteAsync(generateOptions, ct)
                .ConfigureAwait(false);

            var fileProxy = new FileTypeClient(
                Session,
                fileNodeId,
                Session.MessageContext.Telemetry);

            return new UaTemporaryWriteFile(
                Proxy,
                fileProxy,
                fileNodeId,
                handle,
                Options.ChunkSize);
        }
    }
}
