// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Writes a stream of <see cref="ChannelKeyMaterial"/> snapshots to a
    /// keylog file. Implementations are append-only and must be safe to
    /// call from multiple threads.
    /// </summary>
    public interface IKeyLogWriter : IAsyncDisposable
    {
        /// <summary>
        /// Path of the file being written.
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// Appends a single key-material record.
        /// </summary>
        ValueTask AppendAsync(ChannelKeyMaterial material, CancellationToken ct);

        /// <summary>
        /// Flushes pending writes to disk.
        /// </summary>
        ValueTask FlushAsync(CancellationToken ct);
    }

    /// <summary>
    /// Reads a sequence of <see cref="ChannelKeyMaterial"/> records from
    /// a keylog file.
    /// </summary>
    public interface IKeyLogReader
    {
        /// <summary>
        /// Reads every record from the given path. Implementations may
        /// stream lazily for large files.
        /// </summary>
        IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(
            string filePath,
            CancellationToken ct);

        /// <summary>
        /// Reads every record from the given stream. The stream is read
        /// to end; the caller owns its lifetime.
        /// </summary>
        IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(
            Stream stream,
            CancellationToken ct);
    }
}
