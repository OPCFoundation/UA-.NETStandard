// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;
using Opc.Ua.Pcap.Dissection;

namespace Opc.Ua.Mcp.Serialization
{
    /// <summary>
    /// Source-generated serialization metadata for the types the packet diagnostics
    /// tools render as JSON. Using a generated context instead of the reflection-based
    /// serializer keeps this assembly trim- and Native-AOT-safe.
    /// </summary>
    [JsonSerializable(typeof(DecodedServiceCall))]
    internal sealed partial class PcapMcpJsonContext : JsonSerializerContext
    {
    }
}
