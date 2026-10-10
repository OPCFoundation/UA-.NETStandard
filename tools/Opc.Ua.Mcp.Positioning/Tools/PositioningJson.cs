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
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit positioning record projections; UA structures use the session's encoder.
    /// </summary>
    internal static class PositioningJson
    {
        /// <summary>
        /// Projects an entry without losing its qualified name, locale or vendor type.
        /// </summary>
        internal static JsonObject Entry(PositioningObjectEntry value)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["browseName"] = new JsonObject
                {
                    ["namespaceIndex"] = value.BrowseName.NamespaceIndex,
                    ["name"] = value.BrowseName.Name
                },
                ["displayName"] = new JsonObject
                {
                    ["locale"] = value.DisplayName.Locale,
                    ["text"] = value.DisplayName.Text
                },
                ["typeDefinitionId"] = value.TypeDefinitionId.ToString()
            };
        }

        /// <summary>
        /// Projects a raw RSL frame and its read provenance.
        /// </summary>
        internal static JsonObject Frame(RelativeSpatialFrameValue value, IServiceMessageContext context)
        {
            JsonObject result = Metadata(value.NodeId, value.StatusCode, value.SourceTimestamp, context);
            result["baseNodeId"] = value.BaseNodeId.ToString();
            result["valueType"] = nameof(ThreeDFrame);
            result["frame"] = McpCompanionJson.Encode(value.Frame, context);
            return result;
        }

        /// <summary>
        /// Projects a resolved frame using the existing transform implementation.
        /// </summary>
        internal static JsonObject Resolved(
            ResolvedRelativeSpatialFrame value,
            PositioningAngleUnit angleUnit,
            IServiceMessageContext context)
        {
            var chain = new JsonArray();
            foreach (NodeId nodeId in value.FrameChain)
            {
                chain.Add(nodeId.ToString());
            }

            var rotation = new JsonArray();
            foreach (double element in value.TransformToWorld.RotationMatrix)
            {
                rotation.Add(McpCompanionJson.Number(element));
            }

            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["angleUnit"] = angleUnit.ToString(),
                ["frameChain"] = chain,
                ["rotationMatrixRowMajor"] = rotation,
                ["translation"] = McpCompanionJson.Encode(value.TransformToWorld.Translation, context),
                ["frameToWorld"] = McpCompanionJson.Encode(
                    value.TransformToWorld.ToFrame(PositioningFitOptions.ToAngleUnit(angleUnit)), context)
            };
        }

        /// <summary>
        /// Projects a list version notification.
        /// </summary>
        internal static JsonObject Version(PositioningNodeVersionChange value, IServiceMessageContext context)
        {
            JsonObject result = Metadata(value.ListNodeId, value.StatusCode, value.SourceTimestamp, context);
            result["listNodeId"] = value.ListNodeId.ToString();
            result["nodeVersion"] = value.NodeVersion;
            return result;
        }

        /// <summary>
        /// Projects a GlobalPosition including its raw server CRS and source node.
        /// </summary>
        internal static JsonObject Position(GlobalPositionValue value, IServiceMessageContext context)
        {
            JsonObject result = Metadata(value.NodeId, value.StatusCode, value.SourceTimestamp, context);
            result["sourceNodeId"] = value.SourceNodeId.ToString();
            result["coordinateReferenceSystem"] = value.CoordinateReferenceSystem;
            result["valueType"] = nameof(Gpos.GlobalPositionDataType);
            result["position"] = McpCompanionJson.Encode(value.Position, context);
            return result;
        }

        /// <summary>
        /// Projects a GlobalLocation, preserving the optional fields through UA encoding.
        /// </summary>
        internal static JsonObject Location(GlobalLocationValue value, IServiceMessageContext context)
        {
            JsonObject result = Metadata(value.NodeId, value.StatusCode, value.SourceTimestamp, context);
            result["sourceNodeId"] = value.SourceNodeId.ToString();
            result["coordinateReferenceSystem"] = value.CoordinateReferenceSystem;
            result["valueType"] = nameof(Gpos.GlobalLocationDataType);
            result["location"] = McpCompanionJson.Encode(value.Location, context);
            return result;
        }

        /// <summary>
        /// Projects all public fit diagnostics and the effective fitting options.
        /// </summary>
        internal static JsonObject Fit(
            NodeId zoneId,
            GroundControlPointFitResult fit,
            GroundControlPointFitOptions options)
        {
            JsonObject result = FitContext(zoneId, options);
            result["dimension"] = fit.Dimension;
            result["rootMeanSquareError"] = McpCompanionJson.Number(fit.RootMeanSquareError);
            result["maxResidual"] = McpCompanionJson.Number(fit.MaxResidual);
            result["determinant"] = McpCompanionJson.Number(fit.Determinant);
            result["rank"] = fit.Rank;
            result["isInvertible"] = fit.IsInvertible;
            return result;
        }

        /// <summary>
        /// Identifies the Zone and actual CRS/units used, not an inferred server CRS.
        /// </summary>
        internal static JsonObject FitContext(NodeId zoneId, GroundControlPointFitOptions options)
        {
            return new JsonObject
            {
                ["zoneNodeId"] = zoneId.ToString(),
                ["mode"] = options.Mode.ToString(),
                ["controlPointAngleUnit"] = options.AngleUnit.ToString(),
                ["allowReflection"] = options.AllowReflection,
                ["transformerCoordinateReferenceSystem"] = options.CoordinateReferenceSystem?.CoordinateReferenceSystem
            };
        }

        /// <summary>
        /// Rejects absent target NodeIds instead of accidentally selecting the null node.
        /// </summary>
        internal static NodeId ParseNodeId(string? nodeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
            NodeId parsed = NodeId.Parse(nodeId);
            if (parsed.IsNull)
            {
                throw new ArgumentException("A non-null target NodeId is required.", nameof(nodeId));
            }

            return parsed;
        }

        /// <summary>
        /// Rejects undefined enum values, including numeric values sent by an MCP client.
        /// </summary>
        /// <typeparam name="T">The finite discriminator type.</typeparam>
        internal static void Validate<T>(T value)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown positioning operation.");
            }
        }

        /// <summary>
        /// Preserves status and the UA-encoded timestamp without reflection serialization.
        /// </summary>
        private static JsonObject Metadata(
            NodeId nodeId,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp,
            IServiceMessageContext context)
        {
            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            encoder.WriteStatusCode("statusCode", statusCode);
            encoder.WriteDateTime("sourceTimestamp", sourceTimestamp);
            return new JsonObject
            {
                ["nodeId"] = nodeId.ToString(),
                ["statusCode"] = statusCode.Code,
                ["metadata"] = JsonNode.Parse(encoder.CloseAndReturnText())
            };
        }
    }
}
