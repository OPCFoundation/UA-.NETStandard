/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Replay
{
    internal static class ReplayTestHelpers
    {
        public static async ValueTask<FakeCaptureFolder> CreateFakeCaptureFolderAsync(
            string rootFolder,
            IReadOnlyList<FakeCaptureFrame> frames,
            CancellationToken ct)
        {
            string folder = Path.Combine(rootFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pcapPath = Path.Combine(folder, "capture.pcap");
            string keyLogPath = Path.Combine(folder, "capture.uakeys.json");

            var writer = new PcapFileWriter(pcapPath, PcapFileWriter.LinkTypeNull);
            try
            {
                foreach (FakeCaptureFrame frame in frames)
                {
                    byte[] packet = LoopbackFrameBuilder.Build(frame.FromClient, frame.ChannelId, frame.Payload.Span);
                    await writer.WriteAsync(frame.Timestamp, packet, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            var keyLogWriter = new UaKeyLogJsonWriter(keyLogPath);
            try
            {
                await keyLogWriter.AppendAsync(
                    PcapTestHelpers.CreateMaterial(SecurityPolicies.None, MessageSecurityMode.None),
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await keyLogWriter.DisposeAsync().ConfigureAwait(false);
            }

            return new FakeCaptureFolder(folder, pcapPath, keyLogPath);
        }

        public static async ValueTask<ReplayCaptureSource> CreateReplaySourceAsync(
            FakeCaptureFolder capture,
            bool includeKeyLog,
            CancellationToken ct)
        {
            var source = new ReplayCaptureSource();
            await source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = capture.PcapFilePath,
                    KeyLogFilePath = includeKeyLog ? capture.KeyLogFilePath : null
                },
                ct).ConfigureAwait(false);
            return source;
        }

        public static FakeCaptureFrame CreateFrame(
            DateTimeOffset timestamp,
            bool fromClient,
            params byte[] payload)
        {
            return new FakeCaptureFrame(timestamp, fromClient, payload, 0x12345678U);
        }
    }

    internal sealed record FakeCaptureFolder(string Folder, string PcapFilePath, string KeyLogFilePath);

    internal sealed record FakeCaptureFrame(
        DateTimeOffset Timestamp,
        bool FromClient,
        ReadOnlyMemory<byte> Payload,
        uint ChannelId);
}
