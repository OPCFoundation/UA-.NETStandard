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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Pcap;
using Opc.Ua.PubSub.Pcap.KeyLog;
using Opc.Ua.PubSub.Transports;
using OpcUaMcpServerOptions = Opc.Ua.Mcp.McpServerOptions;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for dissecting OPC UA PubSub packet captures.
    /// </summary>
    [McpServerToolType]
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "MCP discovers tool types through reflection; TODO: remove when supported.")]
    internal sealed class PubSubDecodeTools
    {
        private const int kMaxResponseBytes = 10 * 1024 * 1024;
        private const uint kLinkTypeEthernet = 1;
        private const string kUadpTransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        /// <summary>
        /// Dissects the last stopped OPC UA PubSub capture.
        /// </summary>
        [McpServerTool(Name = "pubsub_dissect_capture")]
        [Description("Dissects the last stopped in-process PubSub capture. format='text' returns a timeline; " +
            "format='json' returns an array of dissection results. Provide keyLogPath, or call pubsub_load_keylog " +
            "first, to decrypt encrypted UADP frames.")]
        public static async Task<IList<ContentBlock>> DissectCaptureAsync(
            PubSubCaptureSessionManager manager,
            [Description("Output format: text | json.")] string format = "text",
            [Description("Optional PubSub JSON-lines key-log path under the MCP pcap base folder.")]
            string? keyLogPath = null,
            [Description("Maximum frames to dissect. Default = all.")] long? maxFrames = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);

            IPubSubCaptureSource source = await PubSubCaptureTools.GetLastStoppedSourceAsync(manager, ct)
                .ConfigureAwait(false);
            using CapturedKeyLogKeyResolver? resolver = await CreateKeyResolverAsync(keyLogPath, ct)
                .ConfigureAwait(false);
            PubSubOfflineDissector dissector = CreateDissector(resolver);
            return await FormatAsync(source.ReadCapturedFramesAsync(maxFrames, ct), dissector, format, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Decodes a libpcap file containing UDP PubSub UADP datagrams.
        /// </summary>
        [McpServerTool(Name = "pubsub_decode_pcap")]
        [Description("Reads a libpcap .pcap file containing Ethernet/IPv4/UDP PubSub UADP datagrams and dissects " +
            "the UDP payloads. pcapng input is not supported by the shared pcap reader.")]
        public static async Task<IList<ContentBlock>> DecodePcapAsync(
            IServiceProvider services,
            [Description("Absolute or relative .pcap path under the MCP pcap base folder.")] string pcapPath,
            [Description("Output format: text | json.")] string format = "text",
            [Description("Optional PubSub JSON-lines key-log path under the MCP pcap base folder.")]
            string? keyLogPath = null,
            [Description("Maximum UDP frames to dissect. Default = all.")] long? maxFrames = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(pcapPath);

            string allowedRoot = GetPcapAllowedRoot(services);
            pcapPath = PacketDecodeTools.ResolveAndValidateDecodePath(pcapPath, allowedRoot);
            keyLogPath = string.IsNullOrWhiteSpace(keyLogPath)
                ? null
                : PacketDecodeTools.ResolveAndValidateDecodePath(keyLogPath, allowedRoot);

            using CapturedKeyLogKeyResolver? resolver = await CreateKeyResolverAsync(keyLogPath, ct)
                .ConfigureAwait(false);
            PubSubOfflineDissector dissector = CreateDissector(resolver);
            return await FormatAsync(ReadPubSubFramesFromPcapAsync(pcapPath, maxFrames, ct), dissector, format, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Loads a PubSub key-log file for later capture dissection.
        /// </summary>
        [McpServerTool(Name = "pubsub_load_keylog")]
        [Description("Loads a PubSub JSON-lines key-log file and keeps it in memory for later pubsub_dissect_capture " +
            "or pubsub_decode_pcap calls when keyLogPath is omitted. Treat this file as a secret.")]
        public static async Task<PubSubKeyLogInfo> LoadKeyLogAsync(
            IServiceProvider services,
            [Description("PubSub JSON-lines key-log path under the MCP pcap base folder.")] string keyLogPath,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(keyLogPath);

            string allowedRoot = GetPcapAllowedRoot(services);
            keyLogPath = PacketDecodeTools.ResolveAndValidateDecodePath(keyLogPath, allowedRoot);
            List<PubSubKeyMaterial> keys = await ReadKeyMaterialAsync(keyLogPath, ct).ConfigureAwait(false);
            await StoreLoadedKeyMaterialAsync(keys, ct).ConfigureAwait(false);
            return new PubSubKeyLogInfo
            {
                FilePath = keyLogPath,
                KeyCount = keys.Count
            };
        }

        private static async Task<IList<ContentBlock>> FormatAsync(
            IAsyncEnumerable<PubSubCaptureFrame> frames,
            PubSubOfflineDissector dissector,
            string format,
            CancellationToken ct)
        {
            string normalized = format.Trim().ToLowerInvariant();
            if (normalized is "json")
            {
                var formatter = new PubSubJsonFormatter();
                byte[] bytes = await formatter.FormatAsync(frames, dissector, ct).ConfigureAwait(false);
                return CreateText(Encoding.UTF8.GetString(bytes));
            }

            if (normalized is "text" or "")
            {
                var formatter = new PubSubTextFormatter();
                string text = await formatter.FormatAsync(frames, dissector, ct).ConfigureAwait(false);
                return CreateText(text);
            }

            throw new PcapDiagnosticsException(
                $"Unsupported PubSub dissection format '{format}'. Use text or json.");
        }

        private static async ValueTask<CapturedKeyLogKeyResolver?> CreateKeyResolverAsync(
            string? keyLogPath,
            CancellationToken ct)
        {
            List<PubSubKeyMaterial>? keys = string.IsNullOrWhiteSpace(keyLogPath)
                ? await CopyLoadedKeyMaterialAsync(ct).ConfigureAwait(false)
                : await ReadKeyMaterialAsync(keyLogPath!, ct).ConfigureAwait(false);
            if (keys.Count == 0)
            {
                return null;
            }

            try
            {
                return new CapturedKeyLogKeyResolver(keys);
            }
            finally
            {
                foreach (PubSubKeyMaterial key in keys)
                {
                    key.Dispose();
                }
            }
        }

        private static PubSubOfflineDissector CreateDissector(IPubSubKeyResolver? resolver)
        {
            if (resolver is null)
            {
                return new PubSubOfflineDissector();
            }

            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
            return new PubSubOfflineDissector(context, resolver);
        }

        private static async Task<List<PubSubKeyMaterial>> ReadKeyMaterialAsync(
            string keyLogPath,
            CancellationToken ct)
        {
            var reader = new PubSubKeyLogReader(keyLogPath);
            List<PubSubKeyMaterial> keys = [];
            await foreach (PubSubKeyMaterial key in reader.ReadAllAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                keys.Add(CopyKeyMaterial(key));
                key.Dispose();
            }

            return keys;
        }

        private static async ValueTask StoreLoadedKeyMaterialAsync(
            List<PubSubKeyMaterial> keys,
            CancellationToken ct)
        {
            await m_loadedKeyGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (PubSubKeyMaterial key in m_loadedKeyMaterial)
                {
                    key.Dispose();
                }

                m_loadedKeyMaterial = [.. keys];
            }
            finally
            {
                m_loadedKeyGate.Release();
            }
        }

        private static async ValueTask<List<PubSubKeyMaterial>> CopyLoadedKeyMaterialAsync(CancellationToken ct)
        {
            await m_loadedKeyGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                List<PubSubKeyMaterial> keys = [];
                foreach (PubSubKeyMaterial key in m_loadedKeyMaterial)
                {
                    keys.Add(CopyKeyMaterial(key));
                }

                return keys;
            }
            finally
            {
                m_loadedKeyGate.Release();
            }
        }

        private static async IAsyncEnumerable<PubSubCaptureFrame> ReadPubSubFramesFromPcapAsync(
            string pcapPath,
            long? maxFrames,
            [EnumeratorCancellation] CancellationToken ct)
        {
            long yielded = 0;
            await foreach (PcapRecord record in PcapFileReader.ReadAllAsync(pcapPath, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                if (maxFrames.HasValue && yielded >= maxFrames.Value)
                {
                    yield break;
                }

                if (!TryGetUdpPayload(record, out ReadOnlyMemory<byte> payload, out string? endpoint))
                {
                    continue;
                }

                yielded++;
                yield return new PubSubCaptureFrame(
                    record.Timestamp,
                    PubSubCaptureDirection.Unknown,
                    kUadpTransportProfileUri,
                    payload.ToArray(),
                    endpoint);
            }
        }

        private static bool TryGetUdpPayload(
            in PcapRecord record,
            out ReadOnlyMemory<byte> payload,
            out string? endpoint)
        {
            payload = default;
            endpoint = null;
            ReadOnlySpan<byte> data = record.Data.Span;
            if (record.LinkType != kLinkTypeEthernet)
            {
                payload = record.Data;
                return data.Length > 0;
            }

            if (data.Length < 42 || BinaryPrimitives.ReadUInt16BigEndian(data[12..14]) != 0x0800)
            {
                return false;
            }

            const int ipOffset = 14;
            int headerLength = (data[ipOffset] & 0x0F) * 4;
            if (headerLength < 20 || data.Length < ipOffset + headerLength + 8 || data[ipOffset + 9] != 17)
            {
                return false;
            }

            int udpOffset = ipOffset + headerLength;
            ushort udpLength = BinaryPrimitives.ReadUInt16BigEndian(data[(udpOffset + 4)..(udpOffset + 6)]);
            if (udpLength < 8 || data.Length < udpOffset + udpLength)
            {
                return false;
            }

            ushort destinationPort = BinaryPrimitives.ReadUInt16BigEndian(data[(udpOffset + 2)..(udpOffset + 4)]);
            IPAddress destinationAddress = new(data.Slice(ipOffset + 16, 4));
            endpoint = FormattableString.Invariant($"{destinationAddress}:{destinationPort}");
            payload = record.Data.Slice(udpOffset + 8, udpLength - 8);
            return payload.Length > 0;
        }

        private static IList<ContentBlock> CreateText(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.LongLength > kMaxResponseBytes)
            {
                throw new PcapDiagnosticsException(
                    $"PubSub dissection output is {bytes.LongLength} bytes, which exceeds the 10 MB MCP " +
                    "response cap.");
            }

            return
            [
                new TextContentBlock
                {
                    Text = text
                }
            ];
        }

        private static string GetPcapAllowedRoot(IServiceProvider services)
        {
            OpcUaMcpServerOptions? mcpOptions =
                services.GetService(typeof(OpcUaMcpServerOptions)) as OpcUaMcpServerOptions;
            if (mcpOptions is not null &&
                !string.IsNullOrWhiteSpace(mcpOptions.PcapBaseFolder))
            {
                return Path.GetFullPath(mcpOptions.PcapBaseFolder!);
            }

            PcapOptions? options = services.GetService(typeof(PcapOptions)) as PcapOptions;
            return options?.BaseFolder ??
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OPCFoundation",
                    "opcua-pcap");
        }

        private static PubSubKeyMaterial CopyKeyMaterial(PubSubKeyMaterial key)
        {
            return new PubSubKeyMaterial(
                key.SecurityGroupId,
                key.TokenId,
                key.SecurityPolicyUri,
                key.SigningKey.ToArray(),
                key.EncryptingKey.ToArray(),
                key.KeyNonce.ToArray());
        }

        private static readonly SemaphoreSlim m_loadedKeyGate = new(1, 1);
        private static IReadOnlyList<PubSubKeyMaterial> m_loadedKeyMaterial = [];
    }

    /// <summary>
    /// Status of a loaded PubSub key-log file.
    /// </summary>
    public sealed class PubSubKeyLogInfo
    {
        /// <summary>
        /// Gets the key-log file path.
        /// </summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>
        /// Gets the number of loaded keys.
        /// </summary>
        public int KeyCount { get; init; }
    }
}
