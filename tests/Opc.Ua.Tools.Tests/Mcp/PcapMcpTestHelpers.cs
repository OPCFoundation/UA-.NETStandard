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

// Opc.Ua.Mcp targets net10.0 only, so the packet-tool test fixtures only
// build and run on net10.0.
#if NET10_0
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Shared helpers for building deterministic, in-process fake pcap +
    /// keylog capture folders for the packet-capture / decode / replay MCP
    /// tool tests. No privileged NIC access or external network traffic is
    /// involved: the pcap and keylog files are synthesized directly with
    /// the public <c>Opc.Ua.Pcap</c> writer types.
    /// </summary>
    internal static class PcapMcpTestHelpers
    {
        /// <summary>
        /// The conventional file name for the fake pcap file.
        /// </summary>
        public const string PcapFileName = "capture.pcap";

        /// <summary>
        /// The conventional file name for the fake JSON keylog file.
        /// </summary>
        public const string KeyLogFileName = "capture.uakeys.json";

        /// <summary>
        /// Creates a unique, empty temp folder rooted under the NUnit
        /// <see cref="TestContext.CurrentContext.WorkDirectory"/> so the
        /// caller does not depend on any shared session temp folder.
        /// </summary>
        public static string CreateTempFolder(string suffix)
        {
            string folder = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "mcp-pcap-tests",
                suffix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>
        /// Deletes a temp folder (and its contents) created via
        /// <see cref="CreateTempFolder"/>, ignoring the case where it was
        /// never created or already removed.
        /// </summary>
        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        /// <summary>
        /// Writes a single-frame pcap file plus a matching JSON keylog
        /// file (security mode <see cref="MessageSecurityMode.None"/>,
        /// no key bytes required) into <paramref name="folder"/>.
        /// </summary>
        public static async Task<(string PcapPath, string KeyLogPath)> CreateFakeCaptureAsync(
            string folder,
            CancellationToken ct,
            uint channelId = 0x12345678U,
            uint tokenId = 1U)
        {
            Directory.CreateDirectory(folder);
            string pcapPath = Path.Combine(folder, PcapFileName);
            string keyLogPath = Path.Combine(folder, KeyLogFileName);

            var writer = new PcapFileWriter(pcapPath, PcapFileWriter.LinkTypeNull);
            try
            {
                byte[] packet = LoopbackFrameBuilder.Build(
                    fromClient: true,
                    channelId,
                    [0x01, 0x02, 0x03, 0x04]);
                await writer.WriteAsync(DateTimeOffset.UtcNow, packet, ct).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            var keyLogWriter = new UaKeyLogJsonWriter(keyLogPath);
            try
            {
                await keyLogWriter.AppendAsync(
                    CreateKeyMaterial(channelId, tokenId),
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await keyLogWriter.DisposeAsync().ConfigureAwait(false);
            }

            return (pcapPath, keyLogPath);
        }

        /// <summary>
        /// Writes a pcap file with no frames (valid empty pcap global
        /// header only) and no keylog file, for exercising formatters/
        /// decoders against an empty-but-valid capture.
        /// </summary>
        public static async Task<string> CreateEmptyPcapAsync(string folder, CancellationToken ct)
        {
            Directory.CreateDirectory(folder);
            string pcapPath = Path.Combine(folder, PcapFileName);
            var writer = new PcapFileWriter(pcapPath, PcapFileWriter.LinkTypeNull);
            await writer.DisposeAsync().ConfigureAwait(false);
            return pcapPath;
        }

        private static ChannelKeyMaterial CreateKeyMaterial(uint channelId, uint tokenId)
        {
            return new ChannelKeyMaterial(
                channelId,
                tokenId,
                SecurityPolicies.None,
                MessageSecurityMode.None,
                DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Utc),
                60000,
                clientNonce: null,
                serverNonce: null,
                clientSigningKey: null,
                clientEncryptingKey: null,
                clientInitializationVector: null,
                serverSigningKey: null,
                serverEncryptingKey: null,
                serverInitializationVector: null);
        }
    }
}
#endif
