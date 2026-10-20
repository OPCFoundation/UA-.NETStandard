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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.FileSystem;
using static Opc.Ua.PlasticsRubber.Server.ProductionDatasetValidation;

namespace Opc.Ua.PlasticsRubber.Server
{
    public sealed partial class ProductionDatasetBinding
    {
        /// <summary>
        /// Specializes the standard transfer with generated chapter 20 options and typed method callbacks.
        /// </summary>
        private void BindTransfer()
        {
            TemporaryFileTransferState transfer = Node.ProductionDatasetTransfer!;
            transfer.ClientProcessingTimeout!.Value = m_options.TransferTimeout.TotalMilliseconds;
            transfer.EventNotifier |= EventNotifiers.SubscribeToEvents;
            Node.AddNotifier(m_owner.SystemContext, ReferenceTypeIds.HasEventSource, false, transfer);
            transfer.AddNotifier(m_owner.SystemContext, ReferenceTypeIds.HasEventSource, true, Node);
            GenerateFileForReadMethodState read = transfer.GenerateFileForRead!;
            GenerateFileForWriteMethodState write = transfer.GenerateFileForWrite!;
            CloseAndCommitMethodState commit = transfer.CloseAndCommit!;
            Own(read, read.OnCall != null || read.OnCallAsync != null);
            Own(write, write.OnCall != null || write.OnCallAsync != null);
            Own(commit, commit.OnCall != null || commit.OnCallAsync != null);
            SetGenerateArgument(read, DataTypes.ProductionDatasetReadOptionsType);
            SetGenerateArgument(write, DataTypes.ProductionDatasetWriteOptionsType);
            read.OnCallAsync = async (context, _, target, options, ct) =>
            {
                ProductionDatasetTransferSlot? slot = null;
                ServiceResult result = await InvokeAsync(
                    context, target, transfer, read, async (request, token) =>
                    {
                        if (!options.TryGetStructure<ProductionDatasetReadOptionsType>(
                            request.SystemContext.AsMessageContext(), out ProductionDatasetReadOptionsType? value))
                        {
                            throw Error(StatusCodes.BadTypeMismatch, "ProductionDatasetReadOptionsType is required.");
                        }
                        Read(value, m_options);
                        SessionId(context);
                        await ExpireCoreAsync().ConfigureAwait(false);
                        CheckTransferCapacity();
                        ProductionDatasetDocument document = await m_provider.ReadAsync(
                            request, (ProductionDatasetReadOptionsType)value.Clone(), token).ConfigureAwait(false);
                        if (document == null || document.Content.IsNull)
                        {
                            throw Error(StatusCodes.BadNoDataAvailable, "The authority did not provide a file.");
                        }
                        Information(document.Information, m_options);
                        if (document.Content.Length > m_options.MaximumFileBytes)
                        {
                            throw Error(StatusCodes.BadEncodingLimitsExceeded, "The dataset file is too large.");
                        }
                        slot = await CreateTransferAsync(
                            context, document.Content, null, Copy(document.Information), token).ConfigureAwait(false);
                    }, ct).ConfigureAwait(false);
                return new GenerateFileForReadMethodStateResult
                {
                    ServiceResult = result,
                    FileNodeId = slot == null ? NodeId.Null : slot.Binding.File.NodeId,
                    FileHandle = slot?.Handle ?? 0,
                    CompletionStateMachine = NodeId.Null
                };
            };
            write.OnCallAsync = async (context, _, target, options, ct) =>
            {
                ProductionDatasetTransferSlot? slot = null;
                ServiceResult result = await InvokeAsync(
                    context, target, transfer, write, async (request, token) =>
                    {
                        if (!options.TryGetStructure<ProductionDatasetWriteOptionsType>(
                            request.SystemContext.AsMessageContext(), out ProductionDatasetWriteOptionsType? value))
                        {
                            throw Error(StatusCodes.BadTypeMismatch, "ProductionDatasetWriteOptionsType is required.");
                        }
                        Write(value, m_options);
                        SessionId(context);
                        await ExpireCoreAsync().ConfigureAwait(false);
                        CheckTransferCapacity();
                        await EnsureMutableAsync(request, value.Storage, token).ConfigureAwait(false);
                        slot = await CreateTransferAsync(
                            context, ByteString.Empty, (ProductionDatasetWriteOptionsType)value.Clone(), null, token)
                            .ConfigureAwait(false);
                    }, ct).ConfigureAwait(false);
                return new GenerateFileForWriteMethodStateResult
                {
                    ServiceResult = result,
                    FileNodeId = slot == null ? NodeId.Null : slot.Binding.File.NodeId,
                    FileHandle = slot?.Handle ?? 0
                };
            };
            commit.OnCallAsync = async (context, _, target, handle, ct) =>
                new CloseAndCommitMethodStateResult
                {
                    ServiceResult = await InvokeAsync(
                        context, target, transfer, commit, async (request, token) =>
                        {
                            ProductionDatasetTransferSlot slot = await RequireTransferAsync(
                                request.SystemContext, handle).ConfigureAwait(false);
                            if (slot.WriteOptions == null)
                            {
                                throw Error(StatusCodes.BadInvalidArgument, "CloseAndCommit requires a write handle.");
                            }
                            slot.CommittingContext = request.SystemContext;
                            try
                            {
                                await slot.Binding.CommitAsync(slot.Owner, handle, token).ConfigureAwait(false);
                            }
                            finally
                            {
                                slot.CommittingContext = null;
                            }
                            await RetireAsync(slot).ConfigureAwait(false);
                        }, ct, publish: true).ConfigureAwait(false),
                    CompletionStateMachine = NodeId.Null
                };
        }

