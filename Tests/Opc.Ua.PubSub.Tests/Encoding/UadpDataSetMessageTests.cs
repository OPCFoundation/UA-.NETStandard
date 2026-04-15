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
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpDataSetMessage objects")]
    public class UadpDataSetMessageTests
    {
        private readonly string m_publisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private readonly string m_subscriberConfigurationFileName = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        private PubSubConfigurationDataType m_publisherConfiguration;
        private UaPubSubApplication m_publisherApplication;
        private WriterGroupDataType m_firstWriterGroup;
        private IUaPubSubConnection m_firstPublisherConnection;
        private ITelemetryContext m_telemetry;

        private PubSubConfigurationDataType m_subscriberConfiguration;
        private UaPubSubApplication m_subscriberApplication;
        private ReaderGroupDataType m_firstReaderGroup;
        private DataSetReaderDataType m_firstDataSetReaderType;

        private const ushort kNamespaceIndexSimple = 2;

        /// <summary>
        /// just for test match the DataSet1->DataSetWriterId
        /// </summary>
        private const ushort kTestDataSetWriterId = 1;
        private const ushort kMessageContentMask = 0x3f;

        [OneTimeTearDown]
        public void MyTestTearDown()
        {
            m_subscriberApplication?.Dispose();
            m_publisherApplication?.Dispose();
        }

        [OneTimeSetUp]
        public void MyTestInitialize()
        {
            // Create a publisher application
            // todo refactor to use the MessagesHelper create configuration
            string publisherConfigurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            m_telemetry = NUnitTelemetryContext.Create();
            m_publisherApplication = UaPubSubApplication.Create(publisherConfigurationFile, m_telemetry);
            Assert.That(m_publisherApplication, Is.Not.Null, "m_publisherApplication should not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_publisherApplication.UaPubSubConfigurator
                .PubSubConfiguration;
            Assert.That(
                m_publisherConfiguration,
                Is.Not.Null,
                "m_publisherConfiguration should not be null");

            // Get first connection
            Assert.That(
                m_publisherConfiguration.Connections.IsEmpty,
                Is.False,
                "m_publisherConfiguration.Connections should not be empty");
            m_firstPublisherConnection = m_publisherApplication.PubSubConnections[0];
            Assert.That(
                m_firstPublisherConnection,
                Is.Not.Null,
                "m_firstPublisherConnection should not be null");

            // Read the first writer group
            Assert.That(
                m_publisherConfiguration.Connections[0].WriterGroups.IsEmpty,
                Is.False,
                "pubSubConfigConnection.WriterGroups should not be empty");
            m_firstWriterGroup = m_publisherConfiguration.Connections[0].WriterGroups[0];
            Assert.That(m_firstWriterGroup, Is.Not.Null, "m_firstWriterGroup should not be null");

            Assert.That(
                m_publisherConfiguration.PublishedDataSets.IsEmpty,
                Is.False,
                "m_publisherConfiguration.PublishedDataSets should not be empty");

            // Create a subscriber application
            string subscriberConfigurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            m_subscriberApplication = UaPubSubApplication.Create(subscriberConfigurationFile, m_telemetry);
            Assert.That(m_subscriberApplication, Is.Not.Null, "m_subscriberApplication should not be null");

            // Get the subscriber configuration
            m_subscriberConfiguration = m_subscriberApplication.UaPubSubConfigurator
                .PubSubConfiguration;
            Assert.That(
                m_subscriberConfiguration,
                Is.Not.Null,
                "m_subscriberConfiguration should not be null");

            // Read the first reader group
            m_firstReaderGroup = m_subscriberConfiguration.Connections[0].ReaderGroups[0];
            Assert.That(m_firstWriterGroup, Is.Not.Null, "m_firstReaderGroup should not be null");

            m_firstDataSetReaderType = GetFirstDataSetReader();
        }

        [Test(
            Description = "Validate dataset message mask with Variant data type;" +
                "Change the Uadp dataset message mask into the [0,63] range that covers all options(properties)"
        )]
        public void ValidateDataSetMessageMask(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            // change network message mask
            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            for (uint dataSetMessageContentMask = 0;
                dataSetMessageContentMask < kMessageContentMask;
                dataSetMessageContentMask++)
            {
                uadpDataSetMessage.SetMessageContentMask(
                    (UadpDataSetMessageContentMask)dataSetMessageContentMask);

                // Assert
                CompareEncodeDecode(uadpDataSetMessage, logger);
            }
        }

        [Test(Description = "Validate TimeStamp")]
        public void ValidateDataSetTimeStamp(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.Timestamp);
            uadpDataSetMessage.Timestamp = DateTime.UtcNow;

            // Assert
            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            CompareEncodeDecode(uadpDataSetMessage, logger);
        }

        [Test(Description = "Validate PicoSeconds")]
        public void ValidatePicoSeconds(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.PicoSeconds);
            uadpDataSetMessage.PicoSeconds = 10;

            // Assert
            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            CompareEncodeDecode(uadpDataSetMessage, logger);
        }

        public static readonly StatusCode[] ValidateStatusCodes = [
            StatusCodes.Good,
            StatusCodes.UncertainDataSubNormal,
            StatusCodes.BadAggregateListMismatch,
            StatusCodes.BadUnknownResponse,
            StatusCodes.Bad,
            StatusCodes.BadAggregateConfigurationRejected,
            StatusCodes.BadAggregateInvalidInputs,
            StatusCodes.BadAlreadyExists
        ];

        [Test(Description = "Validate Status")]
        public void ValidateStatus(
            [Values(
                UadpDataSetMessageContentMask.None,
                UadpDataSetMessageContentMask.Timestamp,
                UadpDataSetMessageContentMask.MajorVersion,
                UadpDataSetMessageContentMask.MinorVersion,
                UadpDataSetMessageContentMask.SequenceNumber,
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion,
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion |
                UadpDataSetMessageContentMask.SequenceNumber
            )]
                UadpDataSetMessageContentMask messageContentMask,
                [ValueSource(nameof(ValidateStatusCodes))] StatusCode statusCode)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(
                DataSetFieldContentMask.None);

            // Act
            uadpDataSetMessage.SetMessageContentMask(
                messageContentMask | UadpDataSetMessageContentMask.Status);
            uadpDataSetMessage.Status = statusCode;

            // Assert
            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            CompareEncodeDecode(uadpDataSetMessage, logger);
        }

        [Test(Description = "Validate MajorVersion and MinorVersion with Equal values")]
        public void ValidateMajorVersionEqMinorVersionEq(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const int versionValue = 2;

            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            uadpDataSetMessage.MetaDataVersion.MajorVersion = versionValue;
            uadpDataSetMessage.MetaDataVersion.MinorVersion = versionValue * 10;

            IServiceMessageContext messageContextEncode = ServiceMessageContext.Create(m_telemetry);
            byte[] bytes;
            using var memoryStream = new MemoryStream();
            using (var encoder = new BinaryEncoder(memoryStream, messageContextEncode, true))
            {
                uadpDataSetMessage.Encode(encoder);
                _ = encoder.Close();
                bytes = ReadBytes(memoryStream);
            }

            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            var uaDataSetMessageDecoded = new UadpDataSetMessage(logger);
            using (var decoder = new BinaryDecoder(bytes, messageContextEncode))
            {
                // Make sure the reader MajorVersion and MinorVersion are the same with the ones on the dataset message
                DataSetReaderDataType reader = CoreUtils.Clone(m_firstDataSetReaderType);
                reader.DataSetMetaData.ConfigurationVersion.MajorVersion = versionValue;
                reader.DataSetMetaData.ConfigurationVersion.MinorVersion = versionValue * 10;

                // workaround
                uaDataSetMessageDecoded.DataSetWriterId = kTestDataSetWriterId;
                uaDataSetMessageDecoded.DecodePossibleDataSetReader(decoder, reader);
            }

            // Assert
            Assert.That(
                uaDataSetMessageDecoded.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.NoError));
            Assert.That(uaDataSetMessageDecoded.IsMetadataMajorVersionChange, Is.False);
            Assert.That(uaDataSetMessageDecoded.DataSet, Is.Not.Null);
            // compare uadpDataSetMessage with uaDataSetMessageDecoded
            CompareUadpDataSetMessages(uadpDataSetMessage, uaDataSetMessageDecoded);
        }

        [Test(Description = "Validate MajorVersion equal and MinorVersion differ")]
        public void ValidateMajorVersionEqMinorVersionDiffer(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const int versionValue = 2;

            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            uadpDataSetMessage.MetaDataVersion.MajorVersion = versionValue;
            uadpDataSetMessage.MetaDataVersion.MinorVersion = versionValue * 10;

            IServiceMessageContext messageContextEncode = ServiceMessageContext.Create(m_telemetry);
            byte[] bytes;
            using var memoryStream = new MemoryStream();
            using var encoder = new BinaryEncoder(memoryStream, messageContextEncode, true);
            uadpDataSetMessage.Encode(encoder);
            _ = encoder.Close();
            bytes = ReadBytes(memoryStream);

            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            var uaDataSetMessageDecoded = new UadpDataSetMessage(logger);
            using (var decoder = new BinaryDecoder(bytes, messageContextEncode))
            {
                // Make sure the reader MajorVersion is same with the ones on the dataset message
                // and MinorVersion differ
                DataSetReaderDataType reader = CoreUtils.Clone(m_firstDataSetReaderType);
                reader.DataSetMetaData.ConfigurationVersion.MajorVersion = uadpDataSetMessage
                    .MetaDataVersion
                    .MajorVersion;
                reader.DataSetMetaData.ConfigurationVersion.MinorVersion =
                    uadpDataSetMessage.MetaDataVersion.MinorVersion + 1;

                // workaround
                uaDataSetMessageDecoded.DataSetWriterId = kTestDataSetWriterId;
                uaDataSetMessageDecoded.DecodePossibleDataSetReader(decoder, reader);
            }

            // Assert
            Assert.That(
                uaDataSetMessageDecoded.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.NoError));
            Assert.That(uaDataSetMessageDecoded.IsMetadataMajorVersionChange, Is.False);
            Assert.That(uaDataSetMessageDecoded.DataSet, Is.Not.Null);
            // compare uadpDataSetMessage with uaDataSetMessageDecoded
            CompareUadpDataSetMessages(uadpDataSetMessage, uaDataSetMessageDecoded);
        }

        [Test(Description = "Validate MajorVersion differ and MinorVersion are equal")]
        public void ValidateMajorVersionDiffMinorVersionEq(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const int versionValue = 2;

            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            uadpDataSetMessage.MetaDataVersion.MajorVersion = versionValue;
            uadpDataSetMessage.MetaDataVersion.MinorVersion = versionValue * 10;

            IServiceMessageContext messageContextEncode = ServiceMessageContext.Create(m_telemetry);
            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            using (var encoder = new BinaryEncoder(memoryStream, messageContextEncode, true))
            {
                uadpDataSetMessage.Encode(encoder);
                _ = encoder.Close();
                bytes = ReadBytes(memoryStream);
            }

            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            var uaDataSetMessageDecoded = new UadpDataSetMessage(logger);
            using (var decoder = new BinaryDecoder(bytes, messageContextEncode))
            {
                // Make sure the reader MajorVersion differ and MinorVersion are equal
                DataSetReaderDataType reader = CoreUtils.Clone(m_firstDataSetReaderType);
                reader.DataSetMetaData.ConfigurationVersion.MajorVersion =
                    uadpDataSetMessage.MetaDataVersion.MajorVersion + 1;
                reader.DataSetMetaData.ConfigurationVersion.MinorVersion = uadpDataSetMessage
                    .MetaDataVersion
                    .MinorVersion;

                // workaround
                uaDataSetMessageDecoded.DataSetWriterId = kTestDataSetWriterId;
                uaDataSetMessageDecoded.DecodePossibleDataSetReader(decoder, reader);
            }

            // Assert
            Assert.That(
                uaDataSetMessageDecoded.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.MetadataMajorVersion));
            Assert.That(uaDataSetMessageDecoded.IsMetadataMajorVersionChange, Is.True);
            Assert.That(uaDataSetMessageDecoded.DataSet, Is.Null);
        }

        [Test(Description = "Validate MajorVersion differ and MinorVersion differ")]
        public void ValidateMajorVersionDiffMinorVersionDiff(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const int versionValue = 2;

            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            uadpDataSetMessage.MetaDataVersion.MajorVersion = versionValue;
            uadpDataSetMessage.MetaDataVersion.MinorVersion = versionValue * 10;

            IServiceMessageContext messageContextEncode = ServiceMessageContext.Create(m_telemetry);
            byte[] bytes;
            using var memoryStream = new MemoryStream();
            using (var encoder = new BinaryEncoder(memoryStream, messageContextEncode, true))
            {
                uadpDataSetMessage.Encode(encoder);
                _ = encoder.Close();
                bytes = ReadBytes(memoryStream);
            }

            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            var uaDataSetMessageDecoded = new UadpDataSetMessage(logger);
            using (var decoder = new BinaryDecoder(bytes, messageContextEncode))
            {
                // Make sure the reader MajorVersion differ and MinorVersion differ
                DataSetReaderDataType reader = CoreUtils.Clone(m_firstDataSetReaderType);
                reader.DataSetMetaData.ConfigurationVersion.MajorVersion =
                    uadpDataSetMessage.MetaDataVersion.MajorVersion + 1;
                reader.DataSetMetaData.ConfigurationVersion.MinorVersion =
                    uadpDataSetMessage.MetaDataVersion.MinorVersion + 1;

                // workaround
                uaDataSetMessageDecoded.DataSetWriterId = kTestDataSetWriterId;
                uaDataSetMessageDecoded.DecodePossibleDataSetReader(decoder, reader);
            }

            // Assert
            Assert.That(
                uaDataSetMessageDecoded.DecodeErrorReason,
                Is.EqualTo(DataSetDecodeErrorReason.MetadataMajorVersion));
            Assert.That(uaDataSetMessageDecoded.IsMetadataMajorVersionChange, Is.True);
            Assert.That(uaDataSetMessageDecoded.DataSet, Is.Null);
        }

        [Test(Description = "Validate SequenceNumber")]
        public void ValidateSequenceNumber(
            [Values(
                DataSetFieldContentMask.None,
                DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
                DataSetFieldContentMask.ServerPicoSeconds,
                DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.ServerTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds | DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.StatusCode,
                DataSetFieldContentMask.ServerPicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.StatusCode
            )]
                DataSetFieldContentMask dataSetFieldContentMask)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.SequenceNumber);
            uadpDataSetMessage.SequenceNumber = 1000;

            // Assert
            ILogger logger = telemetry.CreateLogger<UadpDataSetMessageTests>();
            CompareEncodeDecode(uadpDataSetMessage, logger);
        }

        /// <summary>
        /// Load Variant data type into datasets
        /// </summary>
        private void LoadData()
        {
            Assert.That(m_publisherApplication, Is.Not.Null, "m_publisherApplication should not be null");

            // DataSet 'Simple' fill with data
            var booleanValue = new DataValue(new Variant(true), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("BoolToggle", kNamespaceIndexSimple),
                Attributes.Value,
                booleanValue);
            var scalarInt32XValue = new DataValue(new Variant(100), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32", kNamespaceIndexSimple),
                Attributes.Value,
                scalarInt32XValue);
            var scalarInt32YValue = new DataValue(new Variant(50), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32Fast", kNamespaceIndexSimple),
                Attributes.Value,
                scalarInt32YValue);
            var dateTimeValue = new DataValue(new Variant(DateTime.UtcNow), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(
                new NodeId("DateTime", kNamespaceIndexSimple),
                Attributes.Value,
                dateTimeValue);
        }

        /// <summary>
        /// Get first DataSetReaders from configuration
        /// </summary>
        private DataSetReaderDataType GetFirstDataSetReader()
        {
            // Read the first configured ReaderGroup
            Assert.That(m_firstReaderGroup, Is.Not.Null, "m_firstReaderGroup should not be null");
            Assert.That(
                m_firstReaderGroup.DataSetReaders.IsEmpty,
                Is.False,
                "m_firstReaderGroup.DataSetReaders should not be empty");
            Assert.That(
                m_firstReaderGroup.DataSetReaders[0],
                Is.Not.Null,
                "m_firstReaderGroup.DataSetReaders[0] should not be null");

            return m_firstReaderGroup.DataSetReaders[0];
        }

        /// <summary>
        /// Get first data set message
        /// </summary>
        /// <param name="fieldContentMask"> a DataSetFieldContentMask specifying what type of encoding is chosen for field values
        /// If none of the flags are set, the fields are represented as Variant.
        /// If the RawData flag is set, the fields are represented as RawData and all other bits are ignored.
        /// If one of the bits StatusCode, SourceTimestamp, ServerTimestamp, SourcePicoSeconds, ServerPicoSeconds is set,
        ///    the fields are represented as DataValue.
        /// </param>
        private UadpDataSetMessage GetFirstDataSetMessage(DataSetFieldContentMask fieldContentMask)
        {
            LoadData();

            // set the configurable field content mask to allow only Variant data type
            foreach (DataSetWriterDataType dataSetWriter in m_firstWriterGroup.DataSetWriters)
            {
                // 00 The DataSet fields are encoded as Variant data type
                // The Variant can contain a StatusCode instead of the expected DataType if the status of the field is Bad.
                // The Variant can contain a DataValue with the value and the statusCode if the status of the field is Uncertain.
                dataSetWriter.DataSetFieldContentMask = (uint)fieldContentMask;
            }

            System.Collections.Generic.IList<UaNetworkMessage> networkMessages =
                m_firstPublisherConnection.CreateNetworkMessages(
                    m_firstWriterGroup,
                    new WriterGroupPublishState());
            // filter out the metadata message
            networkMessages = [.. from m in networkMessages where !m.IsMetaDataMessage select m];
            Assert.That(
                networkMessages,
                Is.Not.Null,
                "connection.CreateNetworkMessages shall not return null");
            Assert.That(
                networkMessages,
                Has.Count.EqualTo(1),
                "connection.CreateNetworkMessages shall return only one network message");

            var uaNetworkMessage = networkMessages[0] as UadpNetworkMessage;

            Assert.That(uaNetworkMessage, Is.Not.Null, "networkMessageEncode should not be null");

            // read first dataset message
            UaDataSetMessage[] uadpDataSetMessages = [.. uaNetworkMessage.DataSetMessages];
            Assert.IsNotEmpty(
                uadpDataSetMessages,
                "uadpDataSetMessages collection should not be empty");

            UaDataSetMessage uadpDataSetMessage = uadpDataSetMessages[0];
            Assert.That(uadpDataSetMessage, Is.Not.Null, "uadpDataSetMessage should not be null");

            return uadpDataSetMessage as UadpDataSetMessage;
        }

        /// <summary>
        /// Compare encoded/decoded dataset messages
        /// </summary>
        private void CompareEncodeDecode(UadpDataSetMessage uadpDataSetMessage, ILogger logger)
        {
            IServiceMessageContext messageContextEncode = ServiceMessageContext.Create(m_telemetry);
            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            using (var encoder = new BinaryEncoder(memoryStream, messageContextEncode, true))
            {
                uadpDataSetMessage.Encode(encoder);
                _ = encoder.Close();
                bytes = ReadBytes(memoryStream);
            }

            var uaDataSetMessageDecoded = new UadpDataSetMessage(logger);
            using (var decoder = new BinaryDecoder(bytes, messageContextEncode))
            {
                // workaround
                uaDataSetMessageDecoded.DataSetWriterId = kTestDataSetWriterId;
                uaDataSetMessageDecoded.DecodePossibleDataSetReader(
                    decoder,
                    m_firstDataSetReaderType);
            }

            // compare uadpDataSetMessage with uaDataSetMessageDecoded
            CompareUadpDataSetMessages(uadpDataSetMessage, uaDataSetMessageDecoded);
        }

        /// <summary>
        /// Compare dataset messages options
        /// </summary>
        private static void CompareUadpDataSetMessages(
            UadpDataSetMessage uadpDataSetMessageEncode,
            UadpDataSetMessage uadpDataSetMessageDecoded)
        {
            DataSet dataSetDecoded = uadpDataSetMessageDecoded.DataSet;
            UadpDataSetMessageContentMask dataSetMessageContentMask =
                uadpDataSetMessageEncode.DataSetMessageContentMask;

            Assert.That(
                uadpDataSetMessageDecoded.DataSetFlags1,
                Is.EqualTo(uadpDataSetMessageEncode.DataSetFlags1),
                "DataSetMessages DataSetFlags1 do not match:");
            Assert.That(
                uadpDataSetMessageDecoded.DataSetFlags2,
                Is.EqualTo(uadpDataSetMessageEncode.DataSetFlags2),
                "DataSetMessages DataSetFlags2 do not match:");

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.Timestamp) ==
                UadpDataSetMessageContentMask.Timestamp)
            {
                Assert.That(
                    uadpDataSetMessageDecoded.Timestamp,
                    Is.EqualTo(uadpDataSetMessageEncode.Timestamp),
                    "DataSetMessages TimeStamp do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.PicoSeconds) ==
                UadpDataSetMessageContentMask.PicoSeconds)
            {
                Assert.That(
                    uadpDataSetMessageDecoded.PicoSeconds,
                    Is.EqualTo(uadpDataSetMessageEncode.PicoSeconds),
                    "DataSetMessages PicoSeconds do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.Status) ==
                UadpDataSetMessageContentMask.Status)
            {
                Assert.That(
                    uadpDataSetMessageDecoded.Status,
                    Is.EqualTo(uadpDataSetMessageEncode.Status),
                    "DataSetMessages Status do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.MajorVersion) ==
                UadpDataSetMessageContentMask.MajorVersion)
            {
                Assert.That(
                    uadpDataSetMessageDecoded.MetaDataVersion.MajorVersion,
                    Is.EqualTo(uadpDataSetMessageEncode.MetaDataVersion.MajorVersion),
                    "DataSetMessages ConfigurationMajorVersion do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.MinorVersion) ==
                UadpDataSetMessageContentMask.MinorVersion)
            {
                Assert.That(
                    uadpDataSetMessageDecoded.MetaDataVersion.MinorVersion,
                    Is.EqualTo(uadpDataSetMessageEncode.MetaDataVersion.MinorVersion),
                    "DataSetMessages ConfigurationMajorVersion do not match:");
            }

            // check also the payload data
            Assert.That(
                dataSetDecoded.Fields,
                Has.Length.EqualTo(uadpDataSetMessageEncode.DataSet.Fields.Length),
                "DataSetMessages DataSet fields size do not match:");

            for (int index = 0; index < uadpDataSetMessageEncode.DataSet.Fields.Length; index++)
            {
                Field dataSetFieldEncoded = uadpDataSetMessageEncode.DataSet.Fields[index];
                Field dataSetFieldDecoded = dataSetDecoded.Fields[index];

                Assert.That(dataSetFieldEncoded.Value, Is.Not.Null, "DataSetFieldEncoded.Value is null");
                Assert.That(dataSetFieldDecoded.Value, Is.Not.Null, "DataSetFieldDecoded.Value is null");
#pragma warning disable CS0618 // Type or member is obsolete
                object encodedValue = dataSetFieldEncoded.Value.Value;
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
                object decodedValue = dataSetFieldDecoded.Value.Value;
#pragma warning restore CS0618 // Type or member is obsolete

                Assert.That(
                    decodedValue,
                    Is.EqualTo(encodedValue),
                    $"DataSetMessages Field.Value does not match value field at position: {index} {encodedValue}|{decodedValue}");
            }
        }

        /// <summary>
        /// Read All bytes from a given stream
        /// </summary>
        private static byte[] ReadBytes(MemoryStream stream)
        {
            stream.Position = 0;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
