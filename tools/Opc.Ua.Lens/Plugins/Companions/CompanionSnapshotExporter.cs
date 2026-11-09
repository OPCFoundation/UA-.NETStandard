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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Exports displayed values and their source identities, not executable workspace or protocol data.
    /// </summary>
    internal static class CompanionSnapshotExporter
    {
        /// <summary>
        /// Serializes a bounded display snapshot to a new explicit local destination.
        /// </summary>
        public static Task WriteAsync(
            string destination, CompanionTarget target, ArrayOf<CompanionValue> values, NamespaceTable namespaces,
            Action requireCurrent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(requireCurrent);
            if (!string.Equals(Path.GetExtension(destination), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Choose a new local .json file.", nameof(destination));
            }
            cancellationToken.ThrowIfCancellationRequested();
            requireCurrent();
            ByteString content = Render(target, values, namespaces);
            return CompanionLocalExport.WriteAsync(destination, content, requireCurrent, cancellationToken);
        }

        /// <summary>
        /// Renders a display-only JSON document, enforcing bounds before it can be published.
        /// </summary>
        internal static ByteString Render(
            CompanionTarget target, ArrayOf<CompanionValue> values, NamespaceTable namespaces)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(namespaces);
            if (target.NodeId.IsNull || values.Count is < 1 or > 1024)
            {
                throw new ArgumentException("A selected target and 1 to 1024 displayed values are required.",
                    nameof(values));
            }
            using var buffer = new SnapshotBuffer();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WriteString("representation", "display-snapshot");
                writer.WriteString("providerId", BoundedText(target.ProviderId));
                writer.WriteString("targetId", PortableId(target.NodeId, namespaces));
                writer.WriteString("targetName", BoundedText(target.DisplayName));
                writer.WriteStartArray("values");
                foreach (CompanionValue value in values)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", BoundedText(value.Name));
                    writer.WriteString("displayValue", BoundedText(value.Text));
                    writer.WriteString("builtInType", value.Value.TypeInfo.BuiltInType.ToString());
                    writer.WriteNumber("valueRank", value.Value.TypeInfo.ValueRank);
                    writer.WriteNumber("statusCode", (uint)value.Quality);
                    if (!value.SourceNodeId.IsNull)
                    {
                        writer.WriteString("sourceNodeId", PortableId(value.SourceNodeId, namespaces));
                    }
                    if (value.SourceTimestamp is { } time)
                    {
                        writer.WriteString("sourceTimestamp", time);
                    }
                    if (value.EngineeringMetadata is { } metadata)
                    {
                        writer.WriteString("engineeringMetadata", BoundedText(metadata));
                    }
                    writer.WriteEndObject();
                    writer.Flush();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return ByteString.From(buffer.ToArray());
        }

        private static string PortableId(NodeId id, NamespaceTable namespaces)
        {
            if (id.NamespaceIndex >= namespaces.Count)
            {
                throw new InvalidOperationException("The snapshot source namespace is no longer available.");
            }
            return BoundedText(NodeId.ToExpandedNodeId(id, namespaces).ToString());
        }

        private static string BoundedText(string text)
        {
            if (text.Length > 4096)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "A snapshot field exceeds 4096 characters.");
            }
            return text;
        }

        /// <summary>
        /// Caps the writer's flushed output before increasing the backing allocation.
        /// </summary>
        private sealed class SnapshotBuffer : MemoryStream
        {
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (buffer.Length > CompanionLocalExport.MaximumBytes - Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The displayed snapshot exceeds 1 MiB.");
                }
                base.Write(buffer);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count > CompanionLocalExport.MaximumBytes - Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The displayed snapshot exceeds 1 MiB.");
                }
                base.Write(buffer, offset, count);
            }
        }
    }
}
