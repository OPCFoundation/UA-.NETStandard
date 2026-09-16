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
