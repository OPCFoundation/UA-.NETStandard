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
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Tests;

#pragma warning disable CS0618, UA0023 // Targeted compatibility coverage for obsolete UaPubSubConfigurator.

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Compatibility coverage for the legacy in-memory PubSub configurator.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.1")]
    public sealed class UaPubSubConfiguratorCoverageTests
    {
        [Test]
        public void AddFindEnableDisableAndRemoveConfigurationObjects()
        {
            var configurator = new UaPubSubConfigurator(NUnitTelemetryContext.Create());
            int publishedAdded = 0;
            int extensionAdded = 0;
            int connectionAdded = 0;
            int writerGroupAdded = 0;
            int dataSetWriterAdded = 0;
            int readerGroupAdded = 0;
            int dataSetReaderAdded = 0;
            int stateChanges = 0;
            configurator.PublishedDataSetAdded += (_, _) => publishedAdded++;
            configurator.ExtensionFieldAdded += (_, _) => extensionAdded++;
            configurator.ConnectionAdded += (_, _) => connectionAdded++;
            configurator.WriterGroupAdded += (_, _) => writerGroupAdded++;
            configurator.DataSetWriterAdded += (_, _) => dataSetWriterAdded++;
            configurator.ReaderGroupAdded += (_, _) => readerGroupAdded++;
            configurator.DataSetReaderAdded += (_, _) => dataSetReaderAdded++;
            configurator.PubSubStateChanged += (_, _) => stateChanges++;

            var published = new PublishedDataSetDataType
            {
                Name = "Published",
                ExtensionFields =
                [
                    new KeyValuePair
                    {
                        Key = QualifiedName.From("Meta"),
                        Value = "value"
                    }
                ]
            };
            Assert.That(configurator.AddPublishedDataSet(published), Is.EqualTo(StatusCodes.Good));
            uint publishedId = configurator.FindIdForObject(published);
            Assert.That(configurator.FindPublishedDataSetByName("Published"), Is.SameAs(published));
            Assert.That(configurator.FindObjectById(publishedId), Is.SameAs(published));

            var writer = new DataSetWriterDataType
            {
                Name = "Writer",
                DataSetName = "Published"
            };
            var writerGroup = new WriterGroupDataType
            {
                Name = "WriterGroup",
                DataSetWriters = [writer]
            };
            var reader = new DataSetReaderDataType
            {
                Name = "Reader",
                DataSetWriterId = 1,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "Meta",
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "ReaderGroup",
                DataSetReaders = [reader]
            };
            var connection = new PubSubConnectionDataType
            {
                Name = "Connection",
                Enabled = true,
                WriterGroups = [writerGroup],
                ReaderGroups = [readerGroup]
            };

            Assert.That(configurator.AddConnection(connection), Is.EqualTo(StatusCodes.Good));
            uint connectionId = configurator.FindIdForObject(connection);
            uint writerGroupId = configurator.FindIdForObject(writerGroup);
            uint writerId = configurator.FindIdForObject(writer);
            uint readerGroupId = configurator.FindIdForObject(readerGroup);
            uint readerId = configurator.FindIdForObject(reader);

            Assert.Multiple(() =>
            {
                Assert.That(configurator.FindParentForObject(connection), Is.SameAs(configurator.PubSubConfiguration));
                Assert.That(configurator.FindParentForObject(writerGroup), Is.SameAs(connection));
                Assert.That(configurator.FindChildrenIdsForObject(connection), Does.Contain(writerGroupId));
                Assert.That(configurator.FindChildrenIdsForObject(writerGroup), Does.Contain(writerId));
                Assert.That(configurator.FindChildrenIdsForObject(readerGroup), Does.Contain(readerId));
                Assert.That(configurator.FindStateForObject(connection), Is.Not.EqualTo(PubSubState.Error));
                Assert.That(configurator.FindStateForId(connectionId), Is.Not.EqualTo(PubSubState.Error));
                Assert.That(publishedAdded, Is.EqualTo(1));
                Assert.That(extensionAdded, Is.EqualTo(1));
                Assert.That(connectionAdded, Is.EqualTo(1));
                Assert.That(writerGroupAdded, Is.EqualTo(1));
                Assert.That(dataSetWriterAdded, Is.EqualTo(1));
                Assert.That(readerGroupAdded, Is.EqualTo(1));
                Assert.That(dataSetReaderAdded, Is.EqualTo(1));
            });

            Assert.That(configurator.Disable(connectionId), Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.Enable(connection), Is.EqualTo(StatusCodes.Good));
            Assert.That(stateChanges, Is.GreaterThanOrEqualTo(2));

            Assert.That(configurator.RemoveDataSetReader(readerId), Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.RemoveDataSetWriter(writer), Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.RemoveReaderGroup(readerGroupId), Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.RemoveWriterGroup(writerGroup), Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.RemoveConnection(connectionId), Is.EqualTo(StatusCodes.Good));
            Assert.That(configurator.RemovePublishedDataSet(published), Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void DuplicateAndMissingObjectsReturnExpectedStatusCodes()
        {
            var configurator = new UaPubSubConfigurator(NUnitTelemetryContext.Create());
            var published = new PublishedDataSetDataType { Name = "Duplicate" };
            Assert.That(configurator.AddPublishedDataSet(published), Is.EqualTo(StatusCodes.Good));
            uint publishedId = configurator.FindIdForObject(published);
            var extensionField = new KeyValuePair
            {
                Key = QualifiedName.From("Meta"),
                Value = "value"
            };
            Assert.That(configurator.AddExtensionField(publishedId, extensionField), Is.EqualTo(StatusCodes.Good));
            uint extensionFieldId = configurator.FindIdForObject(extensionField);
            Assert.That(
                configurator.AddExtensionField(
                    publishedId,
                    new KeyValuePair
                    {
                        Key = QualifiedName.From("Meta"),
                        Value = "other"
                    }),
                Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(configurator.RemoveExtensionField(publishedId, extensionFieldId), Is.EqualTo(StatusCodes.Good));
            Assert.That(
                () => configurator.AddPublishedDataSet(published),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                configurator.AddPublishedDataSet(new PublishedDataSetDataType { Name = "Duplicate" }),
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));

            var connection = new PubSubConnectionDataType { Name = "Connection" };
            Assert.That(configurator.AddConnection(connection), Is.EqualTo(StatusCodes.Good));
            uint connectionId = configurator.FindIdForObject(connection);
            Assert.That(
                () => configurator.AddConnection(connection),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                configurator.AddConnection(new PubSubConnectionDataType { Name = "Connection" }),
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));

            var writerGroup = new WriterGroupDataType { Name = "WriterGroup" };
            Assert.That(configurator.AddWriterGroup(connectionId, writerGroup), Is.EqualTo(StatusCodes.Good));
            uint writerGroupId = configurator.FindIdForObject(writerGroup);
            Assert.That(
                () => configurator.AddWriterGroup(connectionId, writerGroup),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                configurator.AddWriterGroup(connectionId, new WriterGroupDataType { Name = "WriterGroup" }),
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            Assert.That(
                () => configurator.AddWriterGroup(uint.MaxValue, new WriterGroupDataType { Name = "Missing" }),
                Throws.TypeOf<ArgumentException>());

            var dataSetWriter = new DataSetWriterDataType { Name = "Writer" };
            Assert.That(configurator.AddDataSetWriter(writerGroupId, dataSetWriter), Is.EqualTo(StatusCodes.Good));
            Assert.That(
                () => configurator.AddDataSetWriter(writerGroupId, dataSetWriter),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                configurator.AddDataSetWriter(writerGroupId, new DataSetWriterDataType { Name = "Writer" }),
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            Assert.That(
                () => configurator.AddDataSetWriter(uint.MaxValue, new DataSetWriterDataType { Name = "Missing" }),
                Throws.TypeOf<ArgumentException>());

            Assert.Multiple(() =>
            {
                Assert.That(configurator.FindObjectById(uint.MaxValue), Is.Null);
                Assert.That(configurator.FindIdForObject(new object()), Is.EqualTo(UaPubSubConfigurator.InvalidId));
                Assert.That(configurator.FindStateForId(uint.MaxValue), Is.EqualTo(PubSubState.Error));
                Assert.That(configurator.FindParentForObject(new object()), Is.Null);
                Assert.That(configurator.RemoveWriterGroup(uint.MaxValue), Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(configurator.RemoveDataSetWriter(uint.MaxValue), Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(configurator.RemoveConnection(uint.MaxValue), Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(configurator.RemovePublishedDataSet(uint.MaxValue), Is.EqualTo(StatusCodes.Good));
                Assert.That(configurator.RemoveExtensionField(uint.MaxValue, uint.MaxValue),
                    Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            });
        }

        [Test]
        public void LoadConfigurationReplacesExistingObjectsAndAssignsDefaultNames()
        {
            var configurator = new UaPubSubConfigurator(NUnitTelemetryContext.Create());
            Assert.That(
                configurator.AddPublishedDataSet(new PublishedDataSetDataType { Name = "Old" }),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(
                configurator.AddConnection(new PubSubConnectionDataType { Name = "OldConnection" }),
                Is.EqualTo(StatusCodes.Good));

            var loaded = new PubSubConfigurationDataType
            {
                PublishedDataSets =
                [
                    new PublishedDataSetDataType { Name = "Loaded" }
                ],
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = string.Empty,
                        WriterGroups =
                        [
                            new WriterGroupDataType
                            {
                                Name = string.Empty,
                                DataSetWriters =
                                [
                                    new DataSetWriterDataType { Name = string.Empty }
                                ]
                            }
                        ],
                        ReaderGroups =
                        [
                            new ReaderGroupDataType
                            {
                                Name = string.Empty,
                                DataSetReaders =
                                [
                                    new DataSetReaderDataType { Name = string.Empty }
                                ]
                            }
                        ]
                    }
                ]
            };

            configurator.LoadConfiguration(loaded);

            PubSubConnectionDataType connection = configurator.PubSubConfiguration.Connections[0];
            Assert.Multiple(() =>
            {
                Assert.That(configurator.FindPublishedDataSetByName("Old"), Is.Null);
                Assert.That(configurator.FindPublishedDataSetByName("Loaded"), Is.Not.Null);
                Assert.That(connection.Name, Does.StartWith("Connection_"));
                Assert.That(connection.WriterGroups[0].Name, Does.StartWith("WriterGroup_"));
                Assert.That(connection.WriterGroups[0].DataSetWriters[0].Name, Does.StartWith("DataSetWriter_"));
                Assert.That(connection.ReaderGroups[0].Name, Does.StartWith("ReaderGroup_"));
                Assert.That(connection.ReaderGroups[0].DataSetReaders[0].Name, Does.StartWith("DataSetReader_"));
            });
        }
    }
}
