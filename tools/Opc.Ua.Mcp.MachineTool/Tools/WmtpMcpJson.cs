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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Reuses the stack's UA codecs and the shared AOT envelope, with local-only cancellation and safe errors.
    /// </summary>
    internal static class WmtpMcpJson
    {
        /// <summary>
        /// Bounds the local request without ever sending AbortOperation or exposing remote diagnostic contents.
        /// </summary>
        internal static async Task<CallToolResult> ExecuteAsync(
            Func<CancellationToken, Task<CallToolResult>> operation, int timeoutSeconds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                MachineToolParameters.Bound(timeoutSeconds, 1, 60);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
                try
                {
                    CallToolResult result = await operation(linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (Exception error) when (linked.IsCancellationRequested &&
                    error is OperationCanceledException or ServiceResultException)
                {
                    ct.ThrowIfCancellationRequested();
                    return Error(StatusCodes.BadTimeout,
                        "Local deadline expired; device outcome is unknown. " +
                        "No wire abort, retry or compensation sent.");
                }
            }
            catch (ServiceResultException error)
            {
                return Error(error.StatusCode,
                    "The UA operation failed or was refused. No automatic abort, retry or compensation was sent.");
            }
            catch (ArgumentOutOfRangeException)
            {
                return Error(StatusCodes.BadOutOfRange,
                    "A bounded WMTP input is out of range or not exactly representable.");
            }
            catch (ArgumentException)
            {
                return Error(StatusCodes.BadInvalidArgument, "A required WMTP input or native JSON shape is invalid.");
            }
            catch (FormatException)
            {
                return Error(StatusCodes.BadDecodingError, "A bounded WMTP input could not be decoded.");
            }
            catch (InvalidOperationException)
            {
                return Error(StatusCodes.BadInvalidState,
                    "The requested session or device state is unavailable; select an existing connected session.");
            }
            catch (TimeoutException)
            {
                return Error(StatusCodes.BadTimeout,
                    "Local deadline expired; device outcome is unknown. No wire abort, retry or compensation sent.");
            }
        }

        /// <summary>
        /// Retains every device DataValue and represents absent optional nodes as JSON null.
        /// </summary>
        internal static JsonObject Snapshot(MachineToolClient client, WmtpPeripheralSnapshot snapshot)
        {
            return new JsonObject
            {
                ["nodeId"] = snapshot.NodeId.ToString(),
                ["status"] = MachineToolJson.Readings(client,
                    ("manufacturer", snapshot.Manufacturer), ("serialNumber", snapshot.SerialNumber),
                    ("calibrationMode", snapshot.CalibrationMode), ("battery", snapshot.Battery),
                    ("licenseInformation", snapshot.LicenseInformation), ("licenseValidity", snapshot.LicenseValidity),
                    ("nextServiceDate", snapshot.NextServiceDate), ("lastService", snapshot.LastService),
                    ("lifeCycleStatus", snapshot.LifeCycleStatus), ("remainingLifetime", snapshot.RemainingLifetime),
                    ("remainingCycles", snapshot.RemainingCycles), ("nextService", snapshot.NextService),
                    ("operationHours", snapshot.OperationHours),
                    ("operationSinceService", snapshot.OperationSinceService))
            };
        }

        /// <summary>
        /// Encodes acquired measurements without turning bad values or missing metadata into plausible zeroes.
        /// </summary>
        internal static JsonObject Measurement(MachineToolClient client, WmtpMeasurementReading reading)
        {
            return new JsonObject
            {
                ["nodeId"] = reading.NodeId.ToString(),
                ["valueNodeId"] = reading.ValueNodeId.ToString(),
                ["browseName"] = reading.BrowseName.ToString(),
                ["values"] = MachineToolJson.Readings(client,
                    ("sample", reading.Sample), ("engineeringUnits", reading.EngineeringUnits),
                    ("euRange", reading.EURange), ("instrumentRange", reading.InstrumentRange),
                    ("valuePrecision", reading.ValuePrecision), ("typeOfMeasurement", reading.TypeOfMeasurement),
                    ("typeOfSample", reading.TypeOfSample), ("triggerSettings", reading.TriggerSettings),
                    ("deltaCondition", reading.DeltaCondition), ("timestamp", reading.Timestamp),
                    ("index", reading.Index), ("measurementPeriod", reading.MeasurementPeriod),
                    ("internalUpdateInterval", reading.InternalUpdateInterval),
                    ("relativeUncertainty", reading.RelativeUncertainty),
                    ("absoluteUncertainty", reading.AbsoluteUncertainty), ("status", reading.Status),
                    ("operationAboveHigh", reading.OperationAboveHigh),
                    ("operationAboveHighHigh", reading.OperationAboveHighHigh))
            };
        }

        /// <summary>
        /// Serializes the exact released WMTP output structure, including all standard optional metadata fields.
        /// </summary>
        internal static JsonObject Report(
            MachineToolClient owner, NodeId node, string dataset, WmtpRecordSelection selection,
            ArrayOf<WMTPOutputDataType> records)
        {
            using var encoder = new JsonEncoder(owner.Session.MessageContext);
            encoder.WriteEncodeableArray("records", records);
            var result = (JsonObject)JsonNode.Parse(encoder.CloseAndReturnText())!;
            result["nodeId"] = node.ToString();
            result["dataset"] = dataset;
            result["selection"] = selection.ToString();
            result["count"] = records.Count;
            result["completeSelection"] = true;
            result["consumed"] = false;
            result["completeHardwareHistory"] = false;
            return result;
        }

        /// <summary>
        /// Acknowledges a successful method response without claiming independent physical verification.
        /// </summary>
        internal static CallToolResult Accepted(NodeId node, string operation, string? dataset = null)
        {
            return MachineToolJson.Result(new JsonObject
            {
                ["nodeId"] = node.ToString(),
                ["operation"] = operation,
                ["dataset"] = dataset,
                ["accepted"] = true,
                ["physicalEffectVerified"] = false
            }, 16384);
        }

        /// <summary>
        /// Retains the exact UA status in a fixed-size diagnostic that contains no payload or server secrets.
        /// </summary>
        private static CallToolResult Error(StatusCode status, string message)
        {
            return MachineToolJson.Result(new JsonObject
            {
                ["error"] = true,
                ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(status),
                ["message"] = message
            }, 2048, isError: true);
        }
    }
}
