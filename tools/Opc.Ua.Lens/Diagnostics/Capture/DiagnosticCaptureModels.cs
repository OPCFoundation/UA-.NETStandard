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
using Opc.Ua;

namespace UaLens.Diagnostics.Capture
{
    /// <summary>
    /// Supported capture sources; replay only reads an explicitly selected local file.
    /// </summary>
    internal enum DiagnosticCaptureKind
    {
        Client,
        PubSub,
        Replay
    }

    /// <summary>
    /// Lifecycle of one bounded packet capture or offline replay.
    /// </summary>
    internal enum DiagnosticCapturePhase
    {
        Empty,
        Starting,
        Recording,
        Stopping,
        Completed,
        Failed
    }

    /// <summary>
    /// An available workspace-owned transport tap, not a network endpoint or provider-loading instruction.
    /// </summary>
    internal sealed record DiagnosticCaptureSource(string Id, DiagnosticCaptureKind Kind, string Name);

    /// <summary>
    /// Explicit payload/frame/time budgets; synthetic PCAP framing has a separate bounded storage allowance.
    /// </summary>
    internal sealed record DiagnosticCaptureLimits(int DurationSeconds = 10, int MaxFrames = 256,
        long MaxPayloadBytes = 1024 * 1024)
    {
        /// <summary>
        /// Rejects requests that exceed the desktop's fixed recording budget.
        /// </summary>
        public void RequireValid()
        {
            if (DurationSeconds is < 1 or > 60 ||
                MaxFrames is < 1 or > 2048 ||
                MaxPayloadBytes is < 1 or > MaximumPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DurationSeconds), "Use 1-60 seconds, 1-2048 frames and at most 8 MiB of payload.");
            }
        }

        /// <summary>The maximum retained transport payload per capture.</summary>
        public const long MaximumPayloadBytes = 8 * 1024 * 1024;

        /// <summary>The bounded PCAP storage/import allowance, including framing and record headers.</summary>
        public const long MaximumFileBytes = 12 * 1024 * 1024;
    }

    /// <summary>
    /// Redacted packet identity; no endpoint, payload, decryption key or decoded application value is retained here.
    /// </summary>
    internal sealed record DiagnosticPacketRow(
        int Ordinal, DateTimeOffset Timestamp, int Bytes, string Sha256);

    /// <summary>
    /// Immutable status shared by the diagnostics view and its allowlist export.
    /// </summary>
    internal sealed record DiagnosticCaptureSnapshot(
        Guid Id,
        DiagnosticCaptureKind Kind,
        DiagnosticCapturePhase Phase,
        long Frames,
        long PayloadBytes,
        long FileBytes,
        string Sha256,
        string Status,
        ArrayOf<DiagnosticPacketRow> Packets)
    {
        /// <summary>
        /// Gets whether a start, recording, flush or replay operation still owns resources.
        /// </summary>
        public bool IsBusy => Phase is DiagnosticCapturePhase.Starting or
            DiagnosticCapturePhase.Recording or DiagnosticCapturePhase.Stopping;

        /// <summary>
        /// Gets the initial offline state. Restoring the shell never starts capture.
        /// </summary>
        public static DiagnosticCaptureSnapshot Empty { get; } = new(
            Guid.Empty, DiagnosticCaptureKind.Client, DiagnosticCapturePhase.Empty,
            0, 0, 0, string.Empty, "No capture. Starting requires explicit packet-data consent.", []);
    }
}
