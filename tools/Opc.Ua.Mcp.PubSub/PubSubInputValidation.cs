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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Kafka;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Pure validation and structural value conversion. Never resolves DNS or constructs a transport.
    /// </summary>
    internal static class PubSubInputValidation
    {
        /// <summary>
        /// Validates the complete caller input, including dormant branches, before admitting a runtime.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void Validate(PubSubApplicationInput input, McpPubSubOptions options)
        {
            ArgumentNullException.ThrowIfNull(input);
            Text(input.Name, 128);
            Text(input.DataSetName, 128);
            if (input.Mode is not ("Publisher" or "Subscriber"))
            {
                throw new ArgumentException("Mode must be Publisher or Subscriber.");
            }
            ArgumentNullException.ThrowIfNull(input.Connection);
            ValidateConnection(input.Connection);
            _ = Publisher(input.PublisherId, input.Connection.Encoding);
            ArgumentOutOfRangeException.ThrowIfLessThan(
                input.PublishingIntervalMilliseconds, options.MinimumPublishingIntervalMilliseconds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.PublishingIntervalMilliseconds, 60000);
            ArgumentOutOfRangeException.ThrowIfLessThan(input.Fields.Count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Fields.Count, options.MaxFields);
            if (input.DataSetClassId is not null)
            {
                _ = Guid.Parse(input.DataSetClassId);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<Guid>();
            foreach (PubSubFieldInput field in input.Fields)
            {
                ArgumentNullException.ThrowIfNull(field);
                Text(field.Name, 128);
                if (!names.Add(field.Name) ||
                    field.ValueRank is not (ValueRanks.Scalar or 1) ||
                    !SupportedType(field.Type))
                {
                    throw new ArgumentException(
                        "Fields require unique names, supported built-in types and rank -1 or 1.");
                }
                if (field.Id is not null &&
                    (!Guid.TryParse(field.Id, out Guid id) ||
                        !ids.Add(id) ||
                        id == Guid.Empty))
                {
                    throw new ArgumentException("Explicit field identifiers must be nonempty, unique GUIDs.");
                }
                if (field.InitialValue is { } value)
                {
                    _ = Value(field, value, options);
                }
            }
            if (input.Security is { } security)
            {
                Text(security.GroupId, 128);
                Text(security.KeyReference, 256);
                Text(security.Resource, 2048);
                if (input.Connection.Encoding != PubSubMessageEncoding.Uadp ||
                    security.Mode is not (MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt) ||
                    security.PolicyUri is not (
                        PubSubSecurityPolicyUri.PubSubAes128Ctr or PubSubSecurityPolicyUri.PubSubAes256Ctr))
                {
                    throw new ArgumentException(
                        "Message security requires UADP, Sign/SignAndEncrypt and a PubSub AES policy.");
                }
            }
        }

        /// <summary>
        /// Validates concrete protocol targets without executing the transport parser's DNS path.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void ValidateConnection(PubSubConnectionInput connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            Text(connection.Endpoint, 2048);
            if (!Enum.IsDefined(connection.Transport) || !Enum.IsDefined(connection.Encoding))
            {
                throw new ArgumentException("Unknown PubSub transport or encoding.");
            }
            if (connection.Transport == PubSubTransportKind.Ethernet)
            {
                _ = EthEndpointParser.Parse(connection.Endpoint);
                Text(connection.NetworkInterface, 256);
                if (connection.Encoding != PubSubMessageEncoding.Uadp)
                {
                    throw new ArgumentException("The existing Ethernet backend transports UADP only.");
                }
            }
            else
            {
                var endpoint = new Uri(connection.Endpoint, UriKind.Absolute);
                string scheme = connection.Transport switch
                {
                    PubSubTransportKind.Udp => "opc.udp",
                    PubSubTransportKind.Dtls => "opc.dtls",
                    PubSubTransportKind.Mqtt => endpoint.Scheme == "mqtts" ? "mqtts" : "mqtt",
                    PubSubTransportKind.Kafka => endpoint.Scheme == "kafkas" ? "kafkas" : "kafka",
                    _ => throw new ArgumentException("Unknown transport.")
                };
                if (endpoint.Scheme != scheme ||
                    endpoint.UserInfo.Length != 0 ||
                    endpoint.Query.Length != 0 ||
                    endpoint.Fragment.Length != 0 ||
                    (endpoint.AbsolutePath.Length != 0 && endpoint.AbsolutePath != "/"))
                {
                    throw new ArgumentException(
                        "The transport endpoint must have the selected scheme and no credentials/path.");
                }
                if (connection.Transport is PubSubTransportKind.Udp or PubSubTransportKind.Dtls)
                {
                    Text(connection.NetworkInterface, 256);
                    if (!IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out IPAddress? address) ||
                        address.Equals(IPAddress.Any) ||
                        address.Equals(IPAddress.IPv6Any) ||
                        endpoint.Port is < 1 or > 65535 ||
                        connection.Encoding != PubSubMessageEncoding.Uadp)
                    {
                        throw new ArgumentException(
                            "Datagrams require an IP literal, explicit port, interface and UADP.");
                    }
                }
            }
            bool broker = connection.Transport is PubSubTransportKind.Mqtt or PubSubTransportKind.Kafka;
            if (broker)
            {
                Topic(connection.Topic);
                Topic(connection.MetadataTopic);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(connection.AuxiliaryTopics.Count, 16);
                foreach (string topic in connection.AuxiliaryTopics)
                {
                    Topic(topic);
                }
            }
            else if (connection.Topic is not null ||
                connection.MetadataTopic is not null ||
                connection.AuxiliaryTopics.Count != 0)
            {
                throw new ArgumentException("Datagram and Ethernet targets do not have broker topics.");
            }
            if (connection.CredentialReference is not null)
            {
                Text(connection.CredentialReference, 256);
                Text(connection.UserName, 256);
                if (connection.Transport != PubSubTransportKind.Mqtt ||
                    !connection.Endpoint.StartsWith("mqtts://", StringComparison.Ordinal))
                {
                    throw new ArgumentException("MQTT credentials require TLS and a host-authorized secret reference.");
                }
            }
            else if (connection.UserName is not null)
            {
                throw new ArgumentException("A user name requires a protected credential reference.");
            }
        }

        /// <summary>
        /// Gets the existing profile corresponding to the selected native encoding.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static string Profile(PubSubConnectionInput connection)
        {
            return connection.Transport switch
            {
                PubSubTransportKind.Udp or PubSubTransportKind.Dtls => Profiles.PubSubUdpUadpTransport,
                PubSubTransportKind.Ethernet => EthProfiles.PubSubEthUadpTransport,
                PubSubTransportKind.Mqtt => connection.Encoding == PubSubMessageEncoding.Json
                    ? Profiles.PubSubMqttJsonTransport : Profiles.PubSubMqttUadpTransport,
                PubSubTransportKind.Kafka => connection.Encoding == PubSubMessageEncoding.Json
                    ? KafkaProfiles.PubSubKafkaJsonTransport : KafkaProfiles.PubSubKafkaUadpTransport,
                _ => throw new ArgumentException("Unknown PubSub transport.")
            };
        }

        /// <summary>
        /// Converts the exact PublisherId discriminator, rejecting lossy conversions.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static PublisherId Publisher(PubSubPublisherIdInput input, PubSubMessageEncoding encoding)
        {
            ArgumentNullException.ThrowIfNull(input);
            JsonElement value = input.Value;
            return input.Type switch
            {
                PublisherIdType.Byte => PublisherId.FromByte(value.GetByte()),
                PublisherIdType.UInt16 => PublisherId.FromUInt16(value.GetUInt16()),
                PublisherIdType.UInt32 => PublisherId.FromUInt32(value.GetUInt32()),
                PublisherIdType.UInt64 => PublisherId.FromUInt64(value.ValueKind == JsonValueKind.String
                    ? ulong.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
                    : value.GetUInt64()),
                PublisherIdType.String => PublisherId.FromString(Text(value.GetString(), 256)),
                PublisherIdType.Guid when encoding == PubSubMessageEncoding.Json =>
                    PublisherId.FromGuid(value.GetGuid()),
                _ => throw new ArgumentException("Invalid PublisherId discriminator for the selected encoding.")
            };
        }

        /// <summary>
        /// Converts a value using its declared metadata, preserving numeric widths and array element types.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal static Variant Value(PubSubFieldInput field, JsonElement value, McpPubSubOptions options)
        {
            if (Encoding.UTF8.GetByteCount(value.GetRawText()) > options.MaxMessageBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The field value is oversized.");
            }
            if (field.ValueRank == 1)
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > options.MaxArrayElements)
                {
                    throw new ArgumentException("The field requires a bounded one-dimensional array.");
                }
                return field.Type switch
                {
                    BuiltInType.Boolean => Variant.From(Array(value, element => element.GetBoolean())),
                    BuiltInType.SByte => Variant.From(Array(value, element => element.GetSByte())),
                    BuiltInType.Byte => Variant.From(Array(value, element => element.GetByte())),
                    BuiltInType.Int16 => Variant.From(Array(value, element => element.GetInt16())),
                    BuiltInType.UInt16 => Variant.From(Array(value, element => element.GetUInt16())),
                    BuiltInType.Int32 => Variant.From(Array(value, element => element.GetInt32())),
                    BuiltInType.UInt32 => Variant.From(Array(value, element => element.GetUInt32())),
                    BuiltInType.Int64 => Variant.From(Array(value, Int64)),
                    BuiltInType.UInt64 => Variant.From(Array(value, UInt64)),
                    BuiltInType.Float => Variant.From(Array(value, element => Finite(element.GetSingle()))),
                    BuiltInType.Double => Variant.From(Array(value, element => Finite(element.GetDouble()))),
                    BuiltInType.String => Variant.From(Array(value, element => element.GetString() ?? string.Empty)),
                    BuiltInType.DateTime => Variant.From(Array(value, Timestamp)),
                    BuiltInType.Guid => Variant.From(Array(value, element => new Uuid(element.GetGuid()))),
                    BuiltInType.ByteString => Variant.From(Array(value,
                        element => ByteString.From(element.GetBytesFromBase64()))),
                    _ => throw new ArgumentException("Unsupported array element type.")
                };
            }
            if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined)
            {
                throw new ArgumentException("The declared field requires a scalar JSON value.");
            }
            return field.Type switch
            {
                BuiltInType.Boolean => Variant.From(value.GetBoolean()),
                BuiltInType.SByte => Variant.From(value.GetSByte()),
                BuiltInType.Byte => Variant.From(value.GetByte()),
                BuiltInType.Int16 => Variant.From(value.GetInt16()),
                BuiltInType.UInt16 => Variant.From(value.GetUInt16()),
                BuiltInType.Int32 => Variant.From(value.GetInt32()),
                BuiltInType.UInt32 => Variant.From(value.GetUInt32()),
                BuiltInType.Int64 => Variant.From(Int64(value)),
                BuiltInType.UInt64 => Variant.From(UInt64(value)),
                BuiltInType.Float => Variant.From(Finite(value.GetSingle())),
                BuiltInType.Double => Variant.From(Finite(value.GetDouble())),
                BuiltInType.String => value.ValueKind == JsonValueKind.Null
                    ? Variant.Null : Variant.From(value.GetString()!),
                BuiltInType.DateTime => Variant.From(Timestamp(value)),
                BuiltInType.Guid => Variant.From(new Uuid(value.GetGuid())),
                BuiltInType.ByteString => Variant.From(value.ValueKind == JsonValueKind.Null
                    ? default : ByteString.From(value.GetBytesFromBase64())),
                _ => throw new ArgumentException("Unsupported field type.")
            };
        }

        /// <summary>
        /// Creates a typed default without strings masquerading as values.
        /// </summary>
        internal static Variant DefaultValue(PubSubFieldInput field, McpPubSubOptions options)
        {
            string json = field.ValueRank == 1 ? "[]" : field.Type switch
            {
                BuiltInType.Boolean => "false",
                BuiltInType.String or BuiltInType.ByteString => "\"\"",
                BuiltInType.DateTime => "\"1601-01-01T00:00:00Z\"",
                BuiltInType.Guid => "\"00000000-0000-0000-0000-000000000000\"",
                _ => "0"
            };
            using var document = JsonDocument.Parse(json);
            return Value(field, document.RootElement, options);
        }

        /// <summary>
        /// Validates bounded text and refuses embedded control characters.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static string Text(string? value, int maximum = 2048)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximum);
            if (value.Any(char.IsControl))
            {
                throw new ArgumentException("Control characters are not permitted.");
            }
            return value;
        }

        /// <summary>
        /// Validates an exact topic, never a subscription wildcard or shared-subscription escape.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void Topic(string? topic)
        {
            Text(topic, 512);
            if (topic!.Contains('#', StringComparison.Ordinal) ||
                topic.Contains('+', StringComparison.Ordinal) ||
                topic.StartsWith("$share/", StringComparison.Ordinal))
            {
                throw new ArgumentException("PubSub MCP broker topics must be exact, not wildcard subscriptions.");
            }
        }

        /// <summary>
        /// Identifies supported scalar built-ins without accepting extension-object dispatch.
        /// </summary>
        private static bool SupportedType(BuiltInType type)
        {
            return type is >= BuiltInType.Boolean and <= BuiltInType.ByteString;
        }

        /// <summary>
        /// Materializes a previously bounded array with a concrete element type.
        /// </summary>
        /// <typeparam name="T">The concrete array element type.</typeparam>
        private static ArrayOf<T> Array<T>(JsonElement value, Func<JsonElement, T> convert)
        {
            return new ArrayOf<T>(value.EnumerateArray().Select(convert).ToArray());
        }

        /// <summary>
        /// Parses lossless signed 64-bit values.
        /// </summary>
        private static long Int64(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String
                ? long.Parse(value.GetString()!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                : value.GetInt64();
        }

        /// <summary>
        /// Parses lossless unsigned 64-bit values.
        /// </summary>
        private static ulong UInt64(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String
                ? ulong.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
                : value.GetUInt64();
        }

        /// <summary>
        /// Requires a UTC or explicit-offset timestamp instead of assuming the host timezone.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static DateTimeUtc Timestamp(JsonElement value)
        {
            string text = Text(value.GetString(), 64);
            int separator = text.IndexOf('T', StringComparison.Ordinal);
            if (separator < 0 ||
                (!text.EndsWith('Z') &&
                    text.IndexOf('+', separator) < 0 &&
                    text.IndexOf('-', separator) < 0))
            {
                throw new ArgumentException("DateTime values require Z or an explicit UTC offset.");
            }
            return DateTimeUtc.From(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None));
        }

        /// <summary>
        /// Refuses nonfinite or overflowing JSON single-precision numbers.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static float Finite(float value)
        {
            return float.IsFinite(value) ? value : throw new ArgumentException("A finite numeric value is required.");
        }

        /// <summary>
        /// Refuses nonfinite or overflowing JSON double-precision numbers.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static double Finite(double value)
        {
            return double.IsFinite(value) ? value : throw new ArgumentException("A finite numeric value is required.");
        }
    }
}
