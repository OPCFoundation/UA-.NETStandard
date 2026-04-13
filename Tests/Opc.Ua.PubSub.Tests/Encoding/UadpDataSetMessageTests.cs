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
using System.Collections.Generic;
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
            var memoryStream = new MemoryStream();
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
            var memoryStream = new MemoryStream();
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

    [TestFixture]
    public class UadpDataSetMessageAdditionalTests
    {
        private const byte FieldTypeBitMask = 0x06;

        private static readonly ConfigurationVersionDataType DefaultMetaDataVersion =
            new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 1 };

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

            int fieldType = ((byte)message.DataSetFlags1 & FieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(0), "Expected Variant (0)");
        }

        [Test]
        public void SetFieldContentMaskRawDataSetsRawDataEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            int fieldType = ((byte)message.DataSetFlags1 & FieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(1), "Expected RawData (1)");
        }

        [Test]
        public void SetFieldContentMaskStatusCodeSetsDataValueEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.StatusCode);

            int fieldType = ((byte)message.DataSetFlags1 & FieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(2), "Expected DataValue (2)");
        }

        [Test]
        public void SetFieldContentMaskSourceTimestampSetsDataValueEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.SourceTimestamp);

            int fieldType = ((byte)message.DataSetFlags1 & FieldTypeBitMask) >> 1;
            Assert.That(fieldType, Is.EqualTo(2), "Expected DataValue (2)");
        }

        [Test]
        public void SetFieldContentMaskServerTimestampSetsDataValueEncoding()
        {
            var message = new UadpDataSetMessage();
            message.SetFieldContentMask(DataSetFieldContentMask.ServerTimestamp);

            int fieldType = ((byte)message.DataSetFlags1 & FieldTypeBitMask) >> 1;
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

            DataSetFieldContentMask fieldMask = DataSetFieldContentMask.StatusCode;
            UadpDataSetMessageContentMask msgMask =
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

            DataSetFieldContentMask fieldMask = DataSetFieldContentMask.RawData;
            UadpDataSetMessageContentMask msgMask =
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

            UadpDataSetMessageContentMask msgMask =
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
            message.MetaDataVersion = DefaultMetaDataVersion;
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
            message.MetaDataVersion = DefaultMetaDataVersion;
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
            return new DataSet("TestDataSet") { Fields = fieldList.ToArray() };
        }

        private byte[] EncodeMessage(
            DataSet dataSet,
            DataSetFieldContentMask fieldMask,
            UadpDataSetMessageContentMask msgMask)
        {
            var message = new UadpDataSetMessage(dataSet);
            message.SetFieldContentMask(fieldMask);
            message.SetMessageContentMask(msgMask);
            message.MetaDataVersion = DefaultMetaDataVersion;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            using (var stream = new MemoryStream())
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                message.Encode(encoder);
                return stream.ToArray();
            }
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
            decodedMessage.MetaDataVersion = DefaultMetaDataVersion;

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
            var metaData = new DataSetMetaDataType();
            metaData.ConfigurationVersion = DefaultMetaDataVersion;
            metaData.Fields = dataSet.Fields
                .Select(f => f.FieldMetaData)
                .ToArray();

            var reader = new DataSetReaderDataType
            {
                DataSetMetaData = metaData,
                MessageSettings = new ExtensionObject(
                    new UadpDataSetReaderMessageDataType())
            };
            return reader;
        }
    }
}