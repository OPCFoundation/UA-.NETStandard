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

#if NET10_0
using System;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Vision;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [Category("Vision")]
    [Category("MCP")]
    public sealed class VisionJsonTests
    {
        [Test]
        public void BuildDetectionsWithNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildDetections(null!));
        }

        [Test]
        public void BuildDetectionsWithEmptyStringThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildDetections(string.Empty));
        }

        [Test]
        public void BuildDetectionsWithNonArrayJsonThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildDetections("{}"));
        }

        [Test]
        public void BuildDetectionsWithNonObjectElementThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildDetections("[42]"));
        }

        [Test]
        public void BuildDetectionsReturnsEmptyArrayForEmptyJsonArray()
        {
            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections("[]");
            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public void BuildDetectionsParsesSingleDetection()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "detectionId": "det-1",
                    "classLabel": "bolt",
                    "classId": 7,
                    "confidence": 0.95,
                    "trackId": "trk-1"
                }
            ]
            """;

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(result[0].DetectionId, Is.EqualTo("det-1"));
                Assert.That(result[0].ClassLabel, Is.EqualTo("bolt"));
                Assert.That(result[0].ClassId, Is.EqualTo(7));
                Assert.That(result[0].Confidence, Is.EqualTo(0.95).Within(0.001));
                Assert.That(result[0].TrackId, Is.EqualTo("trk-1"));
            });
        }

        [Test]
        public void BuildDetectionsParsesBoundingBox2D()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "detectionId": "det-2",
                    "classLabel": "nut",
                    "confidence": 0.8,
                    "boundingBox2D": {
                        "centerX": 320.0,
                        "centerY": 240.0,
                        "width": 50.0,
                        "height": 30.0,
                        "rotation": 0.1
                    }
                }
            ]
            """;

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.That(result[0].HasBoundingBox2D, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result[0].BoundingBox2D.CenterX, Is.EqualTo(320.0));
                Assert.That(result[0].BoundingBox2D.CenterY, Is.EqualTo(240.0));
                Assert.That(result[0].BoundingBox2D.Width, Is.EqualTo(50.0));
                Assert.That(result[0].BoundingBox2D.Height, Is.EqualTo(30.0));
                Assert.That(result[0].BoundingBox2D.Rotation, Is.EqualTo(0.1).Within(0.001));
            });
        }

        [Test]
        public void BuildDetectionsParsesBoundingBox3D()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "detectionId": "det-3",
                    "classLabel": "part",
                    "confidence": 0.9,
                    "boundingBox3D": {
                        "center": {
                            "position": [1.0, 2.0, 3.0],
                            "orientation": [0.0, 0.0, 0.0, 1.0]
                        },
                        "size": [0.1, 0.2, 0.3]
                    }
                }
            ]
            """;

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.That(result[0].HasBoundingBox3D, Is.True);
            Assert.That(result[0].BoundingBox3D.Size.Count, Is.EqualTo(3));
        }

        [Test]
        public void BuildDetectionsParsesDetectionPose()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "detectionId": "det-4",
                    "classLabel": "widget",
                    "confidence": 0.85,
                    "pose": {
                        "frameId": "camera",
                        "position": [0.5, 0.6, 0.7],
                        "orientation": [0.0, 0.0, 0.707, 0.707]
                    }
                }
            ]
            """;

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.That(result[0].HasPose, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result[0].Pose.FrameId, Is.EqualTo("camera"));
                Assert.That(result[0].Pose.Position.Count, Is.EqualTo(3));
                Assert.That(result[0].Pose.Orientation.Count, Is.EqualTo(4));
            });
        }

        [Test]
        public void BuildDetectionsParsesMultipleDetections()
        {
            const string json = /*lang=json,strict*/ """
            [
                {"detectionId": "a", "classLabel": "x", "confidence": 0.1},
                {"detectionId": "b", "classLabel": "y", "confidence": 0.2}
            ]
            """;

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].DetectionId, Is.EqualTo("a"));
            Assert.That(result[1].DetectionId, Is.EqualTo("b"));
        }

        [Test]
        public void BuildCharacteristicsWithNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildCharacteristics(null!));
        }

        [Test]
        public void BuildCharacteristicsWithEmptyStringThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildCharacteristics(string.Empty));
        }

        [Test]
        public void BuildCharacteristicsWithNonArrayThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildCharacteristics("{}"));
        }

        [Test]
        public void BuildCharacteristicsWithNonObjectElementThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildCharacteristics("[123]"));
        }

        [Test]
        public void BuildCharacteristicsReturnsEmptyForEmptyArray()
        {
            ArrayOf<VisionCharacteristicDataType> result = VisionJson.BuildCharacteristics("[]");
            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public void BuildCharacteristicsParsesSingleCharacteristic()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "characteristicId": "c1",
                    "name": "diameter",
                    "nominal": 10.0,
                    "actual": 10.05,
                    "deviation": 0.05,
                    "lowerTolerance": -0.1,
                    "upperTolerance": 0.1,
                    "uncertainty": 0.01,
                    "status": "OutOfTolerance"
                }
            ]
            """;

            ArrayOf<VisionCharacteristicDataType> result = VisionJson.BuildCharacteristics(json);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(result[0].CharacteristicId, Is.EqualTo("c1"));
                Assert.That(result[0].Name, Is.EqualTo("diameter"));
                Assert.That(result[0].Nominal, Is.EqualTo(10.0));
                Assert.That(result[0].Actual, Is.EqualTo(10.05).Within(0.001));
                Assert.That(result[0].Deviation, Is.EqualTo(0.05).Within(0.001));
                Assert.That(result[0].LowerTolerance, Is.EqualTo(-0.1));
                Assert.That(result[0].UpperTolerance, Is.EqualTo(0.1));
                Assert.That(result[0].Uncertainty, Is.EqualTo(0.01).Within(0.001));
                Assert.That(result[0].Status, Is.EqualTo(VisionToleranceStatusEnum.OutOfTolerance));
            });
        }

        [Test]
        public void BuildCharacteristicsWithUnitParsesEuInformation()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "characteristicId": "c2",
                    "name": "length",
                    "nominal": 25.0,
                    "actual": 25.01,
                    "deviation": 0.01,
                    "unit": {
                        "namespaceUri": "http://www.opcfoundation.org/UA/units/un/cefact",
                        "shortName": "mm",
                        "longName": "millimetre"
                    }
                }
            ]
            """;

            ArrayOf<VisionCharacteristicDataType> result = VisionJson.BuildCharacteristics(json);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result[0].Unit.NamespaceUri,
                    Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"));
                Assert.That(result[0].Unit.DisplayName.Text, Is.EqualTo("mm"));
                Assert.That(result[0].Unit.Description.Text, Is.EqualTo("millimetre"));
            });
        }

        [Test]
        public void BuildPoseWithNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildPose(null!, "test"));
        }

        [Test]
        public void BuildPoseWithEmptyStringThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildPose(string.Empty, "test"));
        }

        [Test]
        public void BuildPoseWithNonObjectThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => VisionJson.BuildPose("[]", "test"));
        }

        [Test]
        public void BuildPoseReturnsPopulatedPose()
        {
            const string json = /*lang=json,strict*/ """
            {
                "frameId": "base_link",
                "position": [1.0, 2.0, 3.0],
                "orientation": [0.0, 0.0, 0.0, 1.0]
            }
            """;

            VisionPose3DDataType pose = VisionJson.BuildPose(json, "test");

            Assert.Multiple(() =>
            {
                Assert.That(pose.FrameId, Is.EqualTo("base_link"));
                Assert.That(pose.Position.Count, Is.EqualTo(3));
                Assert.That(pose.Position[0], Is.EqualTo(1.0));
                Assert.That(pose.Orientation.Count, Is.EqualTo(4));
                Assert.That(pose.Orientation[3], Is.EqualTo(1.0));
            });
        }

        [Test]
        public void BuildPoseWithCovarianceParsesFullArray()
        {
            string covariance = string.Join(",", new double[36]);
            string json = $$"""
            {
                "frameId": "world",
                "position": [0.0, 0.0, 0.0],
                "orientation": [0.0, 0.0, 0.0, 1.0],
                "covariance": [{{covariance}}]
            }
            """;

            VisionPose3DDataType pose = VisionJson.BuildPose(json, "test");

            Assert.That(pose.Covariance.Count, Is.EqualTo(36));
        }

        [Test]
        public void BuildImageReferenceWithNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => VisionJson.BuildImageReference(null!, "test"));
        }

        [Test]
        public void BuildImageReferenceWithEmptyStringThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => VisionJson.BuildImageReference(string.Empty, "test"));
        }

        [Test]
        public void BuildImageReferenceWithNonObjectThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => VisionJson.BuildImageReference("[]", "test"));
        }

        [Test]
        public void BuildImageReferenceReturnsPopulatedReference()
        {
            const string json = /*lang=json,strict*/ """
            {
                "uri": "https://images.example.com/frame001.jpg",
                "format": "Jpeg",
                "pixelFormat": "RGB8",
                "width": 1920,
                "height": 1080,
                "sizeBytes": 204800,
                "digest": "AQID",
                "digestAlgorithm": "SHA-256"
            }
            """;

            VisionImageReferenceDataType result = VisionJson.BuildImageReference(json, "test");

            Assert.Multiple(() =>
            {
                Assert.That(result.Uri, Is.EqualTo("https://images.example.com/frame001.jpg"));
                Assert.That(result.PixelFormat, Is.EqualTo("RGB8"));
                Assert.That(result.Width, Is.EqualTo(1920));
                Assert.That(result.Height, Is.EqualTo(1080));
                Assert.That(result.SizeBytes, Is.EqualTo(204800));
                Assert.That(result.DigestAlgorithm, Is.EqualTo("SHA-256"));
                Assert.That(result.Digest.Length, Is.GreaterThan(0));
            });
        }

        [Test]
        public void BuildImageReferenceWithMinimalFieldsUsesDefaults()
        {
            const string json = /*lang=json,strict*/ """
            {
                "uri": "https://example.com/img.png"
            }
            """;

            VisionImageReferenceDataType result = VisionJson.BuildImageReference(json, "test");

            Assert.Multiple(() =>
            {
                Assert.That(result.Uri, Is.EqualTo("https://example.com/img.png"));
                Assert.That(result.DigestAlgorithm, Is.EqualTo("SHA-256"));
                Assert.That(result.Width, Is.Zero);
                Assert.That(result.Height, Is.Zero);
            });
        }

        [Test]
        public void BuildDetectionsWithPoseWithoutCovarianceProducesEmptyCovariance()
        {
            const string json = /*lang=json,strict*/ """
            [
                {
                    "detectionId": "det-5",
                    "classLabel": "item",
                    "confidence": 0.7,
                    "pose": {
                        "position": [1.0, 0.0, 0.0],
                        "orientation": [0.0, 0.0, 0.0, 1.0]
                    }
                }
            ]
            """;

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.That(result[0].Pose.Covariance.Count, Is.Zero);
        }

        [Test]
        public void BuildDetectionsWithMinimalFieldsUsesDefaults()
        {
            const string json = /*lang=json,strict*/ """[{"detectionId":"","classLabel":"","confidence":0}]""";

            ArrayOf<VisionDetectionDataType> result = VisionJson.BuildDetections(json);

            Assert.Multiple(() =>
            {
                Assert.That(result[0].DetectionId, Is.EqualTo(string.Empty));
                Assert.That(result[0].HasBoundingBox2D, Is.False);
                Assert.That(result[0].HasBoundingBox3D, Is.False);
                Assert.That(result[0].HasPose, Is.False);
            });
        }
    }
}
#endif
