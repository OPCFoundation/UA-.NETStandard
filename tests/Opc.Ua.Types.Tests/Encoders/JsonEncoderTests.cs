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
using System.Buffers;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Unit tests for the <see cref = "JsonDecoder"/> class.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonEncoderTests
    {
        [Test]
        public void WriteBadVariantThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
#pragma warning disable CS0618 // Type or member is obsolete
            var badVariant = new Variant(new DiagnosticInfo(), TypeInfo.Scalars.DiagnosticInfo);
#pragma warning restore CS0618 // Type or member is obsolete
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);
            try
            {
                writer.WriteVariant(JsonProperties.Value, badVariant);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteBadVariantValuesThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            var badVariant = new Variant(
                default,
                TypeInfo.Arrays.DiagnosticInfo,
                new[] { new DiagnosticInfo() }.ToArrayOf());
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);
            try
            {
                writer.WriteVariant(JsonProperties.Value, badVariant);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteBooleanValuesWithLengthExceedingThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxArrayLength = 4
            };
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);
            try
            {
                ArrayOf<bool> b = new bool[6].ToArrayOf();
                writer.WriteBooleanArray(JsonProperties.Value, b);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteByteStringWithLengthExceedingThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxByteStringLength = 4
            };
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            try
            {
                var bytes = ByteString.From(new byte[6]);
                writer.WriteByteString(JsonProperties.Value, bytes);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteByteValuesWithLengthExceedingThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxArrayLength = 4
            };
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            try
            {
                ArrayOf<byte> bytes = new byte[6].ToArrayOf();
                writer.WriteByteArray(JsonProperties.Value, bytes);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteDataValueUsesStatusMember()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using (var writer = new JsonEncoder(buffer, messageContext))
            {
                DataValue value = DataValue.FromStatusCode(StatusCodes.BadNotWritable);
                writer.WriteDataValue(JsonProperties.Value, value);
            }

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement dataValue = document.RootElement.GetProperty(JsonProperties.Value);
            Assert.Multiple(() =>
            {
                Assert.That(dataValue.TryGetProperty("Status", out _), Is.True);
                Assert.That(dataValue.TryGetProperty("StatusCode", out _), Is.False);
            });
        }

        [Test]
        public void WriteStatusCodeRawDataOmitsSymbol()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using (var writer = new JsonEncoder(buffer, messageContext, JsonEncoderOptions.RawData))
            {
                writer.WriteStatusCode(JsonProperties.Value, StatusCodes.BadNotWritable);
            }

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement status = document.RootElement.GetProperty(JsonProperties.Value);

            Assert.Multiple(() =>
            {
                Assert.That(status.TryGetProperty(JsonProperties.Code, out _), Is.True);
                Assert.That(status.TryGetProperty(JsonProperties.Symbol, out _), Is.False);
            });
        }

        [Test]
        public void WriteDataValueWithSourcePicosecondsWithoutTimestampThrowsBadEncodingError()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);
            var value = new DataValue(
                Variant.From("value"),
                StatusCodes.Good,
                DateTimeUtc.MinValue,
                DateTimeUtc.MinValue,
                sourcePicoseconds: 1,
                serverPicoseconds: 0);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => writer.WriteDataValue(JsonProperties.Value, value));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteDataValueWithServerPicosecondsWithoutTimestampThrowsBadEncodingError()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);
            var value = new DataValue(
                Variant.From("value"),
                StatusCodes.Good,
                DateTimeUtc.MinValue,
                DateTimeUtc.MinValue,
                sourcePicoseconds: 0,
                serverPicoseconds: 1);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => writer.WriteDataValue(JsonProperties.Value, value));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteExtensionObjectJsonBodyWritesRawObjectProperties()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using (var writer = new JsonEncoder(buffer, messageContext))
            {
                var value = new ExtensionObject(
                    new ExpandedNodeId(1),
                    /*lang=json,strict*/ """{"A":1,"B":{"C":true}}""");
                writer.WriteExtensionObject(JsonProperties.Value, value);
            }

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement valueElement = document.RootElement.GetProperty(JsonProperties.Value);
            Assert.Multiple(() =>
            {
                Assert.That(valueElement.GetProperty("A").GetInt32(), Is.EqualTo(1));
                Assert.That(valueElement.GetProperty("B").GetProperty("C").GetBoolean(), Is.True);
            });
        }

        [Test]
        public void WriteExtensionObjectJsonBodyDoesNotDuplicateUaTypeId()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using (var writer = new JsonEncoder(buffer, messageContext))
            {
                var value = new ExtensionObject(
                    new ExpandedNodeId(809),
                    /*lang=json,strict*/ """{"UaTypeId":"i=809","MonitoredItems":[]}""");
                writer.WriteExtensionObject(JsonProperties.Value, value);
            }

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement valueElement = document.RootElement.GetProperty(JsonProperties.Value);

            Assert.That(
                valueElement.EnumerateObject().Count(property => property.Name == JsonProperties.UaTypeId),
                Is.EqualTo(1));
        }

        [Test]
        public void WriteExtensionObjectWithUnencodableBodylessTypeIdThrowsBadEncodingError()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            var value = new ExtensionObject(new ExpandedNodeId(NodeId.Null, "urn:test"));
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => writer.WriteExtensionObject(JsonProperties.Value, value));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteExtensionObjectMalformedJsonBodyThrowsBadEncodingError()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);
            var value = new ExtensionObject(
                new ExpandedNodeId(1),
                /*lang=json,strict*/ """{"A":1,"B":}""");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => writer.WriteExtensionObject(JsonProperties.Value, value));
            Assert.Multiple(() =>
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
                Assert.That(ex.InnerException, Is.InstanceOf<JsonException>());
            });
        }

        [Test]
        public void WriteQualifiedNameWithNamespaceQualifiedEmptyNameRoundTrips()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            messageContext.NamespaceUris.Append("urn:test");
            var expected = new QualifiedName(string.Empty, 1);
            using var buffer = new PooledBufferWriter();
            using (var writer = new JsonEncoder(buffer, messageContext))
            {
                writer.WriteQualifiedName(JsonProperties.Value, expected);
            }

            using var decoder = new JsonDecoder(buffer.WrittenMemory.ToReadOnlySequence(16), messageContext);
            QualifiedName actual = decoder.ReadQualifiedName(JsonProperties.Value);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void WriteQualifiedNameWithNamespaceQualifiedNullNameThrowsBadEncodingError()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            var value = new QualifiedName(null, 1);
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => writer.WriteQualifiedName(JsonProperties.Value, value));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteDiagnosticInfosWithNestingLevelsExceedingThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxEncodingNestingLevels = 1
            };
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            try
            {
                writer.WriteDiagnosticInfo(JsonProperties.Value, new DiagnosticInfo
                {
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            InnerDiagnosticInfo = new DiagnosticInfo()
                        }
                    }
                });
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteLocalDateTime()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var expected = (DateTime)DateTime.UtcNow;
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            using var buffers = new PooledBufferWriter();

            using (var writer = new JsonEncoder(buffers, messageContext))
            {
                writer.WriteDateTime(JsonProperties.Value, expected);
            }

            using var reader = new JsonDecoder(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            var result = (DateTime)reader.ReadDateTime(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void WriteStringWithLengthExceedingThrows()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxStringLength = 4
            };
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            try
            {
                writer.WriteString(JsonProperties.Value, "123456");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteStructureThrowsIfNestingLimitsExceeded()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxEncodingNestingLevels = 1
            };
            using var buffer = new PooledBufferWriter();
            using var writer = new JsonEncoder(buffer, messageContext);

            try
            {
                writer.WriteEncodeable(JsonProperties.Value, CreateType);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        [Test]
        public void WriteVariantThrowsIfNestingLimitsExceeded()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, new EncodeableFactory())
            {
                MaxEncodingNestingLevels = 1
            };
            using var buffer = new PooledBufferWriter();
            var variant = new Variant(new DataValue(new Variant(new DataValue(new Variant(1)))));
            using var writer = new JsonEncoder(buffer, messageContext);

            try
            {
                writer.WriteVariant(JsonProperties.Value, variant);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            Assert.Fail("Exception not thrown");
        }

        public static Argument CreateType => new()
        {
            Description = LocalizedText.From("Test"),
            Value = Enumerable.Repeat(new BrowseDescription
            {
                NodeId = new NodeId(5),
                ReferenceTypeId = new NodeId(5),
                IncludeSubtypes = false
            }, 100).ToArrayOf()
        };
    }
}
