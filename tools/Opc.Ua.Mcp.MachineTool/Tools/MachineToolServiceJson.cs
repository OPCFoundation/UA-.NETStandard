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

using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Encodes standard job values and bounded file metadata without a second protocol or reflective serializers.
    /// </summary>
    internal static class MachineToolServiceJson
    {
        /// <summary>
        /// Preserves the ISA-95 UInt64 bitmap, whose success bit is one, not the UA Good code zero.
        /// </summary>
        internal static JsonObject JobStatus(string operation, ulong returnStatus)
        {
            return new JsonObject
            {
                ["operation"] = operation,
                ["returnStatus"] = returnStatus.ToString(CultureInfo.InvariantCulture),
                ["accepted"] = Accepted(returnStatus),
                ["meaning"] = "ISA95 ReturnStatus bitmap; acceptance does not confirm physical execution."
            };
        }

        /// <summary>
        /// Tests only the standard success bit, retaining every other bit in the returned decimal string.
        /// </summary>
        internal static bool Accepted(ulong status)
        {
            return (status & 1UL) != 0;
        }

        /// <summary>
        /// Uses the generated type's native encode method, preserving optional-field masks and variants.
        /// </summary>
        /// <typeparam name="T">The closed generated UA structure contract.</typeparam>
        internal static JsonNode? Encode<T>(T value, IServiceMessageContext context)
            where T : IEncodeable, new()
        {
            using var encoder = new JsonEncoder(context, s_nativeOptions);
            encoder.WriteEncodeable("value", value);
            return JsonNode.Parse(encoder.CloseAndReturnText())!["value"]?.DeepClone();
        }

        /// <summary>
        /// Preserves the complete catalog DataValue and explicit optional masks inside its native structures.
        /// </summary>
        internal static JsonNode Reading(IServiceMessageContext context, in DataValue value)
        {
            using var encoder = new JsonEncoder(context, s_nativeOptions);
            encoder.WriteDataValue("DataValue", value);
            return JsonNode.Parse(encoder.CloseAndReturnText())!;
        }

        /// <summary>
        /// Returns actual namespace-qualified remote identities; no local path or remote handle is exposed.
        /// </summary>
        internal static JsonObject Entry(UaFileSystemInfo entry)
        {
            var result = new JsonObject
            {
                ["nodeId"] = entry.NodeId.ToString(),
                ["path"] = entry.FullPath,
                ["browseName"] = entry.BrowseName.ToString(),
                ["isDirectory"] = entry.IsDirectory
            };
            if (entry is UaFileInfo file)
            {
                result["size"] = file.Size.ToString(CultureInfo.InvariantCulture);
                result["writable"] = file.Writable;
                result["userWritable"] = file.UserWritable;
                result["openCount"] = file.OpenCount;
                result["mimeType"] = file.MimeType;
            }
            return result;
        }

        /// <summary>
        /// Creates a small mutation receipt which can be fully budget-checked before any write.
        /// </summary>
        internal static JsonObject FileReceipt(string operation, int byteCount)
        {
            return new JsonObject
            {
                ["operation"] = operation,
                ["completed"] = true,
                ["byteCount"] = byteCount,
                ["meaning"] = "Remote file operation completed; no job or equipment execution was requested."
            };
        }

        /// <summary>Native verbose values with explicit masks, including present-but-default optional fields.</summary>
        private static readonly JsonEncoderOptions s_nativeOptions = JsonEncoderOptions.Verbose with
        {
            IgnoreOptionalFieldEncodingMask = false
        };
    }
}
