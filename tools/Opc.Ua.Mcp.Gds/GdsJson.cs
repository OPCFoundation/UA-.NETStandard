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
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Explicit safe projections for GDS results; raw method outputs and diagnostics are never serialized.
    /// </summary>
    internal static class GdsJson
    {
        /// <summary>
        /// Preserves actual operation and argument status codes, including non-default Good statuses.
        /// </summary>
        internal static JsonObject Method(CallMethodResult call)
        {
            var arguments = new JsonArray();
            for (int index = 0; index < Math.Min(call.InputArgumentResults.Count, 16); index++)
            {
                JsonNode status = JsonValue.Create(call.InputArgumentResults[index].Code);
                arguments.Add(status);
            }
            return new JsonObject
            {
                ["error"] = StatusCode.IsBad(call.StatusCode) || call.InputArgumentResults.Count > 16,
                ["statusCode"] = call.StatusCode.ToString(),
                ["statusCodeValue"] = call.StatusCode.Code,
                ["inputArgumentResults"] = arguments,
                ["inputArgumentResultsTruncated"] = call.InputArgumentResults.Count > 16,
                ["methodAcknowledged"] = true
            };
        }

        /// <summary>
        /// Preserves acknowledged outcomes, identifiers and protected references when optional details exceed
        /// the response ceiling. No continuation implies that a mutation should be replayed for omitted details.
        /// </summary>
        internal static JsonObject Compact(JsonObject source)
        {
            var result = new JsonObject();
            foreach ((string name, JsonNode? value) in source)
            {
                if (value is JsonValue scalar)
                {
                    if (!scalar.TryGetValue(out string? text) || text.Length <= 2048)
                    {
                        result[name] = scalar.DeepClone();
                    }
                }
                else if (value is JsonObject child &&
                    child.TryGetPropertyValue("reference", out JsonNode? reference))
                {
                    result[name] = new JsonObject
                    {
                        ["reference"] = reference?.DeepClone(),
                        ["metadataOmitted"] = true
                    };
                }
                else if (value is JsonObject failure && failure.ContainsKey("localPersistence"))
                {
                    result[name] = failure.DeepClone();
                }
            }
            result["error"] = true;
            result["detailsOmitted"] = true;
            result["message"] = "Detailed output exceeded the configured response budget. " +
                "Acknowledged outcomes and protected references are retained; do not repeat a mutation for details.";
            return result;
        }

        /// <summary>
        /// Creates a fixed, secret-free failure description.
        /// </summary>
        internal static JsonObject Error(StatusCode status, string message)
        {
            return new JsonObject
            {
                ["error"] = true,
                ["statusCode"] = status.ToString(),
                ["statusCodeValue"] = status.Code,
                ["message"] = message
            };
        }

        /// <summary>
        /// Reports failed persistence without suggesting that remote issuance was rolled back.
        /// </summary>
        internal static JsonObject PersistenceFailure(IMcpSecretPersistenceFailure failure)
        {
            JsonObject result = Error(failure.ProviderStatus,
                "Remote issuance succeeded or may have committed; local persistence failed. Do not retry blindly.");
            result["localPersistence"] = "failed";
            result["cleanupRequired"] = failure.CleanupFailed;
            result["cleanupReference"] = failure.CleanupFailed ? failure.Reference.Reference : null;
            return result;
        }

        /// <summary>
        /// Includes only safe metadata about a protected result.
        /// </summary>
        internal static JsonObject Reference(McpSecretReference reference)
        {
            return new JsonObject
            {
                ["reference"] = reference.Reference,
                ["purpose"] = reference.Purpose,
                ["resource"] = reference.Resource,
                ["referenceExpiresAt"] = reference.ExpiresAt,
                ["credentialId"] = reference.CredentialId,
                ["issuer"] = reference.Issuer
            };
        }

        /// <summary>
        /// Validates a public DER certificate using the existing certificate abstraction.
        /// Private-key containers are never accepted as public artifacts.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static JsonObject Certificate(ByteString bytes)
        {
            using var certificate = Security.Certificates.Certificate.FromRawData(bytes.Memory);
            if (certificate.HasPrivateKey ||
                !certificate.RawData.AsSpan().SequenceEqual(bytes.Span))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            return new JsonObject
            {
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes.Span)),
                ["subject"] = certificate.Subject,
                ["issuer"] = certificate.Issuer,
                ["notAfter"] = certificate.NotAfter.ToUniversalTime(),
                ["length"] = bytes.Length
            };
        }

        /// <summary>
        /// Reads a required output position without serializing an unexpected value.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static Variant Output(CallMethodResult call, int index)
        {
            return index < call.OutputArguments.Count
                ? call.OutputArguments[index]
                : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a typed string output, including protocol-defined null strings.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static string String(CallMethodResult call, int index)
        {
            Variant output = Output(call, index);
            return output.IsNull ? string.Empty
                : output.TryGetValue(out string value) ? GdsAccess.Text(value, allowEmpty: true)
                : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a secret-bearing string without applying the public metadata ceiling.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static string SecretString(CallMethodResult call, int index)
        {
            Variant output = Output(call, index);
            return output.IsNull ? string.Empty
                : output.TryGetValue(out string value) ? value
                : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a ByteString output without exposing its contents.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static ByteString Bytes(CallMethodResult call, int index)
        {
            Variant output = Output(call, index);
            return output.IsNull ? default
                : output.TryGetValue(out ByteString value) ? value
                : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a NodeId output.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static NodeId Node(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out NodeId value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a Guid output.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static Uuid Uuid(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out Uuid value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a protocol UTC timestamp without substituting the local clock.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static DateTimeUtc Time(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out DateTimeUtc value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a record cursor.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static uint UInt32(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out uint value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a Boolean without confusing false with optional absence.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static bool Boolean(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out bool value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads a typed StatusCode output.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static StatusCode Status(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out StatusCode value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads typed node identities.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static ArrayOf<NodeId> Nodes(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out ArrayOf<NodeId> value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads typed public certificate chains.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static ArrayOf<ByteString> ByteStrings(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out ArrayOf<ByteString> value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Reads typed per-item outcomes, not a synthesized aggregate success.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static ArrayOf<StatusCode> Statuses(CallMethodResult call, int index)
        {
            return Output(call, index).TryGetValue(out ArrayOf<StatusCode> value) ? value : throw InvalidOutput();
        }

        /// <summary>
        /// Decodes generated model records with the existing AOT-safe encodeable factory.
        /// </summary>
        /// <typeparam name="T">The generated structure element.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        internal static ArrayOf<T> Structures<T>(
            CallMethodResult call, int index, IServiceMessageContext context)
            where T : class, IEncodeable, new()
        {
            return Output(call, index).TryGetStructure(context, out ArrayOf<T> values)
                ? values : throw InvalidOutput();
        }

        /// <summary>
        /// Rejects malformed method output without copying its value or diagnostics into an exception.
        /// </summary>
        private static ServiceResultException InvalidOutput()
        {
            return new ServiceResultException(StatusCodes.BadDecodingError,
                "The method response does not match its declared output contract.");
        }
    }
}
