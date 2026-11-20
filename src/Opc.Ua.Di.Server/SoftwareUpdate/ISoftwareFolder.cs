/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// Multi-version software repository for one DI topology element
    /// (typically a device). Models OPC 10000-100 §10.3.5
    /// <c>SoftwareFolderType</c> — the folder under
    /// <c>SoftwareType</c> that lists every known software version
    /// (current, previous, future) and tracks which one is currently
    /// active on the device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations are scoped to a single topology element. Use
    /// one folder instance per device that supports the
    /// <c>SoftwareFolderType</c> facet.
    /// </para>
    /// <para>
    /// All operations must be safe for concurrent calls.
    /// </para>
    /// </remarks>
    public interface ISoftwareFolder
    {
        /// <summary>
        /// The topology element this folder belongs to (typically a
        /// device NodeId).
        /// </summary>
        NodeId ElementId { get; }

        /// <summary>
        /// Streams every software version known to the folder.
        /// </summary>
        IAsyncEnumerable<SoftwarePackage> ListVersionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the version currently marked as active on the
        /// device, or <see langword="null"/> when no version is
        /// active.
        /// </summary>
        ValueTask<SoftwarePackage?> GetCurrentVersionAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the package with the supplied
        /// <paramref name="version"/> identifier; or
        /// <see langword="null"/> when no such version exists.
        /// </summary>
        ValueTask<SoftwarePackage?> GetVersionAsync(
            string version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens the binary payload of the version for reading.
        /// </summary>
        ValueTask<Stream> OpenVersionAsync(
            string version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a new software version. If a version with the same
        /// identifier already exists it is replaced.
        /// </summary>
        ValueTask<SoftwarePackage> AddVersionAsync(
            SoftwarePackage metadata,
            Stream payload,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the version with the supplied identifier. Returns
        /// <see langword="true"/> when a version was removed.
        /// </summary>
        ValueTask<bool> RemoveVersionAsync(
            string version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks the supplied version as the currently active one on
        /// the device. Throws <see cref="System.ArgumentException"/>
        /// when the version is unknown.
        /// </summary>
        ValueTask SetCurrentVersionAsync(
            string version,
            CancellationToken cancellationToken = default);
    }
}
