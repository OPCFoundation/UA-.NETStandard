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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Server
{
    public sealed partial class MachineVisionSystemHandle
    {
        private IFileTypeBinder FileBinder =>
            (m_context as VisionBuildContext)?.GetService<IFileTypeBinder>() ?? new FileTypeBinder();

        private ValueTask BindTransfersAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Has(MachineVisionFeatures.FileTransfer))
            {
                if (State.RecipeManagement != null)
                {
                    BindTransfer(State.RecipeManagement, "RecipeTransfer", MachineVisionDocumentKind.Recipe);
                }
                if (State.ConfigurationManagement != null)
                {
                    BindTransfer(State.ConfigurationManagement, "ConfigurationTransfer",
                        MachineVisionDocumentKind.Configuration);
                }
                if (State.ResultManagement != null)
                {
                    BindTransfer(State.ResultManagement, "ResultTransfer", MachineVisionDocumentKind.Result);
                }
            }
            return default;
        }

        private void BindTransfer(NodeState management, string name, MachineVisionDocumentKind kind)
        {
            TemporaryFileTransferState transfer = MachineVisionNodes.Require<TemporaryFileTransferState>(
                m_context.Manager, management, name);
            transfer.ClientProcessingTimeout!.Value = m_options.TransferTimeout.TotalMilliseconds;
            MethodState read = MachineVisionNodes.Require<MethodState>(
                m_context.Manager, transfer, "GenerateFileForRead");
            MethodState write = MachineVisionNodes.Require<MethodState>(
                m_context.Manager, transfer, "GenerateFileForWrite");
            MethodState commit = MachineVisionNodes.Require<MethodState>(m_context.Manager, transfer, "CloseAndCommit");
            ObserveMethodFailures(read, (context, _, _, args, output, ct) =>
                GenerateFileAsync(context, transfer, kind, args, output, false, ct));
            ObserveMethodFailures(write, (context, _, _, args, output, ct) =>
                GenerateFileAsync(context, transfer, kind, args, output, true, ct));
            ObserveMethodFailures(commit, async (context, _, _, args, output, ct) =>
            {
                uint handle = UInt32(args, 0);
                TransferSlot slot = await GetTransferAsync(context, transfer, handle, ct).ConfigureAwait(false);
                if (!slot.Writable)
                {
                    return new ServiceResult(StatusCodes.BadInvalidArgument);
                }
                await slot.Binding.CommitAsync(SessionId(context), handle, ct).ConfigureAwait(false);
                await RetireTransferAsync(slot, ct).ConfigureAwait(false);
                output[0] = Variant.From(NodeId.Null);
                return ServiceResult.Good;
            });
        }

        private async ValueTask<ServiceResult> GenerateFileAsync(
            ISystemContext context,
            TemporaryFileTransferState transfer,
            MachineVisionDocumentKind kind,
            ArrayOf<Variant> args,
            List<Variant> output,
            bool writing,
            CancellationToken ct)
        {
            if (writing && kind == MachineVisionDocumentKind.Result)
            {
                return new ServiceResult(StatusCodes.BadNotWritable);
            }
            await ExpireTransfersAsync(ct).ConfigureAwait(false);
            await EnterTransferAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (m_transfers.Count >= m_options.MaximumTransfers)
                {
                    return new ServiceResult(StatusCodes.BadTooManyOperations);
                }
                MachineVisionDocument document = kind switch
                {
                    MachineVisionDocumentKind.Recipe => await FindRecipeAsync(new RecipeIdExternalDataType(),
                        Structure<RecipeTransferOptions>(args, 0).InternalId, ct).ConfigureAwait(false),
                    MachineVisionDocumentKind.Configuration => await RequiredConfigurationAsync(
                        Structure<ConfigurationTransferOptions>(args, 0).InternalId, ct).ConfigureAwait(false),
                    MachineVisionDocumentKind.Result => await RequiredDocumentAsync(
                        kind, Structure<ResultTransferOptions>(args, 0).Id.Id, ct).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                string id = document.Id;
                if (!writing && document.Content.IsNull)
                {
                    return new ServiceResult(StatusCodes.BadNoDataAvailable);
                }
                FileState file = m_context.Context.CreateInstanceOfFileType(transfer,
                    new QualifiedName("Transfer:" + Guid.NewGuid().ToString("N"), m_context.InstanceNamespaceIndex));
                file.DisplayName = new LocalizedText("Temporary vision file");
                file.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                IFileTypeBinding binding = FileBinder.Bind(
                    file, m_context.Context, document.Content,
                    writing ? (content, token) => CommitDocumentAsync(kind, id, content, file.NodeId, token) : null,
                    ownerSession: SessionId(context),
                    maximumBytes: m_options.MaximumFileBytes);
                bool published = false;
                try
                {
                    uint handle = await binding.OpenAsync(SessionId(context), writing ? (byte)6 : (byte)1, ct)
                        .ConfigureAwait(false);
                    var slot = new TransferSlot(transfer, binding, handle, SessionId(context), writing,
                        m_clock.GetUtcNow());
                    transfer.AddChild(file);
                    await m_context.Manager.AddPredefinedNodeAsync(file, ct).ConfigureAwait(false);
                    m_transfers.Add((transfer.NodeId, handle), slot);
                    BindTransferActivity(slot);
                    if (writing)
                    {
                        Outputs(output, Variant.From(file.NodeId), Variant.From(handle));
                    }
                    else
                    {
                        Outputs(output, Variant.From(file.NodeId), Variant.From(handle), Variant.From(NodeId.Null));
                    }
                    published = true;
                    return ServiceResult.Good;
                }
                finally
                {
                    if (!published)
                    {
                        transfer.RemoveChild(file);
                        await binding.DisposeAsync().ConfigureAwait(false);
                        await m_context.Manager.DeleteNodeAsync(
                            m_context.Manager.SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_transferGate.Release();
            }
        }

        private void BindTransferActivity(TransferSlot slot)
        {
            foreach (string name in s_fileOperations)
            {
                MethodState method = (MethodState)slot.Binding.File.FindChild(
                    m_context.Context, new QualifiedName(name))!;
                var original = method.OnCallMethod2Async!;
                ObserveMethodFailures(method, async (context, called, objectId, args, output, ct) =>
                {
                    await GetTransferAsync(context, slot.Transfer, slot.Handle, ct).ConfigureAwait(false);
                    ServiceResult result = await original(context, called, objectId, args, output, ct)
                        .ConfigureAwait(false);
                    if (name == "Close" && ServiceResult.IsGood(result) && slot.Binding.File.OpenCount!.Value == 0)
                    {
                        await RetireTransferAsync(slot, ct).ConfigureAwait(false);
                    }
                    return result;
                });
            }
        }

        private async ValueTask<TransferSlot> GetTransferAsync(
            ISystemContext context, TemporaryFileTransferState transfer, uint handle, CancellationToken ct)
        {
            await EnterTransferAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_transfers.TryGetValue((transfer.NodeId, handle), out TransferSlot? slot) ||
                    slot.Owner != SessionId(context))
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Unknown or foreign transfer.");
                }
                if (m_clock.GetUtcNow() - slot.LastActivity >= m_options.TransferTimeout)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "The transfer expired.");
                }
                slot.LastActivity = m_clock.GetUtcNow();
                return slot;
            }
            finally
            {
                m_transferGate.Release();
            }
        }

        private async ValueTask ExpireTransfersAsync(CancellationToken ct)
        {
            var expired = new List<TransferSlot>();
            await EnterTransferAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (TransferSlot slot in m_transfers.Values)
                {
                    if (m_clock.GetUtcNow() - slot.LastActivity >= m_options.TransferTimeout)
                    {
                        expired.Add(slot);
                    }
                }
            }
            finally
            {
                m_transferGate.Release();
            }
            foreach (TransferSlot slot in expired)
            {
                await RetireTransferAsync(slot, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask RetireTransferAsync(TransferSlot slot, CancellationToken ct)
        {
            await m_transferGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_transfers.Remove((slot.Transfer.NodeId, slot.Handle)))
                {
                    return;
                }
            }
            finally
            {
                m_transferGate.Release();
            }
            await slot.Binding.DisposeAsync().ConfigureAwait(false);
            slot.Transfer.RemoveChild(slot.Binding.File);
            await m_context.Manager.DeleteNodeAsync(
                m_context.Manager.SystemContext, slot.Binding.File.NodeId, ct).ConfigureAwait(false);
        }

        private async ValueTask BindRecipeFileAsync(
            MachineVisionDocument document, RecipeState recipe, NodeId committingFile, CancellationToken ct)
        {
            if (m_recipeFiles.TryGetValue(document.Id, out IFileTypeBinding? existing))
            {
                if (existing.File.NodeId != committingFile)
                {
                    await existing.UpdateContentAsync(document.Content, ct).ConfigureAwait(false);
                }
                return;
            }
            FileState file = MachineVisionNodes.Require<FileState>(m_context.Manager, recipe, "Handle");
            IFileTypeBinding binding = FileBinder.Bind(
                file, m_context.Context, document.Content,
                (content, token) => CommitDocumentAsync(document.Kind, document.Id, content, file.NodeId, token),
                commitOnClose: true,
                maximumBytes: m_options.MaximumFileBytes);
            foreach (string name in s_fileOperations)
            {
                var method = (MethodState)file.FindChild(m_context.Context, new QualifiedName(name))!;
                ObserveMethodFailures(method, method.OnCallMethod2Async!);
            }
            m_recipeFiles.Add(document.Id, binding);
        }

        private async ValueTask CommitDocumentAsync(
            MachineVisionDocumentKind kind, string id, ByteString content, NodeId file, CancellationToken ct)
        {
            await EnterRuntimeAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                MachineVisionDocument document = await RequiredDocumentAsync(kind, id, ct).ConfigureAwait(false);
                if (kind == MachineVisionDocumentKind.Recipe && RecipeRecord.From(document).Prepared)
                {
                    throw InvalidState("Unprepare a recipe before replacing its content.");
                }
                if (kind == MachineVisionDocumentKind.Configuration &&
                    (await ActiveConfigurationAsync(ct).ConfigureAwait(false))?.Id == id)
                {
                    throw InvalidState("Activate another configuration before replacing its content.");
                }
                BinaryIdBaseDataType external = kind == MachineVisionDocumentKind.Recipe
                    ? RecipeRecord.From(document).ExternalId
                    : Metadata<ConfigurationDataType>(document).ExternalId;
                ValidateContentHash(external, content);
                MachineVisionDocument updated = document with { Content = content };
                MachineVisionOperationResult valid = await m_provider.ValidateDocumentAsync(updated, ct)
                    .ConfigureAwait(false);
                if (!valid.Succeeded)
                {
                    if (ServiceResult.IsBad(valid.ServiceResult))
                    {
                        throw new ServiceResultException(valid.ServiceResult);
                    }
                    throw InvalidState("The document provider refused the content with domain error " + valid.Error);
                }
                ByteString hash = ByteString.From(ComputeContentHash("SHA256", content));
                if (kind == MachineVisionDocumentKind.Recipe)
                {
                    RecipeRecord recipe = RecipeRecord.From(document);
                    recipe.InternalId.Hash = hash;
                    recipe.InternalId.HashAlgorithm = "SHA256";
                    recipe.InternalId.EncodingMask |= (uint)(BinaryIdBaseDataTypeFields.Hash |
                        BinaryIdBaseDataTypeFields.HashAlgorithm);
                    updated = updated with { Metadata = (recipe with { LastModified = Now }).ToVariant() };
                }
                else
                {
                    ConfigurationDataType configuration = Metadata<ConfigurationDataType>(document);
                    configuration.HasTransferableDataOnFile = true;
                    configuration.LastModified = Now;
                    configuration.InternalId.Hash = hash;
                    configuration.InternalId.HashAlgorithm = "SHA256";
                    configuration.InternalId.EncodingMask |= (uint)(BinaryIdBaseDataTypeFields.Hash |
                        BinaryIdBaseDataTypeFields.HashAlgorithm);
                    configuration.EncodingMask |= (uint)ConfigurationDataTypeFields.HasTransferableDataOnFile;
                    updated = updated with { Metadata = Variant.FromStructure(configuration) };
                }
                await m_store.WriteAsync(updated, ct).ConfigureAwait(false);
                await RefreshFoldersAsync(ct, file).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask DisposeTransfersAsync()
        {
            TransferSlot[] transfers;
            await m_transferGate.WaitAsync().ConfigureAwait(false);
            try
            {
                transfers = [.. m_transfers.Values];
            }
            finally
            {
                m_transferGate.Release();
            }
            foreach (TransferSlot slot in transfers)
            {
                await RetireTransferAsync(slot, CancellationToken.None).ConfigureAwait(false);
            }
            foreach (IFileTypeBinding file in m_recipeFiles.Values)
            {
                await file.DisposeAsync().ConfigureAwait(false);
            }
            m_recipeFiles.Clear();
        }

        private static NodeId SessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        private static void ValidateContentHash(BinaryIdBaseDataType identifier, ByteString content)
        {
            if (identifier.Hash.IsNull || identifier.Hash.Span.IsEmpty)
            {
                return;
            }
            string algorithm = NormalizeHashAlgorithm(identifier.HashAlgorithm);
            byte[] hash = ComputeContentHash(algorithm, content);
            if (!identifier.Hash.Span.SequenceEqual(hash))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "The uploaded content hash does not match.");
            }
        }

        private static byte[] ComputeContentHash(string algorithm, ByteString content)
        {
#if NET5_0_OR_GREATER
            return algorithm switch
            {
                "SHA256" => SHA256.HashData(content.Span),
                "SHA384" => SHA384.HashData(content.Span),
                "SHA512" => SHA512.HashData(content.Span),
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported, "Unsupported content hash algorithm.")
            };
