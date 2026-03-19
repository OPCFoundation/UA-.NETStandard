/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    public class SubscriptionStore : ISubscriptionStore
    {
        private static readonly JsonSerializerOptions s_settings = new()
        {
            Converters =
            {
                new ExtensionObjectConverter(),
                new NumericRangeConverter(),
                new StoredMonitoredItemConverter(),
                new DateTimeUtcConverter(),
                new ArrayOfConverterFactory(),
                new NodeIdConverter(),
                new ExpandedNodeIdConverter(),
                new QualifiedNameConverter(),
                new StatusCodeConverter(),
                new VariantConverter(),
                new ServiceResultConverter(),
                new UserIdentityTokenConverter()
            },
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions");

        private const string kFilename = "subscriptionsStore.txt";
        private readonly DurableMonitoredItemQueueFactory m_durableMonitoredItemQueueFactory;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;

        public SubscriptionStore(IServerInternal server)
        {
            m_logger = server.Telemetry.CreateLogger<SubscriptionStore>();
            m_telemetry = server.Telemetry;
            m_durableMonitoredItemQueueFactory = server
                .MonitoredItemQueueFactory as DurableMonitoredItemQueueFactory;
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json is used with known subscription types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json is used with known subscription types.")]
        public bool StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions)
        {
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
                string result = JsonSerializer.Serialize(
                    subscriptions.Cast<StoredSubscription>().ToList(),
                    s_settings);

                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                File.WriteAllText(Path.Combine(s_storage_path, kFilename), result);

                if (m_durableMonitoredItemQueueFactory != null)
                {
                    IEnumerable<uint> ids = subscriptions.SelectMany(
                        s => s.MonitoredItems.Select(m => m.Id));
                    m_durableMonitoredItemQueueFactory.PersistQueues(ids, s_storage_path);
                }
                return true;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to store subscriptions");
            }
            return false;
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json is used with known subscription types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json is used with known subscription types.")]
        public RestoreSubscriptionResult RestoreSubscriptions()
        {
            string filePath = Path.Combine(s_storage_path, kFilename);
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
                    List<StoredSubscription> storedSubscriptions =
                        JsonSerializer.Deserialize<List<StoredSubscription>>(json, s_settings);

                    File.Delete(filePath);

                    List<IStoredSubscription> result =
                        storedSubscriptions?.Cast<IStoredSubscription>().ToList();
                    return new RestoreSubscriptionResult(true, result);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to restore subscriptions");
            }

            return new RestoreSubscriptionResult(false, null);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        public class ExtensionObjectConverter : JsonConverter<ExtensionObject>
        {
            public override ExtensionObject Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                JsonElement root = doc.RootElement;

                ExpandedNodeId typeId = JsonSerializer.Deserialize<ExpandedNodeId>(
                    root.GetProperty("TypeId").GetRawText(), options);
                string body = root.GetProperty("Body").GetRawText();
                return new ExtensionObject(typeId, body);
            }

            public override void Write(
                Utf8JsonWriter writer,
                ExtensionObject value,
                JsonSerializerOptions options)
            {
                writer.WriteStartObject();
#pragma warning disable CS0618 // Type or member is obsolete
                writer.WritePropertyName("Body");
                JsonSerializer.Serialize(writer, value.Body, options);
                writer.WritePropertyName("TypeId");
                JsonSerializer.Serialize(writer, value.TypeId, options);
#pragma warning restore CS0618 // Type or member is obsolete
                writer.WriteEndObject();
            }
        }

        public class NumericRangeConverter : JsonConverter<NumericRange>
        {
            public override NumericRange Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                JsonElement root = doc.RootElement;

                int begin = root.GetProperty("Begin").GetInt32();
                int end = root.GetProperty("End").GetInt32();

                if (begin == -1)
                {
                    return NumericRange.Null;
                }

                return new NumericRange(begin, end);
            }

            public override void Write(
                Utf8JsonWriter writer,
                NumericRange value,
                JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("Begin", value.Begin);
                writer.WriteNumber("End", value.End);
                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// Converter that maps the <see cref="IStoredMonitoredItem"/> interface to its
        /// concrete <see cref="StoredMonitoredItem"/> implementation for deserialization.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        public class StoredMonitoredItemConverter : JsonConverter<IStoredMonitoredItem>
        {
            public override IStoredMonitoredItem Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return JsonSerializer.Deserialize<StoredMonitoredItem>(ref reader, options);
            }

            public override void Write(
                Utf8JsonWriter writer,
                IStoredMonitoredItem value,
                JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, (StoredMonitoredItem)value, options);
            }
        }

        /// <summary>
        /// Converter for <see cref="DateTimeUtc"/> which is a struct with a
        /// <c>NowIfDefault</c> property returning <see cref="DateTimeUtc"/>,
        /// causing infinite recursion in default STJ serialization.
        /// </summary>
        public class DateTimeUtcConverter : JsonConverter<DateTimeUtc>
        {
            public override DateTimeUtc Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                long ticks = reader.GetInt64();
                return new DateTimeUtc(ticks);
            }

            public override void Write(
                Utf8JsonWriter writer,
                DateTimeUtc value,
                JsonSerializerOptions options)
            {
                writer.WriteNumberValue(value.Value);
            }
        }

        /// <summary>
        /// Factory for creating <see cref="ArrayOfConverter{T}"/> instances.
        /// <see cref="ArrayOf{T}"/> has a <c>[JsonConstructor]</c> with a parameter name
        /// that does not match its property name, preventing default STJ deserialization.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json converter factory for known OPC UA ArrayOf<T>.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json converter factory for known OPC UA ArrayOf<T>.")]
        public class ArrayOfConverterFactory : JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert)
            {
                return typeToConvert.IsGenericType &&
                    typeToConvert.GetGenericTypeDefinition() == typeof(ArrayOf<>);
            }

            public override JsonConverter CreateConverter(
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                Type elementType = typeToConvert.GetGenericArguments()[0];
                Type converterType = typeof(ArrayOfConverter<>).MakeGenericType(elementType);
                return (JsonConverter)Activator.CreateInstance(converterType);
            }
        }

        /// <summary>
        /// Converter for <see cref="ArrayOf{T}"/> that serializes as a JSON array.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        public class ArrayOfConverter<T> : JsonConverter<ArrayOf<T>>
        {
            public override ArrayOf<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                T[] array = JsonSerializer.Deserialize<T[]>(ref reader, options);
                return array == null ? ArrayOf<T>.Null : new ArrayOf<T>(array.AsMemory());
            }

            public override void Write(
                Utf8JsonWriter writer,
                ArrayOf<T> value,
                JsonSerializerOptions options)
            {
                if (value.IsNull)
                {
                    writer.WriteNullValue();
                    return;
                }

                JsonSerializer.Serialize(writer, value.ToArray(), options);
            }
        }

        public class NodeIdConverter : JsonConverter<NodeId>
        {
            public override NodeId Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                string text = reader.GetString();
                return text == null ? NodeId.Null : NodeId.Parse(text);
            }

            public override void Write(
                Utf8JsonWriter writer,
                NodeId value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.IsNull ? null : value.ToString());
            }
        }

        public class ExpandedNodeIdConverter : JsonConverter<ExpandedNodeId>
        {
            public override ExpandedNodeId Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                string text = reader.GetString();
                return text == null ? ExpandedNodeId.Null : ExpandedNodeId.Parse(text);
            }

            public override void Write(
                Utf8JsonWriter writer,
                ExpandedNodeId value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.IsNull ? null : value.ToString());
            }
        }

        public class QualifiedNameConverter : JsonConverter<QualifiedName>
        {
            public override QualifiedName Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                string text = reader.GetString();
                return text == null ? QualifiedName.Null : QualifiedName.Parse(text);
            }

            public override void Write(
                Utf8JsonWriter writer,
                QualifiedName value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.IsNull ? null : value.ToString());
            }
        }

        public class StatusCodeConverter : JsonConverter<StatusCode>
        {
            public override StatusCode Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return new StatusCode(reader.GetUInt32());
            }

            public override void Write(
                Utf8JsonWriter writer,
                StatusCode value,
                JsonSerializerOptions options)
            {
                writer.WriteNumberValue(value.Code);
            }
        }

        /// <summary>
        /// Converter for <see cref="Opc.Ua.Variant"/> that stores the raw value
        /// as a number (if numeric) or a string representation.
        /// Since <see cref="Opc.Ua.Variant"/> can hold any OPC UA value, the converter
        /// stores the built-in type alongside the value for round-tripping.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        public class VariantConverter : JsonConverter<Variant>
        {
            public override Variant Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return Variant.Null;
                }

                using var doc = JsonDocument.ParseValue(ref reader);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("Type", out JsonElement typeElement))
                {
                    return Variant.Null;
                }

                int builtInType = typeElement.GetInt32();
                if (!root.TryGetProperty("Value", out JsonElement valueElement))
                {
                    return Variant.Null;
                }

                return (BuiltInType)builtInType switch
                {
                    BuiltInType.Boolean => new Variant(valueElement.GetBoolean()),
                    BuiltInType.SByte => new Variant((sbyte)valueElement.GetInt32()),
                    BuiltInType.Byte => new Variant(valueElement.GetByte()),
                    BuiltInType.Int16 => new Variant(valueElement.GetInt16()),
                    BuiltInType.UInt16 => new Variant(valueElement.GetUInt16()),
                    BuiltInType.Int32 => new Variant(valueElement.GetInt32()),
                    BuiltInType.UInt32 => new Variant(valueElement.GetUInt32()),
                    BuiltInType.Int64 => new Variant(valueElement.GetInt64()),
                    BuiltInType.UInt64 => new Variant(valueElement.GetUInt64()),
                    BuiltInType.Float => new Variant(valueElement.GetSingle()),
                    BuiltInType.Double => new Variant(valueElement.GetDouble()),
                    BuiltInType.String => new Variant(valueElement.GetString()),
                    BuiltInType.DateTime =>
                        new Variant(new DateTimeUtc(valueElement.GetInt64())),
                    BuiltInType.StatusCode =>
                        new Variant(new StatusCode(valueElement.GetUInt32())),
                    BuiltInType.NodeId =>
                        new Variant(NodeId.Parse(valueElement.GetString())),
                    BuiltInType.ExpandedNodeId =>
                        new Variant(ExpandedNodeId.Parse(valueElement.GetString())),
                    BuiltInType.QualifiedName =>
                        new Variant(QualifiedName.Parse(valueElement.GetString())),
                    BuiltInType.LocalizedText =>
                        new Variant(new LocalizedText(valueElement.GetString())),
                    BuiltInType.ByteString =>
                        new Variant(valueElement.GetBytesFromBase64()),
                    _ => Variant.Null
                };
            }

            public override void Write(
                Utf8JsonWriter writer,
                Variant value,
                JsonSerializerOptions options)
            {
                if (value.IsNull)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartObject();
                writer.WriteNumber("Type", (int)value.TypeInfo.BuiltInType);
                writer.WritePropertyName("Value");

                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        writer.WriteBooleanValue((bool)value);
                        break;
                    case BuiltInType.SByte:
                        writer.WriteNumberValue((sbyte)value);
                        break;
                    case BuiltInType.Byte:
                        writer.WriteNumberValue((byte)value);
                        break;
                    case BuiltInType.Int16:
                        writer.WriteNumberValue((short)value);
                        break;
                    case BuiltInType.UInt16:
                        writer.WriteNumberValue((ushort)value);
                        break;
                    case BuiltInType.Int32:
                        writer.WriteNumberValue((int)value);
                        break;
                    case BuiltInType.UInt32:
                        writer.WriteNumberValue((uint)value);
                        break;
                    case BuiltInType.Int64:
                        writer.WriteNumberValue((long)value);
                        break;
                    case BuiltInType.UInt64:
                        writer.WriteNumberValue((ulong)value);
                        break;
                    case BuiltInType.Float:
                        writer.WriteNumberValue((float)value);
                        break;
                    case BuiltInType.Double:
                        writer.WriteNumberValue((double)value);
                        break;
                    case BuiltInType.String:
                        writer.WriteStringValue((string)value);
                        break;
                    case BuiltInType.DateTime:
                        writer.WriteNumberValue(((DateTimeUtc)value).Value);
                        break;
                    case BuiltInType.StatusCode:
                        writer.WriteNumberValue(((StatusCode)value).Code);
                        break;
                    case BuiltInType.NodeId:
                        writer.WriteStringValue(((NodeId)value).ToString());
                        break;
                    case BuiltInType.ExpandedNodeId:
                        writer.WriteStringValue(((ExpandedNodeId)value).ToString());
                        break;
                    case BuiltInType.QualifiedName:
                        writer.WriteStringValue(((QualifiedName)value).ToString());
                        break;
                    case BuiltInType.LocalizedText:
                        writer.WriteStringValue(((LocalizedText)value).Text);
                        break;
                    case BuiltInType.ByteString:
                        writer.WriteBase64StringValue(
                            (byte[])value.AsBoxedObject());
                        break;
                    default:
                        writer.WriteStringValue(value.ToString());
                        break;
                }

                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// Converter for <see cref="ServiceResult"/> that serializes as a JSON object
        /// with StatusCode and optional AdditionalInfo.
        /// </summary>
        public class ServiceResultConverter : JsonConverter<ServiceResult>
        {
            public override ServiceResult Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                using var doc = JsonDocument.ParseValue(ref reader);
                JsonElement root = doc.RootElement;

                uint code = root.GetProperty("StatusCode").GetUInt32();
                return new ServiceResult(new StatusCode(code));
            }

            public override void Write(
                Utf8JsonWriter writer,
                ServiceResult value,
                JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartObject();
                writer.WriteNumber("StatusCode", value.StatusCode.Code);
                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// Polymorphic converter for <see cref="UserIdentityToken"/> that preserves the
        /// concrete token type (anonymous, username, x509, issued) across serialization.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json converter for known OPC UA types.")]
        public class UserIdentityTokenConverter : JsonConverter<UserIdentityToken>
        {
            private const string TypeDiscriminator = "$tokenType";

            public override UserIdentityToken Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                using var doc = JsonDocument.ParseValue(ref reader);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty(TypeDiscriminator, out JsonElement typeEl))
                {
                    return new AnonymousIdentityToken();
                }

                string typeName = typeEl.GetString();
                string policyId = root.TryGetProperty("PolicyId", out JsonElement p)
                    ? p.GetString() : null;

                switch (typeName)
                {
                    case "UserName":
                        var userToken = new UserNameIdentityToken { PolicyId = policyId };
                        if (root.TryGetProperty("UserName", out JsonElement u))
                        {
                            userToken.UserName = u.GetString();
                        }
                        if (root.TryGetProperty("Password", out JsonElement pw) &&
                            pw.ValueKind != JsonValueKind.Null)
                        {
                            userToken.Password = new ByteString(pw.GetBytesFromBase64());
                        }
                        if (root.TryGetProperty("EncryptionAlgorithm", out JsonElement ea))
                        {
                            userToken.EncryptionAlgorithm = ea.GetString();
                        }
                        return userToken;

                    case "X509":
                        var x509Token = new X509IdentityToken { PolicyId = policyId };
                        if (root.TryGetProperty("CertificateData", out JsonElement cd) &&
                            cd.ValueKind != JsonValueKind.Null)
                        {
                            x509Token.CertificateData = new ByteString(cd.GetBytesFromBase64());
                        }
                        return x509Token;

                    case "Issued":
                        var issuedToken = new IssuedIdentityToken { PolicyId = policyId };
                        if (root.TryGetProperty("TokenData", out JsonElement td) &&
                            td.ValueKind != JsonValueKind.Null)
                        {
                            issuedToken.TokenData = new ByteString(td.GetBytesFromBase64());
                        }
                        if (root.TryGetProperty("EncryptionAlgorithm", out JsonElement ea2))
                        {
                            issuedToken.EncryptionAlgorithm = ea2.GetString();
                        }
                        return issuedToken;

                    default:
                        return new AnonymousIdentityToken { PolicyId = policyId };
                }
            }

            public override void Write(
                Utf8JsonWriter writer,
                UserIdentityToken value,
                JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartObject();
                writer.WriteString("PolicyId", value.PolicyId);

                switch (value)
                {
                    case UserNameIdentityToken u:
                        writer.WriteString(TypeDiscriminator, "UserName");
                        writer.WriteString("UserName", u.UserName);
                        if (!u.Password.IsNull)
                        {
                            writer.WriteBase64String(
                                "Password", u.Password.Span);
                        }
                        writer.WriteString("EncryptionAlgorithm", u.EncryptionAlgorithm);
                        break;

                    case X509IdentityToken x:
                        writer.WriteString(TypeDiscriminator, "X509");
                        if (!x.CertificateData.IsNull)
                        {
                            writer.WriteBase64String(
                                "CertificateData", x.CertificateData.Span);
                        }
                        break;

                    case IssuedIdentityToken i:
                        writer.WriteString(TypeDiscriminator, "Issued");
                        if (!i.TokenData.IsNull)
                        {
                            writer.WriteBase64String(
                                "TokenData", i.TokenData.Span);
                        }
                        writer.WriteString("EncryptionAlgorithm", i.EncryptionAlgorithm);
                        break;

                    default:
                        writer.WriteString(TypeDiscriminator, "Anonymous");
                        break;
                }

                writer.WriteEndObject();
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json is used with known queue types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json is used with known queue types.")]
        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(
            uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreDataChangeQueue(
                monitoredItemId,
                s_storage_path);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "System.Text.Json is used with known queue types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "System.Text.Json is used with known queue types.")]
        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreEventQueue(
                monitoredItemId,
                s_storage_path);
        }

        public void OnSubscriptionRestoreComplete(Dictionary<uint, ArrayOf<uint>> createdSubscriptions)
        {
            string filePath = Path.Combine(s_storage_path, kFilename);

            // remove old file
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to cleanup files for stored subscsription");
                }
            }
            //remove old batches & queues
            if (m_durableMonitoredItemQueueFactory != null)
            {
                IEnumerable<uint> ids = createdSubscriptions.SelectMany(s => s.Value.ToArray());
                m_durableMonitoredItemQueueFactory.CleanStoredQueues(s_storage_path, ids);
            }
        }
    }
}

