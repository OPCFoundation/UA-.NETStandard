// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Plugins.Companions;

/// <summary>
/// Supplies a bounded, owned package snapshot during explicit task preparation.
/// </summary>
internal interface ICompanionPackageReader
{
    ValueTask<ByteString> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken);
}

/// <summary>
/// Reads only an explicitly selected local file. No path or open handle survives
/// preparation; execution uses the verified bytes, not a reopened mutable file.
/// </summary>
internal sealed class CompanionPackageReader : ICompanionPackageReader
{
    public async ValueTask<ByteString> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Select an absolute local package path, not a network share.", nameof(path));
        }
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            long length = stream.Length;
            if (length is <= 0 || length > maximumBytes)
            {
                throw new ArgumentException(
                    $"The package must contain 1 through {maximumBytes} bytes.", nameof(path));
            }
            var content = new byte[(int)length];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            var extra = new byte[1];
            if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new IOException("The package size changed while it was being prepared.");
            }
            return new ByteString(content);
        }
    }
}
