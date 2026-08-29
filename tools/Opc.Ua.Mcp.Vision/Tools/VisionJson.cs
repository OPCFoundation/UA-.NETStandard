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
using System.Text.Json;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Vision;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Parses Vision detection and characteristic payloads submitted through MCP
    /// tools as JSON, mapping the ROS 2 vision_msgs conventions the Vision
    /// NodeSet documents onto the generated Vision data types.
    /// </summary>
    internal static class VisionJson
    {
        public static ArrayOf<VisionDetectionDataType> BuildDetections(string detectionsJson)
        {
            if (string.IsNullOrWhiteSpace(detectionsJson))
            {
                throw new ArgumentException(
                    "Detections JSON must be a non-empty array.", nameof(detectionsJson));
            }

            using JsonDocument document = JsonDocument.Parse(detectionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    "Detections JSON must be an array.", nameof(detectionsJson));
            }

            var detections = new List<VisionDetectionDataType>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException(
                        "Each detection must be a JSON object.", nameof(detectionsJson));
                }
                detections.Add(BuildDetection(element));
            }

            return [.. detections];
        }

        public static ArrayOf<VisionCharacteristicDataType> BuildCharacteristics(string characteristicsJson)
        {
            if (string.IsNullOrWhiteSpace(characteristicsJson))
            {
                throw new ArgumentException(
                    "Characteristics JSON must be a non-empty array.", nameof(characteristicsJson));
            }

            using JsonDocument document = JsonDocument.Parse(characteristicsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    "Characteristics JSON must be an array.", nameof(characteristicsJson));
            }

            var characteristics = new List<VisionCharacteristicDataType>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException(
                        "Each characteristic must be a JSON object.", nameof(characteristicsJson));
                }
                characteristics.Add(BuildCharacteristic(element));
            }

            return [.. characteristics];
        }

        public static VisionPose3DDataType BuildPose(string poseJson, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(poseJson))
            {
                throw new ArgumentException("Pose JSON must be a non-empty object.", parameterName);
            }

            using JsonDocument document = JsonDocument.Parse(poseJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Pose JSON must be an object.", parameterName);
            }

            return BuildPose(document.RootElement, parameterName);
        }

        public static VisionImageReferenceDataType BuildImageReference(string imageJson, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(imageJson))
            {
                throw new ArgumentException(
                    "Image reference JSON must be a non-empty object.", parameterName);
            }

            using JsonDocument document = JsonDocument.Parse(imageJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Image reference JSON must be an object.", parameterName);
            }

            JsonElement root = document.RootElement;
            return new VisionImageReferenceDataType
            {
                Uri = GetString(root, "uri") ?? string.Empty,
                Digest = GetByteString(root, "digest"),
                DigestAlgorithm = GetString(root, "digestAlgorithm") ?? "SHA-256",
                Format = GetEnum(root, "format", VisionClipFormatEnum.Jpeg),
                PixelFormat = GetString(root, "pixelFormat") ?? string.Empty,
                Width = GetUInt32(root, "width", 0),
                Height = GetUInt32(root, "height", 0),
                SizeBytes = GetUInt32(root, "sizeBytes", 0),
                Timestamp = GetDateTimeUtc(root, "timestamp")
            };
        }

        private static VisionDetectionDataType BuildDetection(JsonElement element)
        {
            var detection = new VisionDetectionDataType
            {
                DetectionId = GetString(element, "detectionId") ?? string.Empty,
                ClassLabel = GetString(element, "classLabel") ?? string.Empty,
                ClassId = GetUInt32(element, "classId", 0),
                Confidence = GetDouble(element, "confidence", 0.0),
                TrackId = GetString(element, "trackId") ?? string.Empty
            };

            if (element.TryGetProperty("boundingBox2D", out JsonElement box2D) &&
                box2D.ValueKind == JsonValueKind.Object)
            {
                detection.HasBoundingBox2D = true;
                detection.BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = GetDouble(box2D, "centerX", 0.0),
                    CenterY = GetDouble(box2D, "centerY", 0.0),
                    Width = GetDouble(box2D, "width", 0.0),
                    Height = GetDouble(box2D, "height", 0.0),
                    Rotation = GetDouble(box2D, "rotation", 0.0)
                };
            }

            if (element.TryGetProperty("boundingBox3D", out JsonElement box3D) &&
                box3D.ValueKind == JsonValueKind.Object)
            {
                detection.HasBoundingBox3D = true;
                detection.BoundingBox3D = new VisionBoundingBox3DDataType
                {
                    Center = BuildPose(GetRequiredProperty(box3D, "center"), "center"),
                    Size = ReadDoubleArray(box3D, "size", 3, "boundingBox3D.size")
                };
            }

            if (element.TryGetProperty("pose", out JsonElement pose) &&
                pose.ValueKind == JsonValueKind.Object)
            {
                detection.HasPose = true;
                detection.Pose = BuildPose(pose, "pose");
            }

            return detection;
        }

        private static VisionCharacteristicDataType BuildCharacteristic(JsonElement element)
        {
            var unit = new EUInformation();
            if (element.TryGetProperty("unit", out JsonElement unitElement) &&
                unitElement.ValueKind == JsonValueKind.Object)
            {
                string? namespaceUri = GetString(unitElement, "namespaceUri");
                string? shortName = GetString(unitElement, "shortName");
                string? longName = GetString(unitElement, "longName") ?? shortName;
                if (!string.IsNullOrEmpty(shortName) && !string.IsNullOrEmpty(namespaceUri))
                {
                    unit = new EUInformation(shortName, longName ?? shortName, namespaceUri);
                }
            }
            return new VisionCharacteristicDataType
            {
                CharacteristicId = GetString(element, "characteristicId") ?? string.Empty,
                Name = GetString(element, "name") ?? string.Empty,
                Nominal = GetDouble(element, "nominal", 0.0),
                Actual = GetDouble(element, "actual", 0.0),
                Deviation = GetDouble(element, "deviation", 0.0),
                LowerTolerance = GetDouble(element, "lowerTolerance", 0.0),
                UpperTolerance = GetDouble(element, "upperTolerance", 0.0),
                Uncertainty = GetDouble(element, "uncertainty", 0.0),
                Unit = unit,
                Status = GetEnum(element, "status", VisionToleranceStatusEnum.InTolerance)
            };
        }

        private static VisionPose3DDataType BuildPose(JsonElement element, string parameterName)
        {
            ArrayOf<double> position = ReadDoubleArray(element, "position", 3, $"{parameterName}.position");
            ArrayOf<double> orientation = ReadDoubleArray(
                element, "orientation", 4, $"{parameterName}.orientation");
            ArrayOf<double> covariance = ArrayOf<double>.Empty;
            if (element.TryGetProperty("covariance", out JsonElement covarianceElement) &&
                covarianceElement.ValueKind == JsonValueKind.Array)
            {
                covariance = ReadDoubleArray(
                    element, "covariance", 36, $"{parameterName}.covariance");
            }
            return new VisionPose3DDataType
            {
                FrameId = GetString(element, "frameId") ?? string.Empty,
                Position = position,
                Orientation = orientation,
                Covariance = covariance
            };
        }

        private static ArrayOf<double> ReadDoubleArray(
            JsonElement element,
            string propertyName,
            int expectedLength,
            string context)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Missing or malformed array '{propertyName}' in {context}."),
                    context);
            }
            var buffer = new List<double>(expectedLength);
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    throw new ArgumentException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"All entries in '{propertyName}' must be numbers in {context}."),
                        context);
                }
                buffer.Add(item.GetDouble());
            }
            if (buffer.Count != expectedLength)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Array '{propertyName}' must have exactly {expectedLength} entries in {context}."),
                    context);
            }
            return [.. buffer];
        }

        private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Required property '{propertyName}' is missing."),
                    propertyName);
            }
            return value;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static uint GetUInt32(JsonElement element, string propertyName, uint fallback)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number)
            {
                return fallback;
            }
            return value.TryGetUInt32(out uint parsed) ? parsed : fallback;
        }

        private static double GetDouble(JsonElement element, string propertyName, double fallback)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number)
            {
                return fallback;
            }
            return value.GetDouble();
        }

        private static ByteString GetByteString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return ByteString.Empty;
            }
            string? base64 = value.GetString();
            if (string.IsNullOrEmpty(base64))
            {
                return ByteString.Empty;
            }
            return new ByteString(Convert.FromBase64String(base64));
        }

        private static TEnum GetEnum<TEnum>(JsonElement element, string propertyName, TEnum fallback)
            where TEnum : struct, Enum
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return fallback;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                string? name = value.GetString();
                if (!string.IsNullOrEmpty(name) &&
                    Enum.TryParse(name, ignoreCase: true, out TEnum parsed))
                {
                    return parsed;
                }
                return fallback;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }
            return fallback;
        }

        private static DateTimeUtc GetDateTimeUtc(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return default;
            }
            string? text = value.GetString();
            if (string.IsNullOrEmpty(text))
            {
                return default;
            }
            if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                    out DateTime parsed))
            {
                return new DateTimeUtc(parsed.ToUniversalTime());
            }
            return default;
        }
    }
}
