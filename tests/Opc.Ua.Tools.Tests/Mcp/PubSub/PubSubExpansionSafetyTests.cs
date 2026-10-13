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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Verifies safe artifact projection over all credential-bearing component levels and exact bounds.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionSafetyTests
    {
        [Test]
        public async Task ReaderArtifactsStripPrivateBagsButPreserveNativeMetadataAndTargetsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(fixture, "Subscriber");
            PubSubConnectionDataType connection = configuration.Connections[0];
            ReaderGroupDataType group = connection.ReaderGroups[0];
            DataSetReaderDataType reader = group.DataSetReaders[0];
            group.GroupProperties = Properties();
            group.SecurityKeyServices = [new EndpointDescription { EndpointUrl = "opc.tcp://issuer.test:4840" }];
            reader.DataSetReaderProperties = Properties();
            reader.SecurityKeyServices = [new EndpointDescription { EndpointUrl = "opc.tcp://issuer.test:4840" }];
            reader.DataSetMetaData.Fields[0].Properties = Properties();
            reader.SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType
            {
                TargetVariables = [new FieldTargetDataType { TargetNodeId = fixture.Remote.Id("private-target") }]
            });
            uint version = reader.DataSetMetaData.ConfigurationVersion.MajorVersion;

            int removed = McpPubSubConfigurationSafety.Sanitize(configuration, new McpPubSubOptions());

            Assert.That(removed, Is.EqualTo(6));
            Assert.That(group.GroupProperties, Is.Empty);
            Assert.That(group.SecurityKeyServices, Is.Empty);
            Assert.That(reader.DataSetReaderProperties, Is.Empty);
            Assert.That(reader.SecurityKeyServices, Is.Empty);
            Assert.That(reader.DataSetMetaData.Fields[0].Properties, Is.Empty);
            Assert.That(reader.SubscribedDataSet.IsNull, Is.True);
            Assert.That(reader.PublisherId.TryGetValue(out ushort publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(42));
            Assert.That(reader.DataSetMetaData.ConfigurationVersion.MajorVersion, Is.EqualTo(version));
            Assert.That(reader.DataSetMetaData.Fields.ToList().Select(field => field.Name),
                Is.EqualTo(s_fieldNames));
            Assert.That(reader.MessageSettings.TryGetValue(out UadpDataSetReaderMessageDataType? _), Is.True);
        }

        [Test]
        public async Task PublishedSourceSubstitutesPropertiesAndOpaqueSettingsCannotEnterAnArtifactAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(fixture, "Publisher");
            PublishedDataSetDataType dataSet = configuration.PublishedDataSets[0];
            dataSet.ExtensionFields = Properties();
            dataSet.DataSetMetaData.Fields[0].Properties = Properties();
            dataSet.DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
            {
                PublishedData =
                [
                    new PublishedVariableDataType
                    {
                        PublishedVariable = fixture.Remote.Id("value"), AttributeId = Attributes.Value,
                        SubstituteValue = Variant.From("private-substitute"),
                        MetaDataProperties = [new QualifiedName("private-metadata")]
                    }
                ]
            });
            PubSubConnectionDataType connection = configuration.Connections[0];
            connection.ConnectionProperties = Properties();
            connection.TransportSettings = new ExtensionObject(new DatagramConnectionTransport2DataType());
            connection.WriterGroups[0].DataSetWriters[0].DataSetWriterProperties = Properties();

            int removed = McpPubSubConfigurationSafety.Sanitize(configuration, new McpPubSubOptions());

            Assert.That(removed, Is.EqualTo(7));
            Assert.That(dataSet.ExtensionFields, Is.Empty);
            Assert.That(dataSet.DataSetMetaData.Fields[0].Properties, Is.Empty);
            Assert.That(dataSet.DataSetSource.TryGetValue(out PublishedDataItemsDataType? source), Is.True);
            Assert.That(source!.PublishedData[0].SubstituteValue.IsNull, Is.True);
            Assert.That(source.PublishedData[0].MetaDataProperties, Is.Empty);
            Assert.That(source.PublishedData[0].PublishedVariable, Is.EqualTo(fixture.Remote.Id("value")));
            Assert.That(connection.ConnectionProperties, Is.Empty);
            Assert.That(connection.TransportSettings.IsNull, Is.True);
            Assert.That(connection.WriterGroups[0].DataSetWriters[0].DataSetWriterProperties, Is.Empty);
        }

        [TestCase("writer-group")]
        [TestCase("writer")]
        [TestCase("reader")]
        public async Task BrokerProjectionRetainsDeliveryAndTopicsButRemovesAuthenticationAndResourceAsync(string kind)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(
                fixture, kind == "reader" ? "Subscriber" : "Publisher");
            PubSubConnectionDataType connection = configuration.Connections[0];
            var address = new NetworkAddressUrlDataType
            {
                Url = new UriBuilder("mqtt://broker.test:1883")
                {
                    UserName = "test-user",
                    Password = "private-marker"
                }.Uri.AbsoluteUri
            };
            connection.Address = new ExtensionObject(address);
            if (kind == "writer-group")
            {
                connection.WriterGroups[0].TransportSettings =
                    new ExtensionObject(new BrokerWriterGroupTransportDataType
                    {
                        QueueName = "data",
                        AuthenticationProfileUri = "urn:private:profile",
                        ResourceUri = "urn:private",
                        RequestedDeliveryGuarantee = BrokerTransportQualityOfService.ExactlyOnce
                    });
            }
            else if (kind == "writer")
            {
                connection.WriterGroups[0].DataSetWriters[0].TransportSettings =
                    new ExtensionObject(new BrokerDataSetWriterTransportDataType
                    {
                        QueueName = "data",
                        MetaDataQueueName = "metadata",
                        MetaDataUpdateTime = 700,
                        AuthenticationProfileUri = "urn:private:profile",
                        ResourceUri = "urn:private",
                        RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
                    });
            }
            else
            {
                connection.ReaderGroups[0].DataSetReaders[0].TransportSettings =
                    new ExtensionObject(new BrokerDataSetReaderTransportDataType
                    {
                        QueueName = "data",
                        MetaDataQueueName = "metadata",
                        AuthenticationProfileUri = "urn:private:profile",
                        ResourceUri = "urn:private",
                        RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
                    });
            }

            int removed = McpPubSubConfigurationSafety.Sanitize(configuration, new McpPubSubOptions());

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(new Uri(address.Url!).UserInfo, Is.Empty);
            Assert.That(new Uri(address.Url!).Host, Is.EqualTo("broker.test"));
            if (kind == "writer-group")
            {
                Assert.That(connection.WriterGroups[0].TransportSettings.TryGetValue(
                    out BrokerWriterGroupTransportDataType? settings), Is.True);
                Assert.That(settings!.QueueName, Is.EqualTo("data"));
                Assert.That(settings.RequestedDeliveryGuarantee,
                    Is.EqualTo(BrokerTransportQualityOfService.ExactlyOnce));
                Assert.That(settings.AuthenticationProfileUri, Is.Null.Or.Empty);
                Assert.That(settings.ResourceUri, Is.Null.Or.Empty);
            }
            else if (kind == "writer")
            {
                Assert.That(connection.WriterGroups[0].DataSetWriters[0].TransportSettings.TryGetValue(
                    out BrokerDataSetWriterTransportDataType? settings), Is.True);
                Assert.That(settings!.QueueName, Is.EqualTo("data"));
                Assert.That(settings.MetaDataQueueName, Is.EqualTo("metadata"));
                Assert.That(settings.MetaDataUpdateTime, Is.EqualTo(700));
                Assert.That(settings.RequestedDeliveryGuarantee,
                    Is.EqualTo(BrokerTransportQualityOfService.AtLeastOnce));
                Assert.That(settings.AuthenticationProfileUri, Is.Null.Or.Empty);
                Assert.That(settings.ResourceUri, Is.Null.Or.Empty);
            }
            else
            {
                Assert.That(connection.ReaderGroups[0].DataSetReaders[0].TransportSettings.TryGetValue(
                    out BrokerDataSetReaderTransportDataType? settings), Is.True);
                Assert.That(settings!.QueueName, Is.EqualTo("data"));
                Assert.That(settings.MetaDataQueueName, Is.EqualTo("metadata"));
                Assert.That(settings.RequestedDeliveryGuarantee,
                    Is.EqualTo(BrokerTransportQualityOfService.AtLeastOnce));
                Assert.That(settings.AuthenticationProfileUri, Is.Null.Or.Empty);
                Assert.That(settings.ResourceUri, Is.Null.Or.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OpaqueDatagramExtensionsAreRemovedInsteadOfMasqueradingAsTheSupportedBaseAsync(bool extension)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(fixture, "Publisher");
            WriterGroupDataType group = configuration.Connections[0].WriterGroups[0];
            group.TransportSettings = extension
                ? new ExtensionObject(new DatagramWriterGroupTransport2DataType())
                : new ExtensionObject(new DatagramWriterGroupTransportDataType { MessageRepeatCount = 3 });

            int removed = McpPubSubConfigurationSafety.Sanitize(configuration, new McpPubSubOptions());

            Assert.That(removed, Is.EqualTo(extension ? 1 : 0));
            Assert.That(group.TransportSettings.IsNull, Is.EqualTo(extension));
            if (!extension)
            {
                Assert.That(group.TransportSettings.TryGetValue(out DatagramWriterGroupTransportDataType? value),
                    Is.True);
                Assert.That(value!.MessageRepeatCount, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task MirrorSubscriptionSurvivesWhileUnsupportedPublishedEventsAreRemovedAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(fixture, "Subscriber");
            DataSetReaderDataType reader = configuration.Connections[0].ReaderGroups[0].DataSetReaders[0];
            reader.SubscribedDataSet = new ExtensionObject(new SubscribedDataSetMirrorDataType());
            configuration.PublishedDataSets =
            [
                new PublishedDataSetDataType
                {
                    Name = "events", DataSetMetaData = reader.DataSetMetaData,
                    DataSetSource = new ExtensionObject(new PublishedEventsDataType())
                }
            ];

            int removed = McpPubSubConfigurationSafety.Sanitize(configuration, new McpPubSubOptions());

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(configuration.PublishedDataSets[0].DataSetSource.IsNull, Is.True);
            Assert.That(reader.SubscribedDataSet.TryGetValue(out SubscribedDataSetMirrorDataType? _), Is.True);
        }

        [TestCase("address")]
        [TestCase("message")]
        [TestCase("fields")]
        [TestCase("components")]
        public async Task UnsupportedShapesAndOverBudgetConfigurationsAreNotSilentlyExportedAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(fixture, "Publisher");
            var options = new McpPubSubOptions
            {
                MaxFields = invalid == "fields" ? 1 : 32,
                MaxConfigurationComponents = invalid == "components" ? 1 : 128
            };
            if (invalid == "address")
            {
                configuration.Connections[0].Address = new ExtensionObject(new PublishedEventsDataType());
            }
            if (invalid == "message")
            {
                configuration.Connections[0].WriterGroups[0].MessageSettings =
                    new ExtensionObject(new NetworkAddressUrlDataType { Url = "urn:opaque:message" });
            }

            Assert.That(() => McpPubSubConfigurationSafety.Sanitize(configuration, options),
                Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(2, true)]
        [TestCase(3, false)]
        public async Task DatasetOnlyConfigurationsStillRespectTheHostComponentBudgetAsync(int count, bool accepted)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PublishedDataSetDataType template = Configuration(fixture, "Publisher").PublishedDataSets[0];
            var configuration = new PubSubConfigurationDataType
            {
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(Enumerable.Range(0, count)
                    .Select(index =>
                    {
                        var dataSet = (PublishedDataSetDataType)template.Clone();
                        dataSet.Name = "dataset-" + index;
                        return dataSet;
                    }).ToArray())
            };
            var options = new McpPubSubOptions { MaxConfigurationComponents = 2 };

            if (accepted)
            {
                Assert.That(McpPubSubConfigurationSafety.Sanitize(configuration, options), Is.Zero);
                Assert.That(configuration.PublishedDataSets, Has.Count.EqualTo(2));
            }
            else
            {
                Assert.That(() => McpPubSubConfigurationSafety.Sanitize(configuration, options),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
            }
        }

        private static PubSubConfigurationDataType Configuration(PubSubExpansionTestContext fixture, string mode)
        {
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(mode: mode);
            var source = new McpPubSubDataSetSource(input, new McpPubSubOptions(),
                fixture.Remote.Context, fixture.Clock);
            return McpPubSubConfiguration.Build(input, source.BuildMetaData());
        }

        private static ArrayOf<KeyValuePair> Properties()
        {
            return [new KeyValuePair { Key = new QualifiedName("private"), Value = Variant.From("private-marker") }];
        }

        private static readonly string[] s_fieldNames = ["Count", "Enabled"];
    }
}
#endif
