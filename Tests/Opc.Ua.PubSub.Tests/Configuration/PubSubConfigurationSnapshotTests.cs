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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Coverage for <see cref="PubSubConfigurationSnapshot"/>: index
    /// materialisation across all dimensions, duplicate-key detection,
    /// empty-config behaviour, and deterministic <c>CreatedAt</c>.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSub configuration model snapshot")]
    public class PubSubConfigurationSnapshotTests
    {
        private static PubSubConfigurationDataType BuildSimpleConfig()
        {
            return new PubSubConfigurationDataType
            {
                Enabled = true,
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                    new[]
                    {
                        new PublishedDataSetDataType { Name = "DS1" },
                        new PublishedDataSetDataType { Name = "DS2" }
                    }),
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn1",
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                            WriterGroups = new ArrayOf<WriterGroupDataType>(
                                new[]
                                {
                                    new WriterGroupDataType
                                    {
                                        Name = "WG1",
                                        WriterGroupId = 1,
                                        PublishingInterval = 1000.0,
                                        DataSetWriters = new ArrayOf<DataSetWriterDataType>(
                                            new[]
                                            {
                                                new DataSetWriterDataType
                                                {
                                                    Name = "Writer1",
                                                    DataSetWriterId = 10,
                                                    DataSetName = "DS1",
                                                    KeyFrameCount = 1
                                                },
                                                new DataSetWriterDataType
                                                {
                                                    Name = "Writer2",
                                                    DataSetWriterId = 11,
                                                    DataSetName = "DS2",
                                                    KeyFrameCount = 1
                                                }
                                            })
                                    }
                                }),
                            ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                                new[]
                                {
                                    new ReaderGroupDataType
                                    {
                                        Name = "RG1",
                                        DataSetReaders = new ArrayOf<DataSetReaderDataType>(
                                            new[]
                                            {
                                                new DataSetReaderDataType
                                                {
                                                    Name = "Reader1",
                                                    DataSetWriterId = 10,
                                                    MessageReceiveTimeout = 1000.0,
                                                    SubscribedDataSet = new ExtensionObject(
                                                        new TargetVariablesDataType())
                                                }
                                            })
                                    }
                                })
                        }
                    })
            };
        }

        [Test]
        [TestSpec("9.1.6", Summary = "ConnectionsByName index includes every connection")]
        public void Create_IndexesConnectionsByName()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                BuildSimpleConfig());
            Assert.That(snapshot.ConnectionsByName, Has.Count.EqualTo(1));
            Assert.That(snapshot.ConnectionsByName.ContainsKey("Conn1"), Is.True);
        }

        [Test]
        [TestSpec("9.1.6", Summary = "WriterGroupsById indexes (Connection, WriterGroupId)")]
        public void Create_IndexesWriterGroupsById()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                BuildSimpleConfig());
            Assert.That(snapshot.WriterGroupsById, Has.Count.EqualTo(1));
            Assert.That(snapshot.WriterGroupsById.ContainsKey(new WriterGroupKey("Conn1", 1)), Is.True);
        }

        [Test]
        [TestSpec("9.1.7", Summary = "DataSetWritersById indexes by (Connection, WG, DSW)")]
        public void Create_IndexesDataSetWritersById()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                BuildSimpleConfig());
            Assert.That(snapshot.DataSetWritersById, Has.Count.EqualTo(2));
            Assert.That(snapshot.DataSetWritersById.ContainsKey(new DataSetWriterKey("Conn1", 1, 10)), Is.True);
            Assert.That(snapshot.DataSetWritersById.ContainsKey(new DataSetWriterKey("Conn1", 1, 11)), Is.True);
        }

        [Test]
        [TestSpec("9.1.8", Summary = "ReaderGroupsByName indexes by (Connection, ReaderGroupName)")]
        public void Create_IndexesReaderGroupsByName()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                BuildSimpleConfig());
            Assert.That(snapshot.ReaderGroupsByName, Has.Count.EqualTo(1));
            Assert.That(snapshot.ReaderGroupsByName.ContainsKey(new ReaderGroupKey("Conn1", "RG1")), Is.True);
        }

        [Test]
        [TestSpec("9.1.9", Summary = "DataSetReadersByName indexes by (Connection, RG, Reader)")]
        public void Create_IndexesDataSetReadersByName()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                BuildSimpleConfig());
            Assert.That(snapshot.DataSetReadersByName, Has.Count.EqualTo(1));
            Assert.That(
                snapshot.DataSetReadersByName.ContainsKey(new DataSetReaderKey("Conn1", "RG1", "Reader1")),
                Is.True);
        }

        [Test]
        [TestSpec("9.1.4", Summary = "PublishedDataSetsByName indexes published data sets")]
        public void Create_IndexesPublishedDataSetsByName()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                BuildSimpleConfig());
            Assert.That(snapshot.PublishedDataSetsByName, Has.Count.EqualTo(2));
            Assert.That(snapshot.PublishedDataSetsByName.ContainsKey("DS1"), Is.True);
            Assert.That(snapshot.PublishedDataSetsByName.ContainsKey("DS2"), Is.True);
        }

        [Test]
        public void Create_OnDuplicateConnectionName_ThrowsConfigurationException()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType { Name = "Dup" },
                        new PubSubConnectionDataType { Name = "Dup" }
                    })
            };
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(
                    () => PubSubConfigurationSnapshot.Create(config))!;
            Assert.That(((PubSubConfigurationIssue[]?)ex.Issues) ?? [], Is.Not.Empty);
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0102"));
        }

        [Test]
        public void Create_OnDuplicateWriterGroupId_ThrowsConfigurationException()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn",
                            WriterGroups = new ArrayOf<WriterGroupDataType>(
                                new[]
                                {
                                    new WriterGroupDataType { Name = "A", WriterGroupId = 1 },
                                    new WriterGroupDataType { Name = "B", WriterGroupId = 1 }
                                })
                        }
                    })
            };
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(
                    () => PubSubConfigurationSnapshot.Create(config))!;
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0103"));
        }

        [Test]
        public void Create_OnDuplicateDataSetWriterId_ThrowsConfigurationException()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn",
                            WriterGroups = new ArrayOf<WriterGroupDataType>(
                                new[]
                                {
                                    new WriterGroupDataType
                                    {
                                        Name = "WG",
                                        WriterGroupId = 1,
                                        DataSetWriters = new ArrayOf<DataSetWriterDataType>(
                                            new[]
                                            {
                                                new DataSetWriterDataType { Name = "A", DataSetWriterId = 5 },
                                                new DataSetWriterDataType { Name = "B", DataSetWriterId = 5 }
                                            })
                                    }
                                })
                        }
                    })
            };
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(
                    () => PubSubConfigurationSnapshot.Create(config))!;
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0104"));
        }

        [Test]
        public void Create_OnDuplicateReaderGroupName_ThrowsConfigurationException()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn",
                            ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                                new[]
                                {
                                    new ReaderGroupDataType { Name = "RG" },
                                    new ReaderGroupDataType { Name = "RG" }
                                })
                        }
                    })
            };
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(
                    () => PubSubConfigurationSnapshot.Create(config))!;
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0106"));
        }

        [Test]
        public void Create_OnDuplicatePublishedDataSetName_ThrowsConfigurationException()
        {
            var config = new PubSubConfigurationDataType
            {
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                    new[]
                    {
                        new PublishedDataSetDataType { Name = "DS" },
                        new PublishedDataSetDataType { Name = "DS" }
                    })
            };
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(
                    () => PubSubConfigurationSnapshot.Create(config))!;
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0110"));
        }

        [Test]
        public void Create_OnDuplicateDataSetReaderName_ThrowsConfigurationException()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn",
                            ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                                new[]
                                {
                                    new ReaderGroupDataType
                                    {
                                        Name = "RG",
                                        DataSetReaders = new ArrayOf<DataSetReaderDataType>(
                                            new[]
                                            {
                                                new DataSetReaderDataType { Name = "R" },
                                                new DataSetReaderDataType { Name = "R" }
                                            })
                                    }
                                })
                        }
                    })
            };
            PubSubConfigurationException ex =
                Assert.Throws<PubSubConfigurationException>(
                    () => PubSubConfigurationSnapshot.Create(config))!;
            Assert.That(
                ((PubSubConfigurationIssue[]?)ex.Issues) ?? [],
                Has.Some.Matches<PubSubConfigurationIssue>(static i => i.Code == "PSC0108"));
        }

        [Test]
        public void Create_OnEmptyConfig_BuildsEmptyIndices()
        {
            var snapshot = PubSubConfigurationSnapshot.Create(
                new PubSubConfigurationDataType());
            Assert.That(snapshot.ConnectionsByName, Is.Empty);
            Assert.That(snapshot.WriterGroupsById, Is.Empty);
            Assert.That(snapshot.DataSetWritersById, Is.Empty);
            Assert.That(snapshot.ReaderGroupsByName, Is.Empty);
            Assert.That(snapshot.DataSetReadersByName, Is.Empty);
            Assert.That(snapshot.PublishedDataSetsByName, Is.Empty);
        }

        [Test]
        public void Create_UsesProvidedTimeProvider_ForCreatedAt()
        {
            var fixedNow = new DateTimeOffset(2026, 7, 1, 12, 30, 0, TimeSpan.Zero);
            var clock = new FakeTimeProvider(fixedNow);
            var snapshot = PubSubConfigurationSnapshot.Create(
                new PubSubConfigurationDataType(),
                clock);
            Assert.That(
                snapshot.CreatedAt.ToDateTimeOffset(),
                Is.EqualTo(fixedNow));
        }

        [Test]
        public void DefaultConstructor_ProducesEmptyIndices()
        {
            var snapshot = new PubSubConfigurationSnapshot(
                new PubSubConfigurationDataType(),
                DateTimeUtc.From(DateTimeOffset.UtcNow));
            Assert.That(snapshot.ConnectionsByName, Is.Empty);
            Assert.That(snapshot.WriterGroupsById, Is.Empty);
            Assert.That(snapshot.DataSetWritersById, Is.Empty);
            Assert.That(snapshot.ReaderGroupsByName, Is.Empty);
            Assert.That(snapshot.DataSetReadersByName, Is.Empty);
            Assert.That(snapshot.PublishedDataSetsByName, Is.Empty);
        }

        [Test]
        public void Create_NullConfiguration_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PubSubConfigurationSnapshot.Create(null!));
        }

        [Test]
        public void Constructor_NullConfiguration_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubConfigurationSnapshot(null!, DateTimeUtc.From(DateTimeOffset.UtcNow)));
        }
    }
}
