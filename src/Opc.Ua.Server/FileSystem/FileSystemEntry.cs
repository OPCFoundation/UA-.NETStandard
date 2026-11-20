// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Metadata describing a single entry in an
    /// <see cref="IFileSystemProvider"/>: either a file or a
    /// directory.
    /// </summary>
    /// <param name="Path">
    /// Provider-relative path (forward-slash separated, never starts with a
    /// slash, never ends with a slash except for the root which is an
    /// empty string).
    /// </param>
    /// <param name="Name">
    /// Display / browse name (the last segment of <see cref="Path"/>).
    /// For the root entry this is the provider's
    /// <see cref="IFileSystemProvider.MountName"/>.
    /// </param>
    /// <param name="IsDirectory">
    /// <c>true</c> when the entry represents a directory, <c>false</c>
    /// for a file.
    /// </param>
    /// <param name="Length">
    /// File size in bytes. <c>0</c> for directories.
    /// </param>
    /// <param name="IsWritable">
    /// <c>true</c> when the entry can be written to / modified /
    /// deleted by the current user.
    /// </param>
    /// <param name="LastModifiedUtc">
    /// Last-write timestamp in UTC.
    /// </param>
    /// <param name="MimeType">
    /// IANA media type (best-effort guess from the file extension is
    /// acceptable). Empty string for directories or unknown types.
    /// </param>
    public readonly record struct FileSystemEntry(
        string Path,
        string Name,
        bool IsDirectory,
        long Length,
        bool IsWritable,
        DateTime LastModifiedUtc,
        string MimeType);
}
