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
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Tests.Replay
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
