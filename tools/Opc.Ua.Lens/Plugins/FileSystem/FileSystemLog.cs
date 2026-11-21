// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Source-generated logging for the file-system tool (<see cref="FileSystemPlugin"/>
/// and <see cref="FsNode"/>). Plugin event ids are offset from
/// <see cref="UaLensEventIds.FileSystemBase"/> and node event ids from
/// <see cref="UaLensEventIds.FileSystemNodeBase"/>.
/// </summary>
internal static partial class FileSystemLog
{
    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 0, Level = LogLevel.Warning,
        Message = "File System opened without an active session.")]
    public static partial void FsOpenedWithoutSession(this ILogger logger);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 1, Level = LogLevel.Information,
        Message = "File System filter: FileSystem={Fs} Directory={Dir} File={File}")]
    public static partial void FsFilterChanged(this ILogger logger, bool fs, bool dir, bool file);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 2, Level = LogLevel.Warning,
        Message = "Create directory '{Name}' in '{Path}' failed.")]
    public static partial void FsCreateDirectoryFailed(
        this ILogger logger, Exception exception, string name, string path);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 3, Level = LogLevel.Warning,
        Message = "Rename '{Path}' → '{NewName}' failed.")]
    public static partial void FsRenameFailed(
        this ILogger logger, Exception exception, string path, string newName);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 4, Level = LogLevel.Warning,
        Message = "Delete '{Path}' failed.")]
    public static partial void FsDeleteFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 5, Level = LogLevel.Warning,
        Message = "Import '{Local}' to '{Path}' failed.")]
    public static partial void FsImportFailed(
        this ILogger logger, Exception exception, string local, string path);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 6, Level = LogLevel.Warning,
        Message = "Export '{Path}' failed.")]
    public static partial void FsExportFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 7, Level = LogLevel.Debug,
        Message = "OpenServerFileSystem threw (root will not be added).")]
    public static partial void FsOpenServerFileSystemThrew(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 8, Level = LogLevel.Warning,
        Message = "Picked file '{Id}' has no containing FileDirectoryType — root not attached.")]
    public static partial void FsPickedFileNoDirectory(this ILogger logger, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 9, Level = LogLevel.Information,
        Message = "File System root attached: {Name} ({Id})")]
    public static partial void FsRootAttached(this ILogger logger, string name, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemBase + 10, Level = LogLevel.Warning,
        Message = "Attach FileSystem root '{Id}' failed.")]
    public static partial void FsAttachRootFailed(this ILogger logger, Exception exception, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemNodeBase + 0, Level = LogLevel.Warning,
        Message = "FileSystem: enumerate '{Path}' failed.")]
    public static partial void FsNodeEnumerateFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = UaLensEventIds.FileSystemNodeBase + 1, Level = LogLevel.Debug,
        Message = "FileSystem: refresh '{Path}' failed.")]
    public static partial void FsNodeRefreshFailed(this ILogger logger, Exception exception, string path);
}
