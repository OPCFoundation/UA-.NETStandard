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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.StateMachines;

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Typed, bounded OPC 40083 chapter 20 operations over a caller-owned session.
    /// </summary>
    public sealed partial class ProductionDatasetClient
    {
        /// <summary>
        /// Creates a client for one management instance and registers its generated wire types.
        /// </summary>
        public ProductionDatasetClient(
            ISession session, NodeId managementId, ProductionDatasetClientOptions? options = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (managementId.IsNull)
            {
                throw new ArgumentException("A management instance NodeId is required.", nameof(managementId));
            }
            m_options = options ?? new ProductionDatasetClientOptions();
            m_options.Validate();
            IEncodeableFactoryBuilder builder = session.Factory.Builder;
            builder.AddOpcUaPlasticsRubber();
            builder.Commit();
            Proxy = new ProductionDatasetManagementTypeClient(session, managementId, session.MessageContext.Telemetry);
        }

        /// <summary>
        /// Gets the original session; this client never changes its user identity or owns its lifetime.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the generated management proxy for direct typed method access.
        /// </summary>
        public ProductionDatasetManagementTypeClient Proxy { get; }

        /// <summary>
        /// Gets the generated proxy for exactly one active or preparation control layer.
        /// </summary>
        public async ValueTask<ProductionDatasetStatusTypeClient> GetStatusProxyAsync(
            StorageEnumeration storage, CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            ProductionDatasetStatusTypeClient? proxy = storage switch
            {
                StorageEnumeration.PRODUCTION => await Proxy.GetActiveProductionDatasetStatusAsync(
                    Session.MessageContext.Telemetry, deadline.Token).ConfigureAwait(false),
                StorageEnumeration.PREPARATION => await Proxy.GetProductionDatasetInPreparationStatusAsync(
                    Session.MessageContext.Telemetry, deadline.Token).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(storage))
            };
            return proxy ?? throw Unsupported("The requested control layer is not present.");
        }

        /// <summary>
        /// Activates a stored file, with an empty component list selecting the complete dataset.
        /// </summary>
        public async ValueTask LoadAsync(
            StorageEnumeration storage,
            string name,
            ArrayOf<ushort> components,
            CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            ProductionDatasetStatusTypeClient status = await GetStatusProxyAsync(storage, deadline.Token)
                .ConfigureAwait(false);
            await status.LoadAsync(name, components, deadline.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists a control-layer dataset under the supplied file-system name.
        /// </summary>
        public async ValueTask SaveAsync(
            StorageEnumeration storage, string name, CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            ProductionDatasetStatusTypeClient status = await GetStatusProxyAsync(storage, deadline.Token)
                .ConfigureAwait(false);
            await status.SaveAsync(name, deadline.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current mandatory Information property of one control layer.
        /// </summary>
        public async ValueTask<ProductionDatasetInformationType> ReadInformationAsync(
            StorageEnumeration storage, CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            Variant value = await ReadStatusPropertyAsync(storage, BrowseNames.Information, deadline.Token)
                .ConfigureAwait(false);
            return value.TryGetStructure<ProductionDatasetInformationType>(
                Session.MessageContext, out ProductionDatasetInformationType? information)
                ? information : throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// Reads the optional Modified property; absence is reported as unsupported, not false.
        /// </summary>
        public ValueTask<bool> ReadModifiedAsync(
            StorageEnumeration storage, CancellationToken cancellationToken = default)
        {
            return ReadBooleanAsync(storage, BrowseNames.Modified, cancellationToken);
        }

        /// <summary>
        /// Reads the optional Frozen property; absence is reported as unsupported, not false.
        /// </summary>
        public ValueTask<bool> ReadFrozenAsync(
            StorageEnumeration storage, CancellationToken cancellationToken = default)
        {
            return ReadBooleanAsync(storage, BrowseNames.Frozen, cancellationToken);
        }

        /// <summary>
        /// Changes the device's frozen policy and validates the per-node write result.
        /// </summary>
        public async ValueTask SetFrozenAsync(
            StorageEnumeration storage, bool frozen, CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            ProductionDatasetStatusTypeClient status = await GetStatusProxyAsync(storage, deadline.Token)
                .ConfigureAwait(false);
            NodeId property = await ResolvePropertyAsync(status.ObjectId, BrowseNames.Frozen, deadline.Token)
                .ConfigureAwait(false);
            ArrayOf<WriteValue> writes =
            [
                new WriteValue
                {
                    NodeId = property,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(frozen))
                }
            ];
            WriteResponse response = await Session.WriteAsync(null, writes, deadline.Token).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, writes);
            if (StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(response.Results[0]);
            }
        }

        /// <summary>
        /// Reads available file-system dataset descriptions using standard wildcard and mould filters.
        /// </summary>
        public async ValueTask<ArrayOf<ProductionDatasetInformationType>> GetListAsync(
            string nameFilter = "", string mouldId = "", CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            ProductionDatasetListsTypeClient lists = await GetListsProxyAsync(deadline.Token).ConfigureAwait(false);
            ArrayOf<ProductionDatasetInformationType> information =
                await lists.GetProductionDatasetListAsync(nameFilter, mouldId, deadline.Token).ConfigureAwait(false);
            CheckListSize(information);
            return information;
        }

        /// <summary>
        /// Sends the client's available-dataset list, including an empty response to a list-request event.
        /// </summary>
        public async ValueTask SendListAsync(
            ArrayOf<ProductionDatasetInformationType> information, CancellationToken cancellationToken = default)
        {
            CheckListSize(information);
            using var deadline = Deadline(cancellationToken);
            ProductionDatasetListsTypeClient lists = await GetListsProxyAsync(deadline.Token).ConfigureAwait(false);
            await lists.SendProductionDatasetListAsync(information, deadline.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets information only while a GenerateFileForRead handle remains open.
        /// </summary>
        public async ValueTask<ProductionDatasetInformationType> GetInformationAsync(
            uint handle, CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            return await Proxy.GetProductionDatasetInformationAsync(handle, deadline.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Associates information with an open GenerateFileForWrite handle, without committing it.
        /// </summary>
        public async ValueTask SendInformationAsync(
            uint handle, ProductionDatasetInformationType information, CancellationToken cancellationToken = default)
        {
            if (information == null)
            {
                throw new ArgumentNullException(nameof(information));
            }
            using var deadline = Deadline(cancellationToken);
            await Proxy.SendProductionDatasetInformationAsync(handle, information, deadline.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the generated transfer child and wraps the existing standard transfer client.
        /// </summary>
        public async ValueTask<TemporaryFileTransferClient> GetTransferClientAsync(
            CancellationToken cancellationToken = default)
        {
            using var deadline = Deadline(cancellationToken);
            TemporaryFileTransferTypeClient? transfer = await Proxy.GetProductionDatasetTransferAsync(
                Session.MessageContext.Telemetry, deadline.Token).ConfigureAwait(false);
            if (transfer == null)
            {
                throw Unsupported("The mandatory dataset transfer object is absent.");
            }
            return new TemporaryFileTransferClient(Session, transfer.ObjectId, new FileSystemClientOptions
            {
                ChunkSize = m_options.ChunkSize,
                MaxBufferedReadSize = m_options.MaximumFileBytes
            });
        }

        /// <summary>
        /// Opens a standard read stream; the caller must dispose it asynchronously to close the transfer.
        /// </summary>
        public async ValueTask<UaFileStream> OpenReadAsync(
            ProductionDatasetReadOptionsType options, CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            using var deadline = Deadline(cancellationToken);
            TemporaryFileTransferClient transfer = await GetTransferClientAsync(deadline.Token).ConfigureAwait(false);
            return await transfer.GenerateFileForReadAsync(Variant.FromStructure(options), deadline.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a standard write transaction; uncommitted asynchronous disposal aborts it.
        /// </summary>
        public async ValueTask<UaTemporaryWriteFile> OpenWriteAsync(
            ProductionDatasetWriteOptionsType options, CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            using var deadline = Deadline(cancellationToken);
            TemporaryFileTransferClient transfer = await GetTransferClientAsync(deadline.Token).ConfigureAwait(false);
            return await transfer.GenerateFileForWriteAsync(Variant.FromStructure(options), deadline.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads bounded content and its matching handle-scoped information, then closes the file.
        /// </summary>
        /// <remarks>
        /// This convenience method requires GetProductionDatasetInformation. For a server without
        /// that optional method, use DownloadContentAsync or OpenReadAsync instead.
        /// </remarks>
        public async ValueTask<ProductionDatasetInformationType> DownloadAsync(
            ProductionDatasetReadOptionsType options,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            CheckStream(destination, writing: true);
            using var deadline = Deadline(cancellationToken);
            UaFileStream file = await OpenReadAsync(options, deadline.Token).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                ProductionDatasetInformationType information = await GetInformationAsync(file.Handle, deadline.Token)
                    .ConfigureAwait(false);
                await CopyBoundedAsync(file, destination, deadline.Token).ConfigureAwait(false);
                return information;
            }
        }

        /// <summary>
        /// Downloads bounded content without requiring the optional information-exchange methods.
        /// </summary>
        public async ValueTask DownloadContentAsync(
            ProductionDatasetReadOptionsType options,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            CheckStream(destination, writing: true);
            using var deadline = Deadline(cancellationToken);
            UaFileStream file = await OpenReadAsync(options, deadline.Token).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                await CopyBoundedAsync(file, destination, deadline.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uploads bounded content and optional metadata, then awaits the server's complete commit.
        /// </summary>
        public async ValueTask UploadAsync(
            ProductionDatasetWriteOptionsType options,
            Stream source,
            ProductionDatasetInformationType? information = null,
            CancellationToken cancellationToken = default)
        {
            CheckStream(source, writing: false);
            using var deadline = Deadline(cancellationToken);
            UaTemporaryWriteFile file = await OpenWriteAsync(options, deadline.Token).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                if (information != null)
                {
                    await SendInformationAsync(file.Handle, information, deadline.Token).ConfigureAwait(false);
                }
                await CopyBoundedAsync(source, file.Stream, deadline.Token).ConfigureAwait(false);
                NodeId completion = await file.CommitAsync(deadline.Token).ConfigureAwait(false);
                await WaitForCommitAsync(completion, deadline.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the current provider-backed value of a generated status child.
        /// </summary>
        private async ValueTask<Variant> ReadStatusPropertyAsync(
            StorageEnumeration storage, string name, CancellationToken ct)
        {
            ProductionDatasetStatusTypeClient status = await GetStatusProxyAsync(storage, ct).ConfigureAwait(false);
            NodeId property = await ResolvePropertyAsync(status.ObjectId, name, ct).ConfigureAwait(false);
            DataValue value = await Session.ReadValueAsync(property, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            return value.WrappedValue;
        }

        /// <summary>
        /// Reads a typed optional Boolean without converting a bad value into false.
        /// </summary>
        private async ValueTask<bool> ReadBooleanAsync(StorageEnumeration storage, string name, CancellationToken ct)
        {
            using var deadline = Deadline(ct);
            Variant value = await ReadStatusPropertyAsync(storage, name, deadline.Token).ConfigureAwait(false);
            return value.TryGetValue(out bool result) ? result
                : throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// Resolves the generated optional list object, preserving absence as unsupported.
        /// </summary>
        private async ValueTask<ProductionDatasetListsTypeClient> GetListsProxyAsync(CancellationToken ct)
        {
            return await Proxy.GetProductionDatasetListsAsync(Session.MessageContext.Telemetry, ct)
                .ConfigureAwait(false)
                ?? throw Unsupported("Dataset lists are not supported by this instance.");
        }

        /// <summary>
        /// Resolves a generated property using its namespace URI instead of a fixed namespace index.
        /// </summary>
        private async ValueTask<NodeId> ResolvePropertyAsync(NodeId parent, string name, CancellationToken ct)
        {
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                                IncludeSubtypes = true,
                                TargetName = QualifiedName.Create(
                                    name, Namespaces.PlasticsRubber, Session.NamespaceUris)
                            }
                        ]
                    }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, paths);
            BrowsePathResult result = response.Results[0];
            if (result.StatusCode == StatusCodes.BadNoMatch)
            {
                throw Unsupported("The optional dataset property is absent: " + name);
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue ||
                result.Targets[0].TargetId.ServerIndex != 0)
            {
                throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
            }
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
        }

        /// <summary>
        /// Copies at most the configured bytes, including non-seekable sources, using asynchronous I/O only.
        /// </summary>
        private async ValueTask CopyBoundedAsync(Stream source, Stream destination, CancellationToken ct)
        {
            byte[] buffer = new byte[m_options.ChunkSize];
            long total = 0;
            while (true)
            {
#if NETSTANDARD2_0 || NETFRAMEWORK
                int read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#else
                int read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    return;
                }
                total += read;
                if (total > m_options.MaximumFileBytes)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
#if NETSTANDARD2_0 || NETFRAMEWORK
                await destination.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
#else
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
#endif
            }
        }

        /// <summary>
        /// Does not report an asynchronous commit as complete until its standard state machine reaches Idle.
        /// </summary>
        private async ValueTask WaitForCommitAsync(NodeId completion, CancellationToken ct)
        {
            if (completion.IsNull)
            {
                return;
            }
            var state = new FileTransferStateMachineTypeClient(Session, completion, Session.MessageContext.Telemetry);
            while (true)
            {
                FiniteStateSnapshot snapshot = await state.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
                if (StatusCode.IsBad(snapshot.Status))
                {
                    throw new ServiceResultException(snapshot.Status);
                }
                if (snapshot.CurrentStateId == Opc.Ua.ObjectIds.FileTransferStateMachineType_Idle)
                {
                    return;
                }
                if (snapshot.CurrentStateId == Opc.Ua.ObjectIds.FileTransferStateMachineType_Error)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks a caller-owned stream before opening a remote handle.
        /// </summary>
        private static void CheckStream(Stream stream, bool writing)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (writing ? !stream.CanWrite : !stream.CanRead)
            {
                throw new ArgumentException("The stream does not support the required access.", nameof(stream));
            }
        }

        /// <summary>
        /// Enforces the client's independent list bound.
        /// </summary>
        private void CheckListSize(ArrayOf<ProductionDatasetInformationType> information)
        {
            if (information.Count > m_options.MaximumDatasetCount)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        /// <summary>
        /// Combines the caller's cancellation with a bounded overall operation deadline.
        /// </summary>
        private CancellationTokenSource Deadline(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            source.CancelAfter(m_options.OperationTimeout);
            return source;
        }

        /// <summary>
        /// Reports an optional capability as absent without manufacturing a value.
        /// </summary>
        private static ServiceResultException Unsupported(string message)
        {
            return ServiceResultException.Create(StatusCodes.BadNotSupported, message);
        }

        /// <summary>
        /// The immutable client transfer bounds.
        /// </summary>
        private readonly ProductionDatasetClientOptions m_options;
    }
}
