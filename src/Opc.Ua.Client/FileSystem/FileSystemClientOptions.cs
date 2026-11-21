// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Configuration knobs for <c>FileSystemClient</c>,
    /// <c>UaFileStream</c> and <c>TemporaryFileTransferClient</c>.
    /// </summary>
    public sealed class FileSystemClientOptions
    {
        /// <summary>
        /// Default chunk size (in bytes) used by
        /// <c>UaFileStream.ReadAsync</c> /
        /// <c>UaFileStream.WriteAsync</c> when streaming to or from the
        /// server. Honours the per-file
        /// <c>MaxByteStringLength</c> property when known (clamped down
        /// at open time). Default: 64 KiB.
        /// </summary>
        public int ChunkSize { get; set; } = 64 * 1024;

        /// <summary>
        /// Cap on the number of bytes a single
        /// <c>ReadAllBytesAsync</c> / <c>ReadAllTextAsync</c> call may
        /// load into memory. Reads that would exceed this size throw a
        /// <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/>. Default:
        /// 16 MiB.
        /// </summary>
        public long MaxBufferedReadSize { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Maximum number of resolved
        /// <c>(parent NodeId, browse name) → child NodeId</c> entries
        /// cached on the client. Set to zero to disable caching.
        /// Default: <c>1024</c>.
        /// </summary>
        public int PathCacheSize { get; set; } = 1024;

        /// <summary>
        /// When listing directory children, controls whether subtypes of
        /// <c>FileType</c> (e.g. <c>TrustListType</c>,
        /// <c>AddressSpaceFileType</c>) count as files. Default
        /// <c>true</c>.
        /// </summary>
        public bool IncludeFileTypeSubtypes { get; set; } = true;

        /// <summary>
        /// When listing directory children, controls whether subtypes of
        /// <c>FileDirectoryType</c> count as directories. Default
        /// <c>true</c>.
        /// </summary>
        public bool IncludeFileDirectoryTypeSubtypes { get; set; } = true;

        /// <summary>
        /// Returns a deep copy of these options.
        /// </summary>
        /// <returns>A new <see cref="FileSystemClientOptions"/> with the
        /// same values.</returns>
        public FileSystemClientOptions Clone()
        {
            return new FileSystemClientOptions
            {
                ChunkSize = ChunkSize,
                MaxBufferedReadSize = MaxBufferedReadSize,
                PathCacheSize = PathCacheSize,
                IncludeFileTypeSubtypes = IncludeFileTypeSubtypes,
                IncludeFileDirectoryTypeSubtypes = IncludeFileDirectoryTypeSubtypes
            };
        }

        internal void Validate()
        {
            if (ChunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ChunkSize),
                    ChunkSize,
                    "ChunkSize must be positive.");
            }
            if (MaxBufferedReadSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxBufferedReadSize),
                    MaxBufferedReadSize,
                    "MaxBufferedReadSize must be positive.");
            }
            if (PathCacheSize < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PathCacheSize),
                    PathCacheSize,
                    "PathCacheSize must be non-negative.");
            }
        }
    }
}
