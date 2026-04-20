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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UadpDataSetMessageAdditionalTests
    {
        private const byte kFieldTypeBitMask = 0x06;

        private static readonly ConfigurationVersionDataType s_defaultMetaDataVersion =
            new()
            { MajorVersion = 1, MinorVersion = 1 };

        private const UadpDataSetMessageContentMask AllMessageContentFlags =
            UadpDataSetMessageContentMask.SequenceNumber |
            UadpDataSetMessageContentMask.Status |
            UadpDataSetMessageContentMask.MajorVersion |
            UadpDataSetMessageContentMask.MinorVersion |
            UadpDataSetMessageContentMask.Timestamp |
            UadpDataSetMessageContentMask.PicoSeconds;

        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void SetFieldContentMaskNoneSetsVariantEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            int fieldType = ((byte)message.DataSetFlags1 & kFieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.Zero, "Expected Variant (0)");
        }

        [Test]
        public void SetFieldContentMaskRawDataSetsRawDataEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            int fieldType = ((byte)message.DataSetFlags1 & kFieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(1), "Expected RawData (1)");
        }

        [Test]
        public void SetFieldContentMaskStatusCodeSetsDataValueEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.StatusCode);

            int fieldType = ((byte)message.DataSetFlags1 & kFieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(2), "Expected DataValue (2)");
        }

        [Test]
        public void SetFieldContentMaskSourceTimestampSetsDataValueEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.SourceTimestamp);

            int fieldType = ((byte)message.DataSetFlags1 & kFieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(2), "Expected DataValue (2)");
        }

        [Test]
        public void SetFieldContentMaskServerTimestampSetsDataValueEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.ServerTimestamp);

            int fieldType = ((byte)message.DataSetFlags1 & kFieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(2), "Expected DataValue (2)");
        }

        [Test]
        public void SetMessageContentMaskSequenceNumberSetsFlag()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(UadpDataSetMessageContentMask.SequenceNumber);

            Assert.That(
                message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.SequenceNumber),
                Is.True);
        }

        [Test]
        public void SetMessageContentMaskStatusSetsFlag()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(UadpDataSetMessageContentMask.Status);

            Assert.That(
                message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.Status),
                Is.True);
        }

        [Test]
        public void SetMessageContentMaskMajorVersionSetsFlag()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(UadpDataSetMessageContentMask.MajorVersion);

            Assert.That(
                message.DataSetFlags1.HasFlag(
                    DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion),
                Is.True);
        }

        [Test]
        public void SetMessageContentMaskMinorVersionSetsFlag()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(UadpDataSetMessageContentMask.MinorVersion);

            Assert.That(
                message.DataSetFlags1.HasFlag(
                    DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion),
                Is.True);
        }

        [Test]
        public void SetMessageContentMaskTimestampSetsFlags2()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(UadpDataSetMessageContentMask.Timestamp);

            Assert.That(
                message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.DataSetFlags2),
                Is.True);
            Assert.That(
                message.DataSetFlags2.HasFlag(DataSetFlags2EncodingMask.Timestamp),
                Is.True);
        }

        [Test]
        public void SetMessageContentMaskPicoSecondsSetsFlags2()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(UadpDataSetMessageContentMask.PicoSeconds);

            Assert.That(
                message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.DataSetFlags2),
                Is.True);
            Assert.That(
                message.DataSetFlags2.HasFlag(DataSetFlags2EncodingMask.PicoSeconds),
                Is.True);
        }

        [Test]
        public void SetMessageContentMaskAllFlagsSet()
        {
            var message = new UadpDataSetMessage();
            message.SetMessageContentMask(AllMessageContentFlags);

            Assert.That(
                message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.SequenceNumber),
                Is.True);
            Assert.That(
                message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.Status),
                Is.True);
            Assert.That(
                message.DataSetFlags1.HasFlag(
                    DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion),
                Is.True);
            Assert.That(
                message.DataSetFlags1.HasFlag(
                    DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion),
                Is.True);
            Assert.That(
                message.DataSetFlags2.HasFlag(DataSetFlags2EncodingMask.Timestamp),
                Is.True);
            Assert.That(
                message.DataSetFlags2.HasFlag(DataSetFlags2EncodingMask.PicoSeconds),
                Is.True);
        }

        [Test]
        public void EncodeDecodeKeyFrameVariant()
        {
            DataSet dataSet = CreateKeyFrameDataSet(
                ("Int32Field", BuiltInType.Int32, 42));

            byte[] encoded = EncodeMessage(
                dataSet,
                DataSetFieldContentMask.None,
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);

            UadpDataSetMessage decoded = DecodeMessage(
                encoded,
                dataSet,
                DataSetFieldContentMask.None,
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);

            Assert.That(decoded.DataSet, Is.Not.Null);
            Assert.That(decoded.DataSet.Fields, Has.Length.EqualTo(1));
            Assert.That(
                decoded.DataSet.Fields[0].Value.WrappedValue.GetInt32(),
                Is.EqualTo(42));
        }

        [Test]
        public void EncodeDecodeKeyFrameDataValue()
        {
            DataSet dataSet = CreateKeyFrameDataSet(
                ("Int32Field", BuiltInType.Int32, 99));

            const DataSetFieldContentMask fieldMask = DataSetFieldContentMask.StatusCode;
            const UadpDataSetMessageContentMask msgMask =
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion;

            byte[] encoded = EncodeMessage(dataSet, fieldMask, msgMask);
            UadpDataSetMessage decoded = DecodeMessage(
                encoded, dataSet, fieldMask, msgMask);

            Assert.That(decoded.DataSet, Is.Not.Null);
            Assert.That(decoded.DataSet.Fields, Has.Length.EqualTo(1));
            Assert.That(
                decoded.DataSet.Fields[0].Value.WrappedValue.GetInt32(),
                Is.EqualTo(99));
        }

        [Test]
        public void EncodeDecodeKeyFrameRawData()
        {
            DataSet dataSet = CreateKeyFrameDataSet(
                ("Int32Field", BuiltInType.Int32, 77));

            const DataSetFieldContentMask fieldMask = DataSetFieldContentMask.RawData;
            const UadpDataSetMessageContentMask msgMask =
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion;

            byte[] encoded = EncodeMessage(dataSet, fieldMask, msgMask);
            UadpDataSetMessage decoded = DecodeMessage(
                encoded, dataSet, fieldMask, msgMask);

            Assert.That(decoded.DataSet, Is.Not.Null);
            Assert.That(decoded.DataSet.Fields, Has.Length.EqualTo(1));
            Assert.That(
                decoded.DataSet.Fields[0].Value.WrappedValue.GetInt32(),
                Is.EqualTo(77));
        }

        [Test]
        public void EncodeDeltaFrameRoundTrip()
        {
            DataSet dataSet = CreateKeyFrameDataSet(
                ("Int32Field", BuiltInType.Int32, 55));
            dataSet.IsDeltaFrame = true;

            const UadpDataSetMessageContentMask msgMask =
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion;

            byte[] encoded = EncodeMessage(
                dataSet, DataSetFieldContentMask.None, msgMask);
            UadpDataSetMessage decoded = DecodeMessage(
                encoded, dataSet, DataSetFieldContentMask.None, msgMask);

            Assert.That(decoded.DataSet, Is.Not.Null);
            Assert.That(decoded.DataSet.Fields, Has.Length.EqualTo(1));
            Assert.That(
                decoded.DataSet.Fields[0].Value.WrappedValue.GetInt32(),
                Is.EqualTo(55));
        }

        [Test]
        public void EncodeWithConfiguredSizePads()
        {
            DataSet dataSet = CreateKeyFrameDataSet(
                ("Int32Field", BuiltInType.Int32, 1));

            var message = new UadpDataSetMessage(dataSet);
            message.SetFieldContentMask(DataSetFieldContentMask.None);
            message.SetMessageContentMask(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            message.MetaDataVersion = s_defaultMetaDataVersion;
            message.ConfiguredSize = 256;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            using (var stream = new MemoryStream())
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                message.Encode(encoder);
            }

            Assert.That(message.PayloadSizeInStream, Is.EqualTo(256));
        }

        [Test]
        public void EncodeWithDataSetOffsetSetsPosition()
        {
            DataSet dataSet = CreateKeyFrameDataSet(
                ("Int32Field", BuiltInType.Int32, 1));

            var message = new UadpDataSetMessage(dataSet);
            message.SetFieldContentMask(DataSetFieldContentMask.None);
            message.SetMessageContentMask(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            message.MetaDataVersion = s_defaultMetaDataVersion;
            message.DataSetOffset = 100;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            using (var stream = new MemoryStream(new byte[512]))
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                message.Encode(encoder);
            }

            Assert.That(message.StartPositionInStream, Is.EqualTo(100));
        }

        private static DataSet CreateKeyFrameDataSet(
            params (string Name, BuiltInType Type, int Value)[] fields)
        {
            var fieldList = new List<Field>();
            foreach ((string name, BuiltInType type, int value) in fields)
            {
                fieldList.Add(new Field
                {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = name,
                        BuiltInType = (byte)type,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(Variant.From(value))
                });
            }
            return new DataSet("TestDataSet") { Fields = [.. fieldList] };
        }

        private byte[] EncodeMessage(
            DataSet dataSet,
            DataSetFieldContentMask fieldMask,
            UadpDataSetMessageContentMask msgMask)
        {
            var message = new UadpDataSetMessage(dataSet);
            message.SetFieldContentMask(fieldMask);
            message.SetMessageContentMask(msgMask);
            message.MetaDataVersion = s_defaultMetaDataVersion;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, context, true);
            message.Encode(encoder);
            return stream.ToArray();
        }

        private UadpDataSetMessage DecodeMessage(
            byte[] encoded,
            DataSet dataSet,
            DataSetFieldContentMask fieldMask,
            UadpDataSetMessageContentMask msgMask)
        {
            var decodedMessage = new UadpDataSetMessage();
            decodedMessage.SetFieldContentMask(fieldMask);
            decodedMessage.SetMessageContentMask(msgMask);
            decodedMessage.MetaDataVersion = s_defaultMetaDataVersion;

            DataSetReaderDataType reader = CreateDataSetReader(dataSet);

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            using (var decoder = new BinaryDecoder(encoded, context))
            {
                decodedMessage.DecodePossibleDataSetReader(decoder, reader);
            }
            return decodedMessage;
        }

        private static DataSetReaderDataType CreateDataSetReader(DataSet dataSet)
        {
            var metaData = new DataSetMetaDataType
            {
                ConfigurationVersion = s_defaultMetaDataVersion,
                Fields = dataSet.Fields
                    .Select(f => f.FieldMetaData)
                    .ToArray()
            };

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                DataSetMetaData = metaData,
                MessageSettings = new ExtensionObject(
                    new UadpDataSetReaderMessageDataType())
            };
            return reader;
        }
    }
}