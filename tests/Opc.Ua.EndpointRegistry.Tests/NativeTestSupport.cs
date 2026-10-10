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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Tests
{
    /// <summary>
    /// Shared context, mapper and language-neutral native renderings used by the mapping tests.
    /// </summary>
    internal static class NativeTestSupport
    {
        public static ServiceMessageContext Context { get; } = CreateContext();

        public static RegistryRecordMapper Mapper { get; } = EndpointRegistryNativeCatalog.CreateMapper(
            Context,
            [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider()]);

        public static RegistryValueDataType Json(string text)
        {
            return RegistryValues.Parse(Encoding.UTF8.GetBytes(text));
        }

        public static string ToJson(RegistryValueDataType value)
        {
            return Encoding.UTF8.GetString(RegistryValues.ToJson(value).ToArray());
        }

        public static ArrayOf<RegistryNativeField> Fields(IEncodeable value)
        {
            return RegistryNativeFields.Read(value, Context);
        }

        public static Variant Field(IEncodeable value, string name)
        {
            foreach (RegistryNativeField field in Fields(value))
            {
                if (field.Name == name)
                {
                    return field.Value;
                }
            }
            throw new ArgumentException("No native field " + name);
        }

        public static IEncodeable? Structure(IEncodeable value, string name)
        {
            Variant field = Field(value, name);
            return field.TryGetValue(out ExtensionObject extension) && !extension.IsNull &&
                extension.TryGetValue(out IEncodeable? body)
                ? body
                : null;
        }

        /// <summary>
        /// Renders a native value as the specification tooling does: type name, ordered fields,
        /// ByteString values as lower-case hex and null ExtensionObjects as null.
        /// </summary>
        public static string Render(IEncodeable value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                Render(writer, value);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void Render(Utf8JsonWriter writer, IEncodeable value)
        {
            writer.WriteStartObject();
            writer.WriteString("$type", value.GetType().Name);
            writer.WritePropertyName("fields");
            writer.WriteStartArray();
            foreach (RegistryNativeField field in Fields(value))
            {
                writer.WriteStartArray();
                writer.WriteStringValue(field.Name);
                Render(writer, field.Value);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void Render(Utf8JsonWriter writer, Variant value)
        {
            if (value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }
            bool scalar = value.TypeInfo.ValueRank == ValueRanks.Scalar;
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.String when scalar && value.TryGetValue(out string text):
                    writer.WriteStringValue(text);
                    return;
                case BuiltInType.String when value.TryGetValue(out ArrayOf<string> texts):
                    writer.WriteStartArray();
                    foreach (string item in texts)
                    {
                        writer.WriteStringValue(item);
                    }
                    writer.WriteEndArray();
                    return;
                case BuiltInType.Boolean when scalar && value.TryGetValue(out bool flag):
                    writer.WriteBooleanValue(flag);
                    return;
                case BuiltInType.Boolean when value.TryGetValue(out ArrayOf<bool> flags):
                    writer.WriteStartArray();
                    foreach (bool item in flags)
                    {
                        writer.WriteBooleanValue(item);
                    }
                    writer.WriteEndArray();
                    return;
                case BuiltInType.UInt32 when scalar && value.TryGetValue(out uint number):
                    writer.WriteNumberValue(number);
                    return;
                case BuiltInType.Int32 when scalar && value.TryGetValue(out int integer):
                    writer.WriteNumberValue(integer);
                    return;
                case BuiltInType.Int64 when scalar && value.TryGetValue(out long wide):
                    writer.WriteNumberValue(wide);
                    return;
                case BuiltInType.ByteString when scalar && value.TryGetValue(out ByteString bytes):
                    writer.WriteStartObject();
                    writer.WriteString("$bytes", Hex(bytes));
                    writer.WriteEndObject();
                    return;
                case BuiltInType.ExtensionObject when scalar && value.TryGetValue(out ExtensionObject extension):
                    RenderExtension(writer, extension);
                    return;
                case BuiltInType.ExtensionObject when value.TryGetValue(out ArrayOf<ExtensionObject> extensions):
                    writer.WriteStartArray();
                    foreach (ExtensionObject item in extensions)
                    {
                        RenderExtension(writer, item);
                    }
                    writer.WriteEndArray();
                    return;
                default:
                    throw new NotSupportedException("No rendering for " + value.TypeInfo);
            }
        }

        private static void RenderExtension(Utf8JsonWriter writer, ExtensionObject extension)
        {
            if (extension.IsNull)
            {
                writer.WriteNullValue();
            }
            else if (extension.TryGetValue(out IEncodeable? body, Context))
            {
                Render(writer, body);
            }
            else
            {
                throw new NotSupportedException("An ExtensionObject body cannot be decoded.");
            }
        }

        public static string Hex(ByteString bytes)
        {
            var builder = new StringBuilder();
            foreach (byte item in bytes.ToArray())
            {
                builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Encodes a structure in a UA Binary ExtensionObject and decodes it again.
        /// </summary>
        public static IEncodeable BinaryRoundTrip(IEncodeable value)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, Context, true))
            {
                encoder.WriteExtensionObject(null, new ExtensionObject(value));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, Context, true);
            ExtensionObject decoded = decoder.ReadExtensionObject(null);
            return decoded.TryGetValue(out IEncodeable? body, Context) && body is not null
                ? body
                : throw new InvalidOperationException("The binary ExtensionObject cannot be decoded.");
        }

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(SchemaRegistry.Namespaces.SchemaRegistry);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            context.Factory.Builder
                .AddOpcUaXRegistry()
                .AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry()
                .Commit();
            return context;
        }
    }
}
