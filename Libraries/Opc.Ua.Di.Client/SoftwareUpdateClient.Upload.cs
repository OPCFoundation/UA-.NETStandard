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

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Client-side helpers that drive the OPC 10000-5 §11.4
    /// <c>TemporaryFileTransferType</c> upload pipeline exposed by the
    /// DI software-update facet's <c>PackageLoadingType.FileTransfer</c>
    /// slot. The server-side wiring is delivered by
    /// <c>SoftwareUpdateFileTransferManager</c>.
    /// </summary>
    public partial class SoftwareUpdateClient
    {
        /// <summary>
        /// Default chunk size when the server does not advertise one.
        /// </summary>
        public const int DefaultUploadChunkSizeBytes = 8 * 1024;

        /// <summary>OPC 10000-5 §11.3.3 — Open mode <c>Write|EraseExisting</c>.</summary>
        private const byte OpenModeWriteEraseExisting = 6;

        private NodeId? m_cachedFileTransferNodeId;
        private NodeId? m_cachedGenerateFileForWriteNodeId;
        private NodeId? m_cachedCloseAndCommitNodeId;

        /// <summary>
        /// Uploads <paramref name="payload"/> through the SU FileTransfer
        /// pipeline. The bytes are streamed in
        /// <paramref name="chunkSizeBytes"/>-sized chunks via the standard
        /// <c>FileType.Write</c> method; on completion
        /// <c>CloseAndCommit</c> is called, which commits the payload to
        /// the server-side <c>ISoftwarePackageStore</c>.
        /// </summary>
        /// <param name="payload">
        /// Source stream. Must be readable; position is not reset by this
        /// method.
        /// </param>
        /// <param name="suggestedPackageId">
        /// Optional package id to advertise to the server via the
        /// vendor-defined <c>generateOptions</c> argument. When non-empty
        /// the default <c>SoftwareUpdateFileTransferManager</c> uses it
        /// as the resulting package's id.
        /// </param>
        /// <param name="chunkSizeBytes">
        /// Chunk size in bytes. Defaults to
        /// <see cref="DefaultUploadChunkSizeBytes"/> when
        /// <see langword="null"/>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The number of payload bytes uploaded.
        /// </returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async ValueTask<long> UploadPackageAsync(
            Stream payload,
            string? suggestedPackageId = null,
            int? chunkSizeBytes = null,
            CancellationToken ct = default)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (!payload.CanRead)
            {
                throw new ArgumentException("Payload stream must be readable.", nameof(payload));
            }
            int chunk = chunkSizeBytes ?? DefaultUploadChunkSizeBytes;
            if (chunk <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSizeBytes), "Chunk size must be positive.");
            }

            (NodeId fileTransferId, NodeId generateForWriteId, NodeId closeAndCommitId) =
                await ResolveFileTransferMethodsAsync(ct).ConfigureAwait(false);

            // 1) GenerateFileForWrite(generateOptions) → (fileNodeId, fileHandle)
            Variant generateOptions = string.IsNullOrEmpty(suggestedPackageId)
                ? Variant.Null
                : new Variant(suggestedPackageId);
            (NodeId fileNodeId, uint commitHandle) = await CallGenerateFileForWriteAsync(
                fileTransferId, generateForWriteId, generateOptions, ct)
                .ConfigureAwait(false);

            // 2) Resolve Open / Write / Close on the transient FileState.
            (NodeId openId, NodeId writeId, NodeId closeId) =
                await ResolveFileMethodsAsync(fileNodeId, ct).ConfigureAwait(false);

            // 3) Open(mode=6) → openHandle
            uint openHandle = await CallOpenAsync(
                fileNodeId, openId, OpenModeWriteEraseExisting, ct)
                .ConfigureAwait(false);

            // 4) Chunked Write
            long totalWritten = 0;
            byte[] buffer = new byte[chunk];
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                    int read = await payload.ReadAsync(
                        buffer.AsMemory(0, chunk), ct).ConfigureAwait(false);
#else
                    int read = await payload.ReadAsync(
                        buffer, 0, chunk, ct).ConfigureAwait(false);
