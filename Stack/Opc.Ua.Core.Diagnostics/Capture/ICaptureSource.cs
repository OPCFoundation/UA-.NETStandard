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
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Capture
{
    /// <summary>
    /// An OPC UA-aware packet-capture source. Implementations buffer the
    /// captured traffic and any associated key material to disk so the
    /// recording can be replayed and decoded later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All implementations are async and may freely use I/O. They are
    /// sealed by default per the repository convention; extensibility is
    /// achieved by registering additional sources with the
    /// <c>CaptureSessionManager</c>.
    /// </para>
    /// <para>
    /// Lifecycle: construction → <see cref="StartAsync"/> →
    /// (capture work) → <see cref="StopAsync"/> →
    /// <see cref="ReadCapturedFramesAsync"/> (any number of times) →
    /// <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </para>
    /// </remarks>
    public interface ICaptureSource : IAsyncDisposable
    {
        /// <summary>
        /// Output formats this source can produce. Sources that capture
        /// link-layer frames (NIC, in-proc taps using loopback framing)
        /// can produce <see cref="FormatKind.Pcap"/> and
        /// <see cref="FormatKind.PcapNg"/>. Replay sources can produce
        /// every format the underlying file supports.
        /// </summary>
        IReadOnlySet<FormatKind> SupportedFormats { get; }

        /// <summary>
        /// Number of OPC UA chunks captured so far.
        /// </summary>
        long FrameCount { get; }

        /// <summary>
        /// Number of bytes (including all framing) captured so far.
        /// </summary>
        long ByteCount { get; }

        /// <summary>
        /// Begin capturing. Implementations should validate their inputs
        /// and throw a <see cref="PcapDiagnosticsException"/> with a
        /// human-friendly message on misconfiguration.
        /// </summary>
        ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct);

        /// <summary>
        /// Stop capturing and flush all buffers to disk. After this
        /// returns successfully the captured records are safe to enumerate
        /// via <see cref="ReadCapturedFramesAsync"/>.
        /// </summary>
        ValueTask StopAsync(CancellationToken ct);

        /// <summary>
        /// Returns the full path to the underlying raw libpcap-format file
        /// if the source produces one (NIC source, in-proc taps that
        /// synthesize loopback framing). Returns <c>null</c> otherwise.
        /// </summary>
        string? GetRawPcapFilePath();

        /// <summary>
        /// Returns the full path to the keylog file produced by this
        /// source (if any). Multiple formats may be produced - this
        /// returns the canonical one (.uakeys.json).
        /// </summary>
        string? GetKeyLogFilePath();

        /// <summary>
        /// Replay every captured key material snapshot from buffered
        /// storage in the order it was observed.
        /// </summary>
        IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(CancellationToken ct);

        /// <summary>
        /// Replay every captured OPC UA chunk from buffered storage. May
        /// be called any number of times after <see cref="StopAsync"/>.
        /// </summary>
        IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
            long? maxFrames,
            CancellationToken ct);
    }

    /// <summary>
    /// Domain-specific exception thrown by the diagnostics pipeline so
    /// MCP tools can surface a clear, single-line message to the caller
    /// without exposing internals.
    /// </summary>
    public sealed class PcapDiagnosticsException : IOException
    {
        /// <summary>
        /// Constructs a new diagnostics exception with the supplied
        /// default message.
        /// </summary>
        public PcapDiagnosticsException()
            : base("An OPC UA packet-capture diagnostics error occurred.")
        {
        }

        /// <summary>
        /// Constructs a new diagnostics exception with the supplied
        /// message.
        /// </summary>
        public PcapDiagnosticsException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Constructs a new diagnostics exception with the supplied
        /// message and inner exception.
        /// </summary>
        public PcapDiagnosticsException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructs a new diagnostics exception with the supplied
        /// message and HRESULT.
        /// </summary>
        /// <param name="message">The diagnostic message; may be <c>null</c>.</param>
        /// <param name="hresult">The HRESULT to associate with the exception.</param>
        public PcapDiagnosticsException(string? message, int hresult)
            : base(message, hresult)
        {
        }
    }
}
