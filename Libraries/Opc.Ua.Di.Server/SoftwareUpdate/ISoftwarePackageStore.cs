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

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// Storage abstraction over the software-update package layer
    /// defined by OPC 10000-100 §10.3. Decouples the DI server's
    /// state-machine / file-transfer wiring from the actual backing
    /// store (in-memory, disk, network share, container image, ...).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be safe for concurrent calls. The store
    /// is consulted both during normal browse / read operations
    /// (read-only) and during the file-transfer pipeline that supplies
    /// the package binary to the DI <c>SoftwareLoadingType</c> file
    /// node.
    /// </para>
    /// <para>
    /// The default <see cref="FileSystemPackageStore"/> composes over
    /// an <see cref="Opc.Ua.Server.FileSystem.IFileSystemProvider"/> so any
    /// provider already used for the server's <c>FileSystem</c> mount
    /// can also serve software packages.
    /// </para>
    /// </remarks>
    public interface ISoftwarePackageStore
    {
        /// <summary>
        /// Enumerates every package currently known to the store.
        /// </summary>
        IAsyncEnumerable<SoftwarePackage> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the package whose <see cref="SoftwarePackage.Id"/>
        /// matches <paramref name="packageId"/>; or <see langword="null"/>
        /// when no such package exists.
        /// </summary>
        ValueTask<SoftwarePackage?> GetAsync(string packageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns <see langword="true"/> when the store contains a
        /// package with the supplied id.
        /// </summary>
        ValueTask<bool> ExistsAsync(string packageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens the binary payload of the package for reading.
        /// Throws <see cref="FileNotFoundException"/> when the package
        /// does not exist.
        /// </summary>
        ValueTask<Stream> OpenReadAsync(string packageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a new package or replaces an existing one with the
        /// same id. The store records the metadata, then copies
        /// <paramref name="payload"/> into its backing storage and
        /// returns the final <see cref="SoftwarePackage"/> (with the
        /// store's computed size and timestamp).
        /// </summary>
        ValueTask<SoftwarePackage> AddAsync(
            SoftwarePackage metadata,
            Stream payload,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the package with the supplied id. Returns
        /// <see langword="true"/> when a package was removed, or
        /// <see langword="false"/> when no matching package existed.
        /// </summary>
        ValueTask<bool> DeleteAsync(string packageId, CancellationToken cancellationToken = default);
    }
}
