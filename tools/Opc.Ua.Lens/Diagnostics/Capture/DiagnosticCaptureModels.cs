// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