        /// <summary>
        /// Advertises the concrete input datatype required by Part 20, not unrestricted BaseDataType.
        /// </summary>
        private void SetGenerateArgument(MethodState method, uint dataType)
        {
            method.InputArguments!.Value =
            [
                new Argument
                {
                    Name = "generateOptions",
                    DataType = NodeId.Create(dataType, Namespaces.PlasticsRubber, m_owner.SystemContext.NamespaceUris),
                    ValueRank = ValueRanks.Scalar,
                    Description = new LocalizedText("OPC 40083 production dataset transfer options.")
                }
            ];
        }

        /// <summary>
        /// Enforces the bound before reading device content or allocating a temporary file.
        /// </summary>
        private void CheckTransferCapacity()
        {
            if (m_transfers.Count >= m_options.MaximumTransfers)
            {
                throw Error(StatusCodes.BadTooManyOperations, "The temporary dataset file limit was reached.");
            }
        }

        /// <summary>
        /// Allocates a single pre-opened session-owned handle using the shared FileType binder.
        /// </summary>
        private async ValueTask<ProductionDatasetTransferSlot> CreateTransferAsync(
            ISystemContext context,
            ByteString content,
            ProductionDatasetWriteOptionsType? options,
            ProductionDatasetInformationType? information,
            CancellationToken ct)
        {
            TemporaryFileTransferState transfer = Node.ProductionDatasetTransfer!;
            FileState file = m_owner.SystemContext.CreateInstanceOfFileType(
                transfer, new QualifiedName(
                    "DatasetTransfer-" + Guid.NewGuid().ToString("N"), Node.NodeId.NamespaceIndex));
            file.DisplayName = new LocalizedText("Temporary production dataset");
            file.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            ProductionDatasetTransferSlot? slot = null;
            IFileTypeBinding binding = m_fileBinder.Bind(
                file, m_owner.SystemContext, ByteString.From(content.Span.ToArray()),
                options == null ? null : (bytes, token) =>
                    CommitTransferAsync(
                        slot ?? throw Error(StatusCodes.BadInvalidState, "The transfer is not initialized."),
                        bytes, token),
                ownerSession: SessionId(context),
                maximumBytes: m_options.MaximumFileBytes);
            bool published = false;
            try
            {
                uint handle = 0;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    handle = await binding.OpenAsync(SessionId(context), options == null ? (byte)1 : (byte)6, ct)
                        .ConfigureAwait(false);
                    if (!m_transfers.ContainsKey(handle))
                    {
                        break;
                    }
                    await binding.CloseAsync(SessionId(context), handle, CancellationToken.None).ConfigureAwait(false);
                    handle = 0;
                }
                if (handle == 0)
                {
                    throw Error(StatusCodes.BadResourceUnavailable, "A unique transfer handle could not be allocated.");
                }
                slot = new ProductionDatasetTransferSlot(
                    binding, handle, SessionId(context), options, information, m_clock.GetTimestamp());
                WrapFileMethods(slot);
                GuardFileMetadata(slot);
                transfer.AddChild(file);
                await m_owner.AddPredefinedNodeAsync(file, ct).ConfigureAwait(false);
                m_transfers.Add(handle, slot);
                published = true;
                return slot;
            }
            finally
            {
                if (!published)
                {
                    try
                    {
                        await binding.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        transfer.RemoveChild(file);
                        await m_owner.DeleteNodeAsync(m_owner.SystemContext, file.NodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Adds common authorization and transfer expiry without duplicating FileType stream semantics.
        /// </summary>
        private void WrapFileMethods(ProductionDatasetTransferSlot slot)
        {
            FileState file = slot.Binding.File;
            foreach (string name in s_fileMethods)
            {
                MethodState method = file.FindChild(m_owner.SystemContext, new QualifiedName(name)) as MethodState
                    ?? throw new InvalidOperationException("The generated FileType method is missing.");
                GenericMethodCalledEventHandler2Async original = method.OnCallMethod2Async
                    ?? throw new InvalidOperationException("The FileType binder did not install its method handler.");
                method.OnCallMethod2Async = (context, called, target, arguments, output, ct) =>
                    InvokeAsync(context, target, file, method, async (request, token) =>
                    {
                        await RequireTransferAsync(request.SystemContext, slot.Handle).ConfigureAwait(false);
                        if (name == "Open")
                        {
                            throw Error(StatusCodes.BadInvalidState, "A temporary transfer is already open.");
                        }
                        if (arguments.Count == 0 || !arguments[0].TryGetValue(out uint handle) || handle != slot.Handle)
                        {
                            throw Error(
                                StatusCodes.BadInvalidArgument, "This file belongs to another transfer handle.");
                        }
                        if (name == "Write")
                        {
                            if (slot.WriteOptions == null)
                            {
                                throw Error(StatusCodes.BadNotWritable, "This is a read transfer.");
                            }
                            await EnsureMutableAsync(request, slot.WriteOptions.Storage, token).ConfigureAwait(false);
                        }
                        ServiceResult result = await original(context, called, target, arguments, output, token)
                            .ConfigureAwait(false);
                        ThrowIfBad(result);
                        if (name == "Close")
                        {
                            await RetireAsync(slot).ConfigureAwait(false);
                        }
                    }, ct);
            }
        }

        /// <summary>
        /// Prevents foreign sessions from learning transfer sizes or writability through value reads.
        /// </summary>
        private void GuardFileMetadata(ProductionDatasetTransferSlot slot)
        {
            var children = new List<BaseInstanceState>();
            slot.Binding.File.GetChildren(m_owner.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is not BaseVariableState variable)
                {
                    continue;
                }
                variable.OnReadValueAsync = async (context, _, range, encoding, ct) =>
                {
                    Variant value = Variant.Null;
                    ServiceResult result = await InvokeAsync(
                        context, slot.Binding.File.NodeId, slot.Binding.File, variable, (request, _) =>
                        {
                            if (slot.Owner != SessionId(request.SystemContext))
                            {
                                throw Error(
                                    StatusCodes.BadUserAccessDenied, "The transfer belongs to another session.");
                            }
                            if (slot.Terminal || IsExpired(slot))
                            {
                                throw Error(StatusCodes.BadInvalidArgument, "The transfer is closed or expired.");
                            }
                            if (!range.IsNull)
                            {
                                throw Error(StatusCodes.BadIndexRangeInvalid, "File metadata is scalar.");
                            }
                            if (!encoding.IsNull)
                            {
                                throw Error(StatusCodes.BadDataEncodingUnsupported, "Unsupported file data encoding.");
                            }
                            value = variable.WrappedValue;
                            return default;
                        }, ct, serialize: false).ConfigureAwait(false);
                    return new AttributeReadResult(result, value, result.StatusCode, Now);
                };
            }
        }

        /// <summary>
        /// Uses the commit caller, not the creation context, and publishes no state before authority success.
        /// </summary>
        private async ValueTask CommitTransferAsync(
            ProductionDatasetTransferSlot slot, ByteString content, CancellationToken ct)
        {
            var request = new PlasticsRubberRequestContext(
                slot.CommittingContext ?? throw Error(StatusCodes.BadInvalidState, "There is no commit caller."),
                Node.ProductionDatasetTransfer!.NodeId);
            ProductionDatasetWriteOptionsType options = slot.WriteOptions!;
            await EnsureMutableAsync(request, options.Storage, ct).ConfigureAwait(false);
            ProductionDatasetCommitResult result = await m_provider.CommitAsync(
                request, (ProductionDatasetWriteOptionsType)options.Clone(), content,
                slot.Information == null ? null : Copy(slot.Information), ct).ConfigureAwait(false);
            if (result == null ||
                ((options.Storage & StorageEnumeration.PRODUCTION) != 0) != (result.Active != null) ||
                ((options.Storage & StorageEnumeration.PREPARATION) != 0) != (result.Preparation != null))
            {
                throw Error(StatusCodes.BadUnexpectedError, "The authority omitted a committed destination snapshot.");
            }
            if (result.Active != null)
            {
                ValidateStatus(result.Active);
            }
            if (result.Preparation != null)
            {
                ValidateStatus(result.Preparation);
            }
            if (result.Active != null)
            {
                PublishStatus(StorageEnumeration.PRODUCTION, result.Active);
            }
            if (result.Preparation != null)
            {
                PublishStatus(StorageEnumeration.PREPARATION, result.Preparation);
            }
        }

        /// <summary>
        /// Resolves a live pre-opened handle without leaking information to a foreign session.
        /// </summary>
        private async ValueTask<ProductionDatasetTransferSlot> RequireTransferAsync(ISystemContext context, uint handle)
        {
            NodeId owner = SessionId(context);
            if (!m_transfers.TryGetValue(handle, out ProductionDatasetTransferSlot? slot))
            {
                throw Error(StatusCodes.BadInvalidArgument, "Unknown production dataset transfer handle.");
            }
            if (slot.Owner != owner)
            {
                throw Error(StatusCodes.BadUserAccessDenied, "The transfer belongs to another session.");
            }
            if (slot.Terminal || IsExpired(slot))
            {
                await RetireAsync(slot).ConfigureAwait(false);
                throw Error(StatusCodes.BadInvalidArgument, "The production dataset transfer is closed or expired.");
            }
            slot.LastActivityTimestamp = m_clock.GetTimestamp();
            return slot;
        }

        /// <summary>
        /// Reclaims all expired slots, including handles closed by the shared binder on session shutdown.
        /// </summary>
        private async ValueTask ExpireCoreAsync()
        {
            foreach (ProductionDatasetTransferSlot slot in new List<ProductionDatasetTransferSlot>(m_transfers.Values))
            {
                if (slot.Terminal || IsExpired(slot))
                {
                    await RetireAsync(slot).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Checks the inactivity deadline and the binder's session-owned stream lifetime.
        /// </summary>
        private bool IsExpired(ProductionDatasetTransferSlot slot)
        {
            return slot.Binding.File.OpenCount!.Value == 0 ||
                m_clock.GetElapsedTime(slot.LastActivityTimestamp) >= m_options.TransferTimeout;
        }

        /// <summary>
        /// Removes a transfer idempotently; resource cleanup is never canceled halfway through.
        /// </summary>
        private async ValueTask RetireAsync(ProductionDatasetTransferSlot slot)
        {
            slot.Terminal = true;
            await slot.Binding.DisposeAsync().ConfigureAwait(false);
            Node.ProductionDatasetTransfer!.RemoveChild(slot.Binding.File);
            await m_owner.DeleteNodeAsync(m_owner.SystemContext, slot.Binding.File.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            m_transfers.Remove(slot.Handle);
        }

        /// <summary>
        /// Requires the authenticated session that owns a temporary transfer.
        /// </summary>
        private static NodeId SessionId(ISystemContext context)
        {
            if (context is not ISessionSystemContext { SessionId: { } session } || session.IsNull)
            {
                throw Error(StatusCodes.BadSessionIdInvalid, "Production dataset transfers require a session.");
            }
            return session;
        }

        /// <summary>
        /// The FileType operations supplied by the reusable binder.
        /// </summary>
        private static readonly string[] s_fileMethods =
            ["Open", "Close", "Read", "Write", "GetPosition", "SetPosition"];

        /// <summary>
        /// One bounded, pre-opened file and its session-scoped chapter 20 metadata.
        /// </summary>
        private sealed class ProductionDatasetTransferSlot
        {
            /// <summary>
            /// Captures the file binding and the exact generated options.
            /// </summary>
            internal ProductionDatasetTransferSlot(
                IFileTypeBinding binding,
                uint handle,
                NodeId owner,
                ProductionDatasetWriteOptionsType? writeOptions,
                ProductionDatasetInformationType? information,
                long lastActivityTimestamp)
            {
                Binding = binding;
                Handle = handle;
                Owner = owner;
                WriteOptions = writeOptions;
                Information = information;
                LastActivityTimestamp = lastActivityTimestamp;
            }

            /// <summary>
            /// Gets the standard session-owned stream binding.
            /// </summary>
            internal IFileTypeBinding Binding { get; }

            /// <summary>
            /// Gets the pre-opened handle returned by GenerateFileForRead or GenerateFileForWrite.
            /// </summary>
            internal uint Handle { get; }

            /// <summary>
            /// Gets the owning authenticated session.
            /// </summary>
            internal NodeId Owner { get; }

            /// <summary>
            /// Gets the destination options; absence identifies a read-only transfer.
            /// </summary>
            internal ProductionDatasetWriteOptionsType? WriteOptions { get; }

            /// <summary>
            /// Gets or sets the detached information valid for this transfer's lifetime.
            /// </summary>
            internal ProductionDatasetInformationType? Information { get; set; }

            /// <summary>
            /// Gets or sets the most recent authorized transfer activity.
            /// </summary>
            internal long LastActivityTimestamp
            {
                get => Interlocked.Read(ref m_lastActivityTimestamp);
                set => Interlocked.Exchange(ref m_lastActivityTimestamp, value);
            }

            /// <summary>
            /// Gets or sets the original service context for the current commit only.
            /// </summary>
            internal ISystemContext? CommittingContext { get; set; }

            /// <summary>
            /// Gets or sets whether the transfer can no longer be accessed.
            /// </summary>
            internal bool Terminal
            {
                get => Volatile.Read(ref m_terminal) != 0;
                set => Volatile.Write(ref m_terminal, value ? 1 : 0);
            }

            /// <summary>
            /// The atomically readable inactivity timestamp.
            /// </summary>
            private long m_lastActivityTimestamp;

            /// <summary>
            /// The atomically readable terminal state.
            /// </summary>
            private int m_terminal;
        }
    }
}