#else
            using HashAlgorithm hash = algorithm switch
            {
                "SHA256" => SHA256.Create(),
                "SHA384" => SHA384.Create(),
                "SHA512" => SHA512.Create(),
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported, "Unsupported content hash algorithm.")
            };
            return hash.ComputeHash(content.Span.ToArray());
#endif
        }

        private static string NormalizeHashAlgorithm(string? value)
        {
            string algorithm = (value ?? string.Empty).Trim().Replace("-", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();
            return algorithm switch
            {
                "" or "HTTP://WWW.W3.ORG/2001/04/XMLENC#SHA256" => "SHA256",
                "HTTP://WWW.W3.ORG/2001/04/XMLENC#SHA512" => "SHA512",
                _ => algorithm
            };
        }

        private async ValueTask EnterTransferAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_disposeStarted) != 0)
            {
                throw InvalidState("The vision system is being disposed.");
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            await m_transferGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        }

        private readonly SemaphoreSlim m_transferGate = new(1, 1);
        private readonly Dictionary<(NodeId Transfer, uint Handle), TransferSlot> m_transfers = [];
        private readonly Dictionary<string, IFileTypeBinding> m_recipeFiles = new(StringComparer.Ordinal);

        private static readonly string[] s_fileOperations =
            ["Open", "Read", "Write", "Close", "GetPosition", "SetPosition"];

        private sealed class TransferSlot
        {
            public TransferSlot(
                TemporaryFileTransferState transfer, IFileTypeBinding binding, uint handle, NodeId owner,
                bool writable, DateTimeOffset lastActivity)
            {
                Transfer = transfer;
                Binding = binding;
                Handle = handle;
                Owner = owner;
                Writable = writable;
                LastActivity = lastActivity;
            }

            public TemporaryFileTransferState Transfer { get; }

            public IFileTypeBinding Binding { get; }

            public uint Handle { get; }

            public NodeId Owner { get; }

            public bool Writable { get; }

            public DateTimeOffset LastActivity { get; set; }
        }
    }
}
