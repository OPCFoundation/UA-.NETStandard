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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Coverage for <see cref="PubSubConfigurationValidator"/>: at
    /// least one positive and one negative test per validation rule
    /// in Part 14 §9.1.4 and §6.2.5, each carrying a
    /// <see cref="TestSpecAttribute"/> referencing the clause it
    /// validates.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.4", Summary = "PubSub configuration object model")]
    [TestSpec("6.2.5", Summary = "PubSub security")]
    public class PubSubConfigurationValidatorTests
    {
        private static readonly string[] s_allProfiles =
        {
            Profiles.PubSubUdpUadpTransport,
            Profiles.PubSubMqttUadpTransport,
            Profiles.PubSubMqttJsonTransport
        };

        private static PubSubConfigurationValidator NewValidator()
        {
            return new PubSubConfigurationValidator(s_allProfiles);
        }

        private static PubSubConnectionDataType NewUdpConnection(string name = "Conn")
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
        }

        private static PubSubConnectionDataType NewMqttConnection(string url = "mqtt://broker:1883")
        {
            return new PubSubConnectionDataType
            {
                Name = "Conn",
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = url })
            };
        }

        private static WriterGroupDataType NewWriterGroup(
            ushort id = 1,
            double publishingInterval = 1000.0)
        {
            return new WriterGroupDataType
            {
                Name = "WG",
                WriterGroupId = id,
                PublishingInterval = publishingInterval,
                SecurityMode = MessageSecurityMode.None
            };
        }

        private static DataSetWriterDataType NewDataSetWriter(
            ushort id = 1,
            string dataSetName = "DS1",
            uint keyFrameCount = 1)
        {
            return new DataSetWriterDataType
            {
                Name = "Writer",
                DataSetWriterId = id,
                DataSetName = dataSetName,
                KeyFrameCount = keyFrameCount
            };
        }

        private static DataSetReaderDataType NewDataSetReader(ushort writerId = 1)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader",
                DataSetWriterId = writerId,
                MessageReceiveTimeout = 1000.0,
                SecurityMode = MessageSecurityMode.None,
                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
            };
        }

        private static PubSubConfigurationDataType NewMinimalValidConfig()
        {
            return new PubSubConfigurationDataType
            {
                Enabled = true,
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                    new[] { new PublishedDataSetDataType { Name = "DS1" } }),
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn",
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                            WriterGroups = new ArrayOf<WriterGroupDataType>(
                                new[]
                                {
                                    new WriterGroupDataType
                                    {
                                        Name = "WG",
                                        WriterGroupId = 1,
                                        PublishingInterval = 1000.0,
                                        SecurityMode = MessageSecurityMode.None,
                                        DataSetWriters = new ArrayOf<DataSetWriterDataType>(
                                            new[] { NewDataSetWriter() })
                                    }
                                }),
                            ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                                new[]
                                {
                                    new ReaderGroupDataType
                                    {
                                        Name = "RG",
                                        SecurityMode = MessageSecurityMode.None,
                                        DataSetReaders = new ArrayOf<DataSetReaderDataType>(
                                            new[] { NewDataSetReader() })
                                    }
                                })
                        }
                    })
            };
        }

        [Test]
        public void Validate_NullConfiguration_Throws()
        {
            PubSubConfigurationValidator validator = NewValidator();
            Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        }

        [Test]
        public void Constructor_NullProfiles_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationValidator(null!));
        }

        [Test]
        public void Validate_MinimalValidConfig_IsValid()
        {
            PubSubConfigurationValidationResult result = NewValidator()
                .Validate(NewMinimalValidConfig());
            Assert.That(result.IsValid, Is.True, () => string.Join(
                "; ",
                result.Issues.Select(static i => $"{i.Code} {i.Path}: {i.Message}")));
        }

        [Test]
        [TestSpec("9.1.4.1", Summary = "Connection.Name uniqueness")]
        public void Validate_DuplicateConnectionName_EmitsError()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        NewUdpConnection("Same"),
                        NewUdpConnection("Same")
                    })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0002"));
        }

        [Test]
        [TestSpec("9.1.4.1", Summary = "Connection.Name presence")]
        public void Validate_MissingConnectionName_EmitsError()
        {
            var conn = NewUdpConnection(string.Empty);
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0001"));
        }

        [Test]
        [TestSpec("9.1.4.1", Summary = "Connection.TransportProfileUri presence")]
        public void Validate_MissingTransportProfile_EmitsError()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.TransportProfileUri = string.Empty;
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0003"));
        }

        [Test]
        [TestSpec("9.1.4.1", Summary = "Connection.TransportProfileUri must be registered")]
        public void Validate_UnregisteredTransportProfile_EmitsError()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.TransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-unknown";
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0004"));
        }

        [Test]
        [TestSpec("9.1.4.1", Summary = "Connection.Address presence")]
        public void Validate_MissingAddress_EmitsError()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.Address = ExtensionObject.Null;
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0005"));
        }

        [Test]
        [TestSpec("9.1.5.2", Summary = "UDP/UADP address scheme")]
        public void Validate_UdpProfileWithWrongScheme_EmitsError()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.Address = new ExtensionObject(
                new NetworkAddressUrlDataType { Url = "mqtt://broker:1883" });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0007"));
        }

        [Test]
        [TestSpec("9.1.5.3", Summary = "MQTT address scheme")]
        public void Validate_MqttProfileWithMqttsScheme_IsAllowed()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.TransportProfileUri = Profiles.PubSubMqttJsonTransport;
            conn.Address = new ExtensionObject(
                new NetworkAddressUrlDataType { Url = "mqtts://broker:8883" });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0007"));
        }

        [Test]
        [TestSpec("9.1.5.2", Summary = "DatagramConnectionTransport2 v2-only fields surface Info")]
        public void Validate_DatagramV2FieldsInUse_EmitsInfo()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.TransportSettings = new ExtensionObject(
                new DatagramConnectionTransport2DataType
                {
                    DiscoveryAnnounceRate = 5,
                    QosCategory = "default"
                });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            PubSubConfigurationIssue? issue = result.Issues.FirstOrDefault(
                static i => i.Code == "PSC0008");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity, Is.EqualTo(PubSubConfigurationIssueSeverity.Info));
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        [TestSpec("9.1.6", Summary = "WriterGroupId must be non-zero")]
        public void Validate_WriterGroupIdZero_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].WriterGroupId = 0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0010"));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "WriterGroupId uniqueness within connection")]
        public void Validate_DuplicateWriterGroupId_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups = new ArrayOf<WriterGroupDataType>(
                new[]
                {
                    NewWriterGroup(1),
                    NewWriterGroup(1)
                });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0011"));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "PublishingInterval must be > 0")]
        public void Validate_PublishingIntervalZero_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].PublishingInterval = 0.0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0012"));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "KeepAliveTime >= PublishingInterval")]
        public void Validate_KeepAliveBelowPublishingInterval_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].PublishingInterval = 1000.0;
            config.Connections[0].WriterGroups[0].KeepAliveTime = 500.0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0013"));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "DataSetWriterId must be non-zero")]
        public void Validate_DataSetWriterIdZero_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetWriterId = 0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0020"));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "DataSetWriterId uniqueness within WriterGroup")]
        public void Validate_DuplicateDataSetWriterId_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].DataSetWriters = new ArrayOf<DataSetWriterDataType>(
                new[]
                {
                    NewDataSetWriter(1),
                    NewDataSetWriter(1)
                });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0021"));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "DataSetWriter.DataSetName must reference existing PublishedDataSet")]
        public void Validate_DataSetNameUnresolved_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetName = "DSDoesNotExist";
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0023"));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "DataSetWriter.DataSetName must not be empty")]
        public void Validate_DataSetNameMissing_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetName = string.Empty;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0022"));
        }

        [Test]
        [TestSpec("9.1.7", Summary = "KeyFrameCount zero emits warning")]
        public void Validate_KeyFrameCountZero_EmitsWarning()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].DataSetWriters[0].KeyFrameCount = 0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            PubSubConfigurationIssue? issue = result.Issues.FirstOrDefault(
                static i => i.Code == "PSC0024");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity, Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        [TestSpec("9.1.8", Summary = "ReaderGroup.Name presence")]
        public void Validate_MissingReaderGroupName_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].ReaderGroups[0].Name = string.Empty;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0030"));
        }

        [Test]
        [TestSpec("9.1.8", Summary = "ReaderGroup name uniqueness (defensive warning)")]
        public void Validate_DuplicateReaderGroupName_EmitsWarning()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                new[]
                {
                    new ReaderGroupDataType { Name = "RG", SecurityMode = MessageSecurityMode.None },
                    new ReaderGroupDataType { Name = "RG", SecurityMode = MessageSecurityMode.None }
                });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            PubSubConfigurationIssue? issue = result.Issues.FirstOrDefault(
                static i => i.Code == "PSC0031");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity, Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
        }

        [Test]
        [TestSpec("9.1.9", Summary = "DataSetReader.DataSetWriterId must be non-zero")]
        public void Validate_ReaderDataSetWriterIdZero_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].ReaderGroups[0].DataSetReaders[0].DataSetWriterId = 0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0040"));
        }

        [Test]
        [TestSpec("9.1.9", Summary = "DataSetReader.MessageReceiveTimeout must be > 0")]
        public void Validate_MessageReceiveTimeoutZero_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].ReaderGroups[0].DataSetReaders[0].MessageReceiveTimeout = 0.0;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0041"));
        }

        [Test]
        [TestSpec("9.1.9", Summary = "DataSetReader.SubscribedDataSet presence")]
        public void Validate_MissingSubscribedDataSet_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].ReaderGroups[0].DataSetReaders[0].SubscribedDataSet =
                ExtensionObject.Null;
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0042"));
        }

        [Test]
        [TestSpec("6.2.5.4", Summary = "SecurityMode != None requires SecurityGroupId")]
        public void Validate_SignWithoutSecurityGroup_EmitsError()
        {
            var config = NewMinimalValidConfig();
            WriterGroupDataType wg = config.Connections[0].WriterGroups[0];
            wg.SecurityMode = MessageSecurityMode.Sign;
            wg.SecurityKeyServices = new ArrayOf<EndpointDescription>(
                new[] { new EndpointDescription { EndpointUrl = "opc.tcp://sks" } });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0050"));
        }

        [Test]
        [TestSpec("6.2.5.4", Summary = "SecurityMode != None requires at least one SKS endpoint")]
        public void Validate_SignAndEncryptWithoutSks_EmitsError()
        {
            var config = NewMinimalValidConfig();
            WriterGroupDataType wg = config.Connections[0].WriterGroups[0];
            wg.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            wg.SecurityGroupId = "Group1";
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0051"));
        }

        [Test]
        [TestSpec("6.2.5.4", Summary = "SecurityMode == None forbids SecurityGroupId")]
        public void Validate_NoneWithSecurityGroup_EmitsError()
        {
            var config = NewMinimalValidConfig();
            WriterGroupDataType wg = config.Connections[0].WriterGroups[0];
            wg.SecurityMode = MessageSecurityMode.None;
            wg.SecurityGroupId = "Group1";
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0052"));
        }

        [Test]
        [TestSpec("6.2.5.4", Summary = "SecurityMode == None forbids SecurityKeyServices")]
        public void Validate_NoneWithSks_EmitsError()
        {
            var config = NewMinimalValidConfig();
            WriterGroupDataType wg = config.Connections[0].WriterGroups[0];
            wg.SecurityMode = MessageSecurityMode.None;
            wg.SecurityKeyServices = new ArrayOf<EndpointDescription>(
                new[] { new EndpointDescription { EndpointUrl = "opc.tcp://sks" } });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0053"));
        }

        [Test]
        [TestSpec("6.2.5", Part = 14, Summary = "SecurityMode None emits warning")]
        public void ValidateSecurityModeNoneEmitsWarning()
        {
            PubSubConfigurationValidationResult result = NewValidator().Validate(NewMinimalValidConfig());

            PubSubConfigurationIssue? issue = result.Issues.FirstOrDefault(
                static i => i.Code == "PSC0054");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity, Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        [TestSpec("6.2.5", Part = 14, Summary = "SecurityMode None warning can be suppressed")]
        public void ValidateSecurityModeNoneWarningCanBeSuppressed()
        {
            var validator = new PubSubConfigurationValidator(s_allProfiles)
            {
                SuppressInsecureSecurityModeWarnings = true
            };

            PubSubConfigurationValidationResult result = validator.Validate(NewMinimalValidConfig());

            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0054"));
        }

        [Test]
        [TestSpec("6.2.5", Part = 14, Summary = "SecurityMode Invalid (unset) emits warning")]
        public void ValidateSecurityModeInvalidEmitsWarning()
        {
            var config = NewMinimalValidConfig();
            config.Connections[0].WriterGroups[0].SecurityMode = MessageSecurityMode.Invalid;

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            PubSubConfigurationIssue? issue = result.Issues.FirstOrDefault(
                static i => i.Code == "PSC0055");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity, Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        [TestSpec("6.2.5", Part = 14, Summary = "Plaintext MQTT without message security emits warning")]
        public void ValidatePlaintextMqttWithoutMessageSecurityEmitsWarning()
        {
            PubSubConnectionDataType connection = NewMqttConnection();
            connection.WriterGroups = new ArrayOf<WriterGroupDataType>(
                new[] { NewWriterGroup() });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { connection })
            };

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            PubSubConfigurationIssue? issue = result.Issues.FirstOrDefault(
                static i => i.Code == "PSC0056");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity, Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        [TestSpec("6.2.5", Part = 14, Summary = "MQTTS without message security avoids plaintext warning")]
        public void ValidateMqttsWithoutMessageSecurityDoesNotEmitPlaintextWarning()
        {
            PubSubConnectionDataType connection = NewMqttConnection("mqtts://broker:8883");
            connection.WriterGroups = new ArrayOf<WriterGroupDataType>(
                new[] { NewWriterGroup() });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { connection })
            };

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0056"));
        }

        [Test]
        [TestSpec("6.2.5", Part = 14, Summary = "Plaintext MQTT with message security avoids warning")]
        public void ValidatePlaintextMqttWithMessageSecurityDoesNotEmitPlaintextWarning()
        {
            PubSubConnectionDataType connection = NewMqttConnection();
            WriterGroupDataType writerGroup = NewWriterGroup();
            writerGroup.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            writerGroup.SecurityGroupId = "Group1";
            writerGroup.SecurityKeyServices = new ArrayOf<EndpointDescription>(
                new[] { new EndpointDescription { EndpointUrl = "opc.tcp://sks" } });
            connection.WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { writerGroup });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { connection })
            };

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0056"));
        }

        [Test]
        [TestSpec("6.2.5.4", Summary = "Sign with both SecurityGroupId and SKS is valid")]
        public void Validate_SignWithGroupAndSks_NoSecurityIssue()
        {
            var config = NewMinimalValidConfig();
            WriterGroupDataType wg = config.Connections[0].WriterGroups[0];
            wg.SecurityMode = MessageSecurityMode.Sign;
            wg.SecurityGroupId = "Group1";
            wg.SecurityKeyServices = new ArrayOf<EndpointDescription>(
                new[] { new EndpointDescription { EndpointUrl = "opc.tcp://sks" } });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(
                    static i =>
                        i.Code == "PSC0050"
                        || i.Code == "PSC0051"
                        || i.Code == "PSC0052"
                        || i.Code == "PSC0053"));
        }

        [Test]
        [TestSpec("9.1.4", Summary = "PublishedDataSet name uniqueness")]
        public void Validate_DuplicatePublishedDataSetName_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                new[]
                {
                    new PublishedDataSetDataType { Name = "DS1" },
                    new PublishedDataSetDataType { Name = "DS1" }
                });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0061"));
        }

        [Test]
        [TestSpec("9.1.4", Summary = "PublishedDataSet name presence")]
        public void Validate_MissingPublishedDataSetName_EmitsError()
        {
            var config = NewMinimalValidConfig();
            config.PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                new[] { new PublishedDataSetDataType { Name = string.Empty } });
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0060"));
        }

        [Test]
        public void Validate_EmptyConfig_NoIssues()
        {
            PubSubConfigurationValidationResult result = NewValidator()
                .Validate(new PubSubConfigurationDataType());
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }

        [Test]
        public void Validate_NonNetworkAddressUrl_EmitsWarning()
        {
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.Address = new ExtensionObject(new NetworkAddressDataType());
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = NewValidator().Validate(config);
            Assert.That(
                result.Issues,
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0006"));
        }

        [Test]
        public void Validate_NoRegisteredProfiles_SkipsTransportProfileCheck()
        {
            var validator = new PubSubConfigurationValidator(Array.Empty<string>());
            PubSubConnectionDataType conn = NewUdpConnection();
            conn.TransportProfileUri = "http://example.com/unknown";
            conn.Address = new ExtensionObject(
                new NetworkAddressUrlDataType { Url = "opc.udp://localhost:4840" });
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { conn })
            };
            PubSubConfigurationValidationResult result = validator.Validate(config);
            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0004"));
        }

        [Test]
        [TestSpec("7.2.4.5.11",
            Summary = "RawData encoding requires MaxStringLength / ArrayDimensions")]
        public void Validate_RawDataWithMaxStringLength_NoPaddingWarning()
        {
            var config = NewMinimalValidConfig();
            var publishedDataSet = new PublishedDataSetDataType
            {
                Name = "DS1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = new ArrayOf<FieldMetaData>(new[]
                    {
                        new FieldMetaData
                        {
                            Name = "stringField",
                            BuiltInType = (byte)BuiltInType.String,
                            ValueRank = ValueRanks.Scalar,
                            MaxStringLength = 10
                        }
                    })
                }
            };
            config.PublishedDataSets =
                new ArrayOf<PublishedDataSetDataType>(new[] { publishedDataSet });
            DataSetWriterDataType writer =
                config.Connections[0].WriterGroups[0].DataSetWriters[0];
            writer.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0025"));
        }

        [Test]
        [TestSpec("7.2.4.5.11",
            Summary = "RawData String field without MaxStringLength must warn")]
        public void Validate_RawDataStringFieldWithoutMaxStringLength_EmitsWarning()
        {
            var config = NewMinimalValidConfig();
            var publishedDataSet = new PublishedDataSetDataType
            {
                Name = "DS1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = new ArrayOf<FieldMetaData>(new[]
                    {
                        new FieldMetaData
                        {
                            Name = "stringField",
                            BuiltInType = (byte)BuiltInType.String,
                            ValueRank = ValueRanks.Scalar,
                            MaxStringLength = 0
                        }
                    })
                }
            };
            config.PublishedDataSets =
                new ArrayOf<PublishedDataSetDataType>(new[] { publishedDataSet });
            DataSetWriterDataType writer =
                config.Connections[0].WriterGroups[0].DataSetWriters[0];
            writer.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            PubSubConfigurationIssue? issue = result.Issues
                .FirstOrDefault(static i => i.Code == "PSC0025");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity,
                Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
            Assert.That(issue.SpecClause, Is.EqualTo("7.2.4.5.11"));
            Assert.That(issue.Message, Does.Contain("RawData"));
            Assert.That(issue.Message, Does.Contain("stringField"));
        }

        [Test]
        [TestSpec("7.2.4.5.11",
            Summary = "RawData array field without ArrayDimensions must warn")]
        public void Validate_RawDataArrayFieldWithoutArrayDimensions_EmitsWarning()
        {
            var config = NewMinimalValidConfig();
            var publishedDataSet = new PublishedDataSetDataType
            {
                Name = "DS1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = new ArrayOf<FieldMetaData>(new[]
                    {
                        new FieldMetaData
                        {
                            Name = "intArrayField",
                            BuiltInType = (byte)BuiltInType.Int32,
                            ValueRank = ValueRanks.OneDimension
                        }
                    })
                }
            };
            config.PublishedDataSets =
                new ArrayOf<PublishedDataSetDataType>(new[] { publishedDataSet });
            DataSetWriterDataType writer =
                config.Connections[0].WriterGroups[0].DataSetWriters[0];
            writer.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            PubSubConfigurationIssue? issue = result.Issues
                .FirstOrDefault(static i => i.Code == "PSC0025");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Severity,
                Is.EqualTo(PubSubConfigurationIssueSeverity.Warning));
            Assert.That(issue.SpecClause, Is.EqualTo("7.2.4.5.11"));
            Assert.That(issue.Message, Does.Contain("intArrayField"));
        }

        [Test]
        [TestSpec("7.2.4.5.11",
            Summary = "Non-RawData encoding suppresses PSC0025")]
        public void Validate_VariantEncodingWithoutBounds_NoPaddingWarning()
        {
            var config = NewMinimalValidConfig();
            var publishedDataSet = new PublishedDataSetDataType
            {
                Name = "DS1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = new ArrayOf<FieldMetaData>(new[]
                    {
                        new FieldMetaData
                        {
                            Name = "stringField",
                            BuiltInType = (byte)BuiltInType.String,
                            ValueRank = ValueRanks.Scalar,
                            MaxStringLength = 0
                        }
                    })
                }
            };
            config.PublishedDataSets =
                new ArrayOf<PublishedDataSetDataType>(new[] { publishedDataSet });
            DataSetWriterDataType writer =
                config.Connections[0].WriterGroups[0].DataSetWriters[0];
            writer.DataSetFieldContentMask = 0;

            PubSubConfigurationValidationResult result = NewValidator().Validate(config);

            Assert.That(
                result.Issues,
                Has.None.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0025"));
        }
    }
}
