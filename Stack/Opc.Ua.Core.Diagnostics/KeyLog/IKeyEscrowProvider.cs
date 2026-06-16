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
using System.Threading;
using System.Threading.Tasks;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.KeyLog;

/// <summary>
/// Escrow provider for OPC UA secure-channel symmetric key
/// material that the Pcap binding extracts from live channels.
/// Replaces the default <see cref="DiskKeyEscrowProvider"/>
/// disk-based writer with an alternative destination such as a
/// hardware security module or cloud key vault, satisfying the
/// "no keys on disk" prescription of the Microsoft SFI / edge
/// security bar.
/// </summary>
/// <remarks>
/// Implementations are registered via
/// <c>services.AddSingleton&lt;IKeyEscrowProvider, MyProvider&gt;()</c>
/// before <c>AddPcap()</c>. When a non-default
/// provider is registered, the disk-based default is skipped
/// entirely.
/// </remarks>
public interface IKeyEscrowProvider : IAsyncDisposable
{
    /// <summary>
    /// Begin escrowing keys for a capture session. Returns a
    /// per-session handle that receives subsequent calls.
    /// </summary>
    /// <param name="sessionId">Stable id of the capture session.</param>
    /// <param name="sessionFolder">
    /// The session's per-session folder (used by the disk default
    /// to locate the keylog file; other providers may ignore it).
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    ValueTask<IKeyEscrowSession> BeginSessionAsync(
        string sessionId,
        string sessionFolder,
        CancellationToken cancellationToken);
}

/// <summary>
/// Per-session escrow handle. Disposed when the capture session
/// ends.
/// </summary>
public interface IKeyEscrowSession : IAsyncDisposable
{
    /// <summary>
    /// Hand a single key material record to the escrow provider.
    /// </summary>
    ValueTask EscrowAsync(ChannelKeyMaterial material, CancellationToken cancellationToken);

    /// <summary>
    /// Flush pending escrow operations to durable storage.
    /// </summary>
    ValueTask FlushAsync(CancellationToken cancellationToken);
}
