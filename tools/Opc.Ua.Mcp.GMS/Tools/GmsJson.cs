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
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Encodes typed snapshots with the stack's native value codec and a closed source-generated JSON envelope.
    /// </summary>
    internal sealed partial class GmsJson
    {
        /// <summary>
        /// Captures the connected server's namespace and encodeable-type context.
        /// </summary>
        internal GmsJson(IServiceMessageContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Preserves service and domain failures as distinct MCP errors while propagating caller cancellation.
        /// </summary>
        internal static async Task<CallToolResult> ExecuteAsync(
            Func<Task<CallToolResult>> operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (GmsResultDomainException error)
            {
                return Result(new JsonObject
                {
                    ["error"] = true,
                    ["errorKind"] = "ResultDomain",
                    ["operation"] = error.Operation,
                    ["domainError"] = error.Error,
                    ["message"] = ShortMessage(error.Message)
                }, 1024, isError: true);
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The GMS MCP request was cancelled.", error, cancellationToken);
            }
            catch (ServiceResultException error)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            catch (FormatException error)
            {
                return Error(StatusCodes.BadDecodingError, error.Message);
            }
            catch (JsonException error)
            {
                return Error(StatusCodes.BadDecodingError, error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                return Error(StatusCodes.BadUserAccessDenied, error.Message);
            }
            catch (IOException error)
            {
                return Error(error.InnerException is ServiceResultException service
                    ? service.StatusCode : StatusCodes.BadUnexpectedError, error.Message);
            }
            catch (InvalidOperationException error)
            {
                return Error(StatusCodes.BadInvalidState, error.Message);
            }
            catch (TimeoutException error)
            {
                return Error(StatusCodes.BadTimeout, error.Message);
            }
            catch (AggregateException error)
            {
                return Error(StatusCodes.BadUnexpectedError, error.Message);
            }
        }

        /// <summary>
        /// Bounds the complete UTF-8 payload, never returning a truncated successful response.
        /// </summary>
        internal static CallToolResult Result(JsonObject value, int maximumResponseBytes, bool isError = false)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, GmsJsonContext.Default.JsonObject);
            if (bytes.Length > maximumResponseBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The complete JSON response exceeds maximumResponseBytes; " +
                    "narrow the request or increase its bound.");
            }
            return new CallToolResult
            {
                IsError = isError,
                Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(bytes) }]
            };
        }

        /// <summary>
        /// Preserves a discovered instance's actual vendor subtype rather than substituting the standard base type.
        /// </summary>
        internal static JsonObject Entry(GmsDeviceEntry entry)
        {
            return new JsonObject
            {
                ["nodeId"] = entry.NodeId.ToString(),
                ["typeDefinitionId"] = entry.TypeDefinitionId.ToString(),
                ["browseName"] = entry.BrowseName.ToString(),
                ["displayName"] = entry.DisplayName.Text,
                ["displayLocale"] = entry.DisplayName.Locale
            };
        }

        /// <summary>
        /// Maps closed typed inventories to native JSON arrays without reflection-based Add overloads.
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
        /// Preserves the complete native sample, including its wire type, status and both timestamps.
        /// </summary>
        internal JsonNode? Reading(in DataValue value)
        {
            if (value.IsNull)
            {
                return null;
            }
            using var encoder = new JsonEncoder(m_context);
            encoder.WriteDataValue("DataValue", value);
            return JsonNode.Parse(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Encodes method/event values as Variants without fabricating per-field DataValue quality.
        /// </summary>
        internal JsonNode? Value(in Variant value)
        {
            using var encoder = new JsonEncoder(m_context);
            encoder.WriteVariant("Value", value);
            return JsonNode.Parse(encoder.CloseAndReturnText());
        }

        /// <summary>
        /// Makes modeled-but-unavailable samples distinct from absent optional variables.
        /// </summary>
        internal JsonObject Variable(in GmsVariableReading value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.IsPresent ? value.NodeId.ToString() : null,
                ["present"] = value.IsPresent,
                ["reading"] = value.IsPresent ? Reading(value.Value) : null
            };
        }

        /// <summary>
        /// Preserves optional absence independently for every named raw sample.
        /// </summary>
        private JsonObject Readings(params (string Name, DataValue Value)[] fields)
        {
            var result = new JsonObject();
            foreach ((string name, DataValue value) in fields)
            {
                result[name] = Reading(value);
            }
            return result;
        }

        /// <summary>
        /// Projects each resolved native variable with its own presence and actual NodeId.
        /// </summary>
        private JsonObject Variables(params (string Name, GmsVariableReading Value)[] fields)
        {
            var result = new JsonObject();
            foreach ((string name, GmsVariableReading value) in fields)
            {
                result[name] = Variable(value);
            }
            return result;
        }

        /// <summary>
        /// Bounds diagnostic strings while retaining the exact UA service status.
        /// </summary>
        private static CallToolResult Error(StatusCode code, string message)
        {
            return Result(new JsonObject
            {
                ["error"] = true,
                ["errorKind"] = "Service",
                ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(code),
                ["message"] = ShortMessage(message)
            }, 1024, isError: true);
        }

        /// <summary>
        /// Bounds error text even when the remote server supplied a much longer diagnostic.
        /// </summary>
        private static string ShortMessage(string value) => value.Length <= 128 ? value : value[..128];

        /// <summary>The native server namespace and value encoding context.</summary>
        private readonly IServiceMessageContext m_context;
    }

    /// <summary>
    /// Generates closed JSON metadata for NativeAOT and reflection-disabled serialization.
    /// </summary>
    [JsonSerializable(typeof(JsonObject))]
    internal sealed partial class GmsJsonContext : JsonSerializerContext
    {
    }
}
