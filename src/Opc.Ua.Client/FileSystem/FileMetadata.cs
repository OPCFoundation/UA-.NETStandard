// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Snapshot of the seven well-known
    /// <see cref="ObjectTypeIds.FileType"/> property values populated
    /// by <see cref="UaFileInfo.RefreshAsync"/>. Optional fields are
    /// <c>null</c> when the server does not expose the property.
    /// </summary>
    internal readonly struct FileMetadata
    {
        public ulong? Size { get; init; }
        public bool? Writable { get; init; }
        public bool? UserWritable { get; init; }
        public ushort? OpenCount { get; init; }
        public string? MimeType { get; init; }
        public uint? MaxByteStringLength { get; init; }
        public DateTime? LastModifiedTime { get; init; }
    }
}