#endif
                    if (read <= 0)
                    {
                        break;
                    }
                    byte[] toSend = read == chunk
                        ? buffer
                        : buffer.AsSpan(0, read).ToArray();
                    await CallWriteAsync(
                        fileNodeId, writeId, openHandle, toSend, ct)
                        .ConfigureAwait(false);
                    totalWritten += read;
                }

                // 5) Close(openHandle)
                await CallCloseAsync(
                    fileNodeId, closeId, openHandle, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort close on failure; ignore secondary errors.
                try
                {
                    await CallCloseAsync(
                        fileNodeId, closeId, openHandle, ct).ConfigureAwait(false);
                }
                catch
                {
                    // swallowed
                }
                throw;
            }

            // 6) CloseAndCommit(commitHandle)
            _ = await CallCloseAndCommitAsync(
                fileTransferId, closeAndCommitId, commitHandle, ct)
                .ConfigureAwait(false);

            return totalWritten;
        }

        /// <summary>
        /// Convenience overload that uploads a byte buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ValueTask<long> UploadPackageAsync(
            byte[] payload,
            string? suggestedPackageId = null,
            int? chunkSizeBytes = null,
            CancellationToken ct = default)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            return UploadPackageAsync(
                new MemoryStream(payload, writable: false),
                suggestedPackageId,
                chunkSizeBytes,
                ct);
        }

        private async ValueTask<(NodeId FileTransfer, NodeId GenerateForWrite, NodeId CloseAndCommit)>
            ResolveFileTransferMethodsAsync(CancellationToken ct)
        {
            if (m_cachedFileTransferNodeId is { } cachedFt &&
                m_cachedGenerateFileForWriteNodeId is { } cachedGen &&
                m_cachedCloseAndCommitNodeId is { } cachedCmt)
            {
                return (cachedFt, cachedGen, cachedCmt);
            }

            ushort diNs = Session.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Di.Namespaces.OpcUaDi);

            var paths = ArrayOf.Wrapped(new[]
            {
                BuildBrowsePath(
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("Loading", diNs)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("FileTransfer", diNs)
                    }),
                BuildBrowsePath(
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("Loading", diNs)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("FileTransfer", diNs)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("GenerateFileForWrite")
                    }),
                BuildBrowsePath(
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("Loading", diNs)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("FileTransfer", diNs)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("CloseAndCommit")
                    })
            });

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct)
                .ConfigureAwait(false);

            NodeId ftId = ExpectSingleTarget(response.Results, 0, "Loading.FileTransfer");
            NodeId genId = ExpectSingleTarget(response.Results, 1,
                "Loading.FileTransfer.GenerateFileForWrite");
            NodeId cmtId = ExpectSingleTarget(response.Results, 2,
                "Loading.FileTransfer.CloseAndCommit");

            m_cachedFileTransferNodeId = ftId;
            m_cachedGenerateFileForWriteNodeId = genId;
            m_cachedCloseAndCommitNodeId = cmtId;

            return (ftId, genId, cmtId);
        }

        private async ValueTask<(NodeId OpenId, NodeId WriteId, NodeId CloseId)>
            ResolveFileMethodsAsync(NodeId fileNodeId, CancellationToken ct)
        {
            var paths = ArrayOf.Wrapped(new[]
            {
                MakeBrowsePathFromFile(fileNodeId, "Open"),
                MakeBrowsePathFromFile(fileNodeId, "Write"),
                MakeBrowsePathFromFile(fileNodeId, "Close")
            });

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct)
                .ConfigureAwait(false);

            return (
                ExpectSingleTarget(response.Results, 0, "FileState.Open"),
                ExpectSingleTarget(response.Results, 1, "FileState.Write"),
                ExpectSingleTarget(response.Results, 2, "FileState.Close")
            );
        }

        private async ValueTask<(NodeId FileNodeId, uint Handle)>
            CallGenerateFileForWriteAsync(
                NodeId fileTransferId,
                NodeId methodId,
                Variant generateOptions,
                CancellationToken ct)
        {
            CallResponse response = await Session.CallAsync(
                null,
                ArrayOf.Wrapped(new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = fileTransferId,
                        MethodId = methodId,
                        InputArguments = ArrayOf.Wrapped(new[] { generateOptions })
                    }
                }),
                ct).ConfigureAwait(false);

            CallMethodResult result = ExpectSingleCallResult(response, "GenerateFileForWrite");
            if (result.OutputArguments.Count < 2)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "GenerateFileForWrite did not return (fileNodeId, fileHandle).");
            }
            NodeId fileNodeId = result.OutputArguments[0].TryGetValue(out NodeId nid)
                ? nid
                : NodeId.Null;
            uint handle = result.OutputArguments[1].TryGetValue(out uint h)
                ? h : 0u;
            if (fileNodeId.IsNull || handle == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "GenerateFileForWrite returned an invalid file NodeId or handle.");
            }
            return (fileNodeId, handle);
        }

        private async ValueTask<uint> CallOpenAsync(
            NodeId fileNodeId, NodeId openMethodId, byte mode, CancellationToken ct)
        {
            CallResponse response = await Session.CallAsync(
                null,
                ArrayOf.Wrapped(new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = fileNodeId,
                        MethodId = openMethodId,
                        InputArguments = ArrayOf.Wrapped(new[] { new Variant(mode) })
                    }
                }),
                ct).ConfigureAwait(false);

            CallMethodResult result = ExpectSingleCallResult(response, "FileState.Open");
            if (result.OutputArguments.Count < 1 ||
                !result.OutputArguments[0].TryGetValue(out uint handle))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "FileState.Open did not return a uint file handle.");
            }
            return handle;
        }

        private async ValueTask CallWriteAsync(
            NodeId fileNodeId, NodeId writeMethodId,
            uint fileHandle, byte[] data, CancellationToken ct)
        {
            CallResponse response = await Session.CallAsync(
                null,
                ArrayOf.Wrapped(new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = fileNodeId,
                        MethodId = writeMethodId,
                        InputArguments = ArrayOf.Wrapped(new[]
                        {
                            new Variant(fileHandle),
                            new Variant(ByteString.From(data))
                        })
                    }
                }),
                ct).ConfigureAwait(false);

            _ = ExpectSingleCallResult(response, "FileState.Write");
        }

        private async ValueTask CallCloseAsync(
            NodeId fileNodeId, NodeId closeMethodId,
            uint fileHandle, CancellationToken ct)
        {
            CallResponse response = await Session.CallAsync(
                null,
                ArrayOf.Wrapped(new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = fileNodeId,
                        MethodId = closeMethodId,
                        InputArguments = ArrayOf.Wrapped(new[] { new Variant(fileHandle) })
                    }
                }),
                ct).ConfigureAwait(false);

            _ = ExpectSingleCallResult(response, "FileState.Close");
        }

        private async ValueTask<NodeId> CallCloseAndCommitAsync(
            NodeId fileTransferId, NodeId methodId,
            uint commitHandle, CancellationToken ct)
        {
            CallResponse response = await Session.CallAsync(
                null,
                ArrayOf.Wrapped(new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = fileTransferId,
                        MethodId = methodId,
                        InputArguments = ArrayOf.Wrapped(new[] { new Variant(commitHandle) })
                    }
                }),
                ct).ConfigureAwait(false);

            CallMethodResult result = ExpectSingleCallResult(response, "CloseAndCommit");
            if (result.OutputArguments.Count < 1 ||
                !result.OutputArguments[0].TryGetValue(out NodeId completion))
            {
                return NodeId.Null;
            }
            return completion;
        }

        private BrowsePath BuildBrowsePath(params RelativePathElement[] elements)
        {
            return new BrowsePath
            {
                StartingNode = SoftwareUpdateNodeId,
                RelativePath = new RelativePath
                {
                    Elements = ArrayOf.Wrapped(elements)
                }
            };
        }

        private static BrowsePath MakeBrowsePathFromFile(
            NodeId fileNodeId, string browseName)
        {
            return new BrowsePath
            {
                StartingNode = fileNodeId,
                RelativePath = new RelativePath
                {
                    Elements = ArrayOf.Wrapped(new[]
                    {
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName)
                        }
                    })
                }
            };
        }

        private NodeId ExpectSingleTarget(
            ArrayOf<BrowsePathResult> results, int index, string description)
        {
            if (results.Count <= index)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    $"TranslateBrowsePaths returned fewer results than expected resolving {description}.");
            }
            BrowsePathResult result = results[index];
            if (!StatusCode.IsGood(result.StatusCode) || result.Targets.Count == 0)
            {
                throw new ServiceResultException(
                    (uint)result.StatusCode,
                    $"Could not resolve {description}.");
            }
            return ExpandedNodeId.ToNodeId(
                result.Targets[0].TargetId,
                Session.NamespaceUris);
        }

        private static CallMethodResult ExpectSingleCallResult(
            CallResponse response, string description)
        {
            if (response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    $"Call({description}) returned no results.");
            }
            CallMethodResult result = response.Results[0];
            if (!StatusCode.IsGood(result.StatusCode))
            {
                throw new ServiceResultException(
                    (uint)result.StatusCode,
                    $"Call({description}) failed.");
            }
            return result;
        }
    }
}
