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

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.KeyLog
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
