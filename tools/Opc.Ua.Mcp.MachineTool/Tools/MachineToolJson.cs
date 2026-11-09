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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Preserves native UA values inside a closed AOT-safe MCP envelope with exact serialized byte bounds.
    /// </summary>
    internal static partial class MachineToolJson
    {
        /// <summary>
        /// Maps expected input and UA failures to errors, never to a successful partial result.
        /// </summary>
        internal static async Task<CallToolResult> ExecuteAsync(
            Func<Task<CallToolResult>> operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (ServiceResultException error)
            {
                return Error(error.StatusCode, error.Message);
            }
            catch (ArgumentOutOfRangeException error)
            {
                return Error(StatusCodes.BadOutOfRange, error.Message);
            }
            catch (ArgumentException error)
            {
                return Error(StatusCodes.BadInvalidArgument, error.Message);
            }
            catch (JsonException error)
            {
                return Error(StatusCodes.BadDecodingError, error.Message);
            }
            catch (FormatException error)
            {
                return Error(StatusCodes.BadDecodingError, error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                return Error(StatusCodes.BadUserAccessDenied, error.Message);
            }
            catch (IOException error)
            {
                return Error(error.InnerException is ServiceResultException service ? service.StatusCode :
                    error is FileNotFoundException or DirectoryNotFoundException ? StatusCodes.BadNotFound :
                    StatusCodes.BadUnexpectedError, error.Message);
            }
            catch (InvalidOperationException error)
            {
                return Error(StatusCodes.BadInvalidState, error.Message);
            }
            catch (TimeoutException)
            {
                return Error(StatusCodes.BadTimeout, "Timed out; the request is not known to have succeeded. " +
                    "No abort, retry or compensating device operation was sent.");
            }
        }

        /// <summary>
        /// Enforces the complete encoded size, with no truncation, loss of quality or hidden collection tails.
        /// </summary>
        internal static CallToolResult Result(JsonObject value, int maximumResponseBytes, bool isError = false)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, MachineToolJsonContext.Default.JsonObject);
            if (bytes.Length > maximumResponseBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The complete JSON response exceeds maximumResponseBytes; request a smaller scope or range.");
            }
            return new CallToolResult
            {
                IsError = isError,
                Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(bytes) }]
            };
        }

        /// <summary>
        /// Preserves actual instance and vendor subtype identities from typed discovery.
        /// </summary>
        internal static JsonObject Entry(MachineToolDeviceEntry value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = value.BrowseName.ToString(),
                ["displayName"] = value.DisplayName.Text,
                ["displayLocale"] = value.DisplayName.Locale,
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString(),
                ["kind"] = value.Kind.ToString()
            };
        }

        /// <summary>
        /// Encodes the original DataValue, including Bad/Uncertain quality and both timestamps.
        /// </summary>
        internal static JsonNode? Reading(MachineToolClient client, in DataValue value)
        {
            if (value.IsNull)
            {
                return null;
            }
            using var encoder = new JsonEncoder(client.Session.MessageContext);
            encoder.WriteDataValue("DataValue", value);
            return JsonNode.Parse(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Maps closed, strongly typed inventories without serializing ArrayOf implementation properties.
        /// </summary>
        /// <typeparam name="T">The known element contract.</typeparam>
        internal static JsonArray Array<T>(ArrayOf<T> values, Func<T, JsonNode?> convert)
        {
            var result = new JsonArray();
            foreach (T value in values)
            {
                result.Add(convert(value));
            }
            return result;
        }

        /// <summary>
        /// Encodes named samples without flattening missing optional samples to Good values.
        /// </summary>
        internal static JsonObject Readings(
            MachineToolClient client, params (string Name, DataValue Value)[] values)
        {
            var result = new JsonObject();
            foreach ((string name, DataValue value) in values)
            {
                result[name] = Reading(client, value);
            }
            return result;
        }

        /// <summary>
        /// Returns a bounded diagnostic while retaining the exact UA status.
        /// </summary>
        private static CallToolResult Error(StatusCode status, string message)
        {
            return Result(new JsonObject
            {
                ["error"] = true,
                ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(status),
                ["message"] = message.Length <= 128 ? message : message[..128],
                ["messageTruncated"] = message.Length > 128
            }, 1024, isError: true);
        }
    }

    /// <summary>
    /// Generates serialization metadata for the closed JSON envelope, not reflection-based UA value codecs.
    /// </summary>
    [JsonSerializable(typeof(JsonObject))]
    internal sealed partial class MachineToolJsonContext : JsonSerializerContext
    {
    }
}
