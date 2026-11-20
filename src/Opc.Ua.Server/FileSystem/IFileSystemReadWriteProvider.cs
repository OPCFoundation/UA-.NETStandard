/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Optional provider capability for an exclusive, seekable stream with one shared read/write cursor.
    /// Providers implementing only <see cref="IFileSystemProvider"/> continue to reject combined opens.
    /// </summary>
    public interface IFileSystemReadWriteProvider : IFileSystemProvider
    {
        /// <summary>
        /// Opens a readable and writable stream. Append positions the cursor at the initial end of the
        /// file but must permit subsequent seeks; truncate erases existing content.
        /// </summary>
        ValueTask<Stream> OpenReadWriteAsync(string path, FileWriteMode mode, CancellationToken ct);
    }
}
