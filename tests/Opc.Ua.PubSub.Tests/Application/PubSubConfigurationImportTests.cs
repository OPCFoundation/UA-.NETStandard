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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    [TestFixture]
    public sealed class PubSubConfigurationImportTests
    {
        [Test]
        public async Task AddParentIgnoresUnreferencedChildrenAndAssignsDefaultPublisher()
        {
            await using IPubSubApplication app = Build();
            PubSubConnectionDataType connection = Connection(null);
            connection.PublisherId = Variant.Null;
            connection.WriterGroups = [new WriterGroupDataType { Name = "ignored", WriterGroupId = 99 }];
            PubSubConfigurationImportResult result = await Import(app,
                new PubSubConfiguration2DataType { Connections = [connection] }, [Ref(256, 1)]);
            Assert.That(result.ChangesApplied, Is.True);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            PubSubConnectionDataType actual = app.GetConfiguration().Connections[0];
            Assert.That(actual.Name, Is.Not.Null.And.Not.Empty);
            Assert.That(actual.PublisherId, Is.EqualTo(Variant.From(7u)));
            Assert.That(actual.WriterGroups.Count, Is.Zero);
            Assert.That(result.ConfigurationValues.Count, Is.EqualTo(1));
            Assert.That(result.ConfigurationValues[0].Name, Is.EqualTo(actual.Name));
            Assert.That(result.ConfigurationValues[0].Identifier, Is.EqualTo(actual.PublisherId));
            Assert.That(result.ConfigurationObjects.Count, Is.Zero);
            Assert.That(connection.Name, Is.Null);
            Assert.That(connection.PublisherId.IsNull, Is.True);
            Assert.That(connection.WriterGroups.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task MatchParentUsesExactFieldsAndPropertySubsetForUnnamedChildParent()
        {
            PubSubConnectionDataType existing = Connection("existing");
            existing.ConnectionProperties =
            [
                new KeyValuePair { Key = new QualifiedName("required"), Value = Variant.From(1) },
                new KeyValuePair { Key = new QualifiedName("extra"), Value = Variant.From(2) }
            ];
            await using IPubSubApplication app = Build(new PubSubConfiguration2DataType { Connections = [existing] });
            PubSubConnectionDataType incoming = Connection(null);
            incoming.PublisherId = Variant.Null;
            incoming.ConnectionProperties = [existing.ConnectionProperties[0]];
            incoming.WriterGroups = [new WriterGroupDataType { Name = "child", PublishingInterval = 1000 }];
            PubSubConfigurationImportResult result = await Import(app,
                new PubSubConfiguration2DataType { Connections = [incoming] },
                [Ref(64, 1), Ref(256, 2)]);
            Assert.That(result.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(app.GetConfiguration().Connections.Count, Is.EqualTo(1));
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups[0].Name, Is.EqualTo("child"));
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups[0].WriterGroupId,
                Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task ModifyPreservesExistingChildrenAndRemoveDeletesThemBeforeSameNameAdd()
        {
            PubSubConnectionDataType existing = Connection("same");
            existing.WriterGroups =
            [
                new WriterGroupDataType { Name = "old-child", WriterGroupId = 1, PublishingInterval = 1000 }
            ];
            await using IPubSubApplication app = Build(new PubSubConfiguration2DataType { Connections = [existing] });
            PubSubConnectionDataType modified = Connection("same");
            modified.ConnectionProperties =
            [
                new KeyValuePair { Key = new QualifiedName("changed"), Value = Variant.From(1) }
            ];
            PubSubConfigurationImportResult modification = await Import(app,
                new PubSubConfiguration2DataType { Connections = [modified] }, [Ref(256, 4)]);
            Assert.That(modification.ChangesApplied, Is.True);
            Assert.That(modification.ConfigurationValues.Count, Is.Zero);
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups.Count, Is.EqualTo(1));
            Assert.That(app.GetConfiguration().Connections[0].ConnectionProperties.Count, Is.EqualTo(1));
            PubSubConfigurationImportResult replacement = await Import(app,
                new PubSubConfiguration2DataType { Connections = [Connection("same"), Connection("same")] },
                [Ref(256, 1), Ref(256, 8, connection: 1)]);
            Assert.That(replacement.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(app.GetConfiguration().Connections.Count, Is.EqualTo(1));
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups.Count, Is.Zero);
            Assert.That(replacement.ConfigurationValues.Count, Is.Zero);
        }

        [Test]
        public async Task NestedRemovalProcessesChildBeforeParentRegardlessOfReferenceOrder()
        {
            PubSubConnectionDataType existing = Connection("parent");
            existing.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "group",
                    WriterGroupId = 10,
                    PublishingInterval = 1000,
                    DataSetWriters =
                    [
                        new DataSetWriterDataType { Name = "writer", DataSetWriterId = 11, DataSetName = "pds" }
                    ]
                }
            ];
            await using IPubSubApplication app = Build(
                new PubSubConfiguration2DataType
                {
                    Connections = [existing],
                    PublishedDataSets = [new PublishedDataSetDataType { Name = "pds" }]
                });
            var input = new PubSubConfiguration2DataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = "parent",
                        WriterGroups =
                        [
                            new WriterGroupDataType
                            {
                                Name = "group",
                                DataSetWriters = [new DataSetWriterDataType { Name = "writer" }]
                            }
                        ]
                    }
                ]
            };
            PubSubConfigurationImportResult result = await Import(app, input,
                [Ref(256, 8), Ref(64, 8), Ref(16, 8)]);
            Assert.That(result.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(app.GetConfiguration().Connections, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CompleteUpdateRollsBackWhilePartialUpdatePreservesResultOrder(bool complete)
        {
            await using IPubSubApplication app = Build();
            var input = new PubSubConfiguration2DataType
            {
                Connections = [Connection("good"), Connection("missing")],
                ConfigurationProperties = [new KeyValuePair { Key = new QualifiedName("top"), Value = Variant.From(1) }]
            };
            PubSubConfigurationImportResult result = await Import(app, input,
                [Ref(256, 4, connection: 1), Ref(256, 1)], complete);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadNoMatch));
            Assert.That(result.ReferenceResults[1], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(result.ChangesApplied, Is.EqualTo(!complete));
            Assert.That(app.GetConfiguration().Connections.Count, Is.EqualTo(complete ? 0 : 1));
            if (!complete)
            {
                Assert.That(((PubSubConfiguration2DataType)app.GetConfiguration()).ConfigurationProperties.Count,
                    Is.EqualTo(1));
            }
        }

        [TestCase(0u, 0, 0, 0)]
        [TestCase(257u, 0, 1, 0)]
        [TestCase(257u, 0, 0, 1)]
        [TestCase(257u, 1, 0, 0)]
        [TestCase(273u, 0, 0, 0)]
        [TestCase(261u, 0, 0, 0)]
        [TestCase(513u, 1, 0, 0)]
        [TestCase(18u, 0, 0, 0)]
        [TestCase(0x2001u, 0, 0, 0)]
        public async Task MalformedReferenceReturnsBadInvalidArgument(uint mask, int connection, int group, int element)
        {
            await using IPubSubApplication app = Build();
            var reference = new PubSubConfigurationRefDataType
            {
                ConfigurationMask = mask,
                ConnectionIndex = (ushort)connection,
                GroupIndex = (ushort)group,
                ElementIndex = (ushort)element
            };
            PubSubConfigurationImportResult result = await Import(app,
                new PubSubConfiguration2DataType { Connections = [Connection("input")] }, [reference]);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(result.ChangesApplied, Is.False);
            Assert.That(app.GetConfiguration().Connections.Count, Is.Zero);
        }

        [Test]
        public async Task TopPropertiesMergeDeleteAndPreserveDerivedFieldsIgnoringReadOnlyInput()
        {
            var initial = new PubSubConfiguration2DataType
            {
                Enabled = true,
                ConfigurationProperties =
                [
                    new KeyValuePair { Key = new QualifiedName("delete"), Value = Variant.From(1) },
                    new KeyValuePair { Key = new QualifiedName("replace"), Value = Variant.From(2) }
                ],
                DefaultSecurityKeyServices = [new EndpointDescription { EndpointUrl = "opc.tcp://old" }]
            };
            await using IPubSubApplication app = Build(initial);
            var input = new PubSubConfiguration2DataType
            {
                Enabled = false,
                ConfigurationVersion = 1,
                DataSetClasses = [new DataSetMetaDataType { Name = "ignored" }],
                DefaultSecurityKeyServices = [new EndpointDescription { EndpointUrl = "opc.tcp://new" }],
                ConfigurationProperties =
                [
                    new KeyValuePair { Key = new QualifiedName("delete"), Value = Variant.Null },
                    new KeyValuePair { Key = new QualifiedName("replace"), Value = Variant.From(3) },
                    new KeyValuePair { Key = new QualifiedName("insert"), Value = Variant.From(4) }
                ],
                PublishedDataSets = [new PublishedDataSetDataType { Name = "added" }]
            };
            _ = await Import(app, input, [Ref(512, 1)]);
            var actual = (PubSubConfiguration2DataType)app.GetConfiguration();
            Assert.That(actual.Enabled, Is.True);
            Assert.That(actual.ConfigurationVersion, Is.Not.EqualTo(1u).And.Not.Zero);
            Assert.That(actual.DataSetClasses.Count, Is.Zero);
            Assert.That(actual.ConfigurationProperties.Count, Is.EqualTo(2));
            Assert.That(actual.ConfigurationProperties[0].Value, Is.EqualTo(Variant.From(3)));
            Assert.That(actual.ConfigurationProperties[1].Value, Is.EqualTo(Variant.From(4)));
            Assert.That(actual.DefaultSecurityKeyServices[0].EndpointUrl, Is.EqualTo("opc.tcp://new"));
            _ = await Import(app, new PubSubConfiguration2DataType
            {
                PublishedDataSets = [new PublishedDataSetDataType { Name = "second" }]
            }, [Ref(512, 1)]);
            actual = (PubSubConfiguration2DataType)app.GetConfiguration();
            Assert.That(actual.ConfigurationProperties.Count, Is.EqualTo(2));
            Assert.That(actual.DefaultSecurityKeyServices[0].EndpointUrl, Is.EqualTo("opc.tcp://new"));
        }

        [Test]
        public async Task AutomaticIdsUseTypedPublisherDomainExcludeReservationsAndReleaseFailedImport()
        {
            using var reservations = new InMemoryPubSubCommunicationIdReservations(new PubSubDefaultPublisherIdPolicy(
                [new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7u))]));
            PubSubConnectionDataType initial = Connection("parent");
            initial.PublisherId = Variant.From("typed");
            await using IPubSubApplication app = Build(
                new PubSubConfiguration2DataType { Connections = [initial] }, reservations);
            PubSubConnectionDataType input = Connection("parent");
            input.WriterGroups = [new WriterGroupDataType { Name = "auto", PublishingInterval = 1000 }];
            PubSubConfigurationImportResult failed = await Import(app,
                new PubSubConfiguration2DataType { Connections = [input] },
                [Ref(64, 1), Ref(256, 4, connection: 99)]);
            Assert.That(failed.ChangesApplied, Is.False);
            Assert.That(failed.ConfigurationValues.Count, Is.Zero);
            PubSubConfigurationImportResult result = await Import(app,
                new PubSubConfiguration2DataType { Connections = [input] }, [Ref(64, 1)]);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(app.GetConfiguration().Connections[0].PublisherId, Is.EqualTo(Variant.From("typed")));
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups[0].WriterGroupId,
                Is.EqualTo((ushort)0x8000), "Failed complete import must release provisional IDs.");
            Assert.That(result.ConfigurationValues[0].Identifier, Is.EqualTo(Variant.From((ushort)0x8000)));
        }

        [Test]
        public async Task ReservedIdsRequireSameSessionAndAutomaticIdsSkipForeignReservations()
        {
            using var reservations = new InMemoryPubSubCommunicationIdReservations(new PubSubDefaultPublisherIdPolicy(
                [new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7u))]));
            await using IPubSubApplication app = Build(
                new PubSubConfiguration2DataType { Connections = [Connection("parent")] }, reservations);
            NodeId owner = new(123u);
            _ = await ((IPubSubCommunicationIdApplication)app).ReserveCommunicationIdsAsync(
                owner, Profiles.PubSubUdpUadpTransport, 1, 0);
            PubSubConnectionDataType input = Connection("parent");
            input.WriterGroups =
            [
                new WriterGroupDataType { Name = "reserved", WriterGroupId = 0x8000, PublishingInterval = 1000 },
                new WriterGroupDataType { Name = "automatic", PublishingInterval = 1000 }
            ];
            var config = new PubSubConfiguration2DataType { Connections = [input] };
            PubSubConfigurationImportResult denied = await Import(app, config, [Ref(64, 1)]);
            Assert.That(denied.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            PubSubConfigurationImportResult automatic = await Import(app, config, [Ref(64, 1, group: 1)]);
            Assert.That(automatic.ChangesApplied, Is.True);
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups[0].WriterGroupId,
                Is.EqualTo((ushort)0x8001));
            PubSubConfigurationImportResult consumed = await PubSubConfigurationSession.ExecuteAsync(
                owner, _ => Import(app, config, [Ref(64, 1)]));
            Assert.That(consumed.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups.Count, Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task UnauthorizedSecurityReferencesReturnElementDenialWithoutBypassingAtomicity(bool complete)
        {
            await using IPubSubApplication app = Build();
            var input = new PubSubConfiguration2DataType
            {
                PublishedDataSets = [new PublishedDataSetDataType { Name = "ordinary" }],
                SecurityGroups = [new SecurityGroupDataType { Name = "privileged" }]
            };
            PubSubConfigurationImportResult result = await ((IPubSubConfigurationImportApplication)app)
                .ImportConfigurationAsync(input, [Ref(2048, 1), Ref(512, 1)], complete, false);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(result.ReferenceResults[1], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(result.ChangesApplied, Is.EqualTo(!complete));
            Assert.That(app.GetConfiguration().PublishedDataSets.Count, Is.EqualTo(complete ? 0 : 1));
        }

        [Test]
        public async Task MissingUnreferencedParentReturnsBadNotFound()
        {
            await using IPubSubApplication app = Build();
            PubSubConnectionDataType input = Connection("absent");
            input.ReaderGroups = [new ReaderGroupDataType { Name = "child" }];
            PubSubConfigurationImportResult result = await Import(app,
                new PubSubConfiguration2DataType { Connections = [input] }, [Ref(128, 1)]);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            Assert.That(result.ChangesApplied, Is.False);
        }

        [Test]
        public async Task WritersReadersAndStandaloneElementsApplyOnlyTheirOwnReference()
        {
            await using IPubSubApplication app = Build();
            PubSubConnectionDataType incoming = Connection("connection");
            incoming.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "wg",
                    PublishingInterval = 1000,
                    DataSetWriters =
                    [
                        new DataSetWriterDataType { Name = "writer", DataSetName = "pds", KeyFrameCount = 1 },
                        new DataSetWriterDataType { Name = "unreferenced", DataSetName = "invalid" }
                    ]
                }
            ];
            incoming.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "rg",
                    DataSetReaders =
                    [
                        new DataSetReaderDataType
                        {
                            Name = "reader",
                            DataSetWriterId = 10,
                            MessageReceiveTimeout = 1000,
                            SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                        }
                    ]
                }
            ];
            var configuration = new PubSubConfiguration2DataType
            {
                Connections = [incoming],
                PublishedDataSets = [new PublishedDataSetDataType { Name = "pds" }],
                SubscribedDataSets =
                [
                    new StandaloneSubscribedDataSetDataType { Name = "standalone" }
                ],
                SecurityGroups = [new SecurityGroupDataType { Name = "security" }],
                PubSubKeyPushTargets = [new PubSubKeyPushTargetDataType { ApplicationUri = "urn:target" }]
            };
            PubSubConfigurationImportResult added = await Import(app, configuration,
            [
                Ref(16, 1), Ref(32, 1), Ref(64, 1), Ref(128, 1), Ref(256, 1),
                Ref(512, 1), Ref(1024, 1), Ref(2048, 1), Ref(4096, 1)
            ]);
            Assert.That(added.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            var actual = (PubSubConfiguration2DataType)app.GetConfiguration();
            Assert.That(actual.Connections[0].WriterGroups[0].DataSetWriters.Count, Is.EqualTo(1));
            Assert.That(actual.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetWriterId,
                Is.EqualTo((ushort)0x8000));
            Assert.That(actual.Connections[0].ReaderGroups[0].DataSetReaders[0].Name, Is.EqualTo("reader"));
            Assert.That(actual.SubscribedDataSets[0].Name, Is.EqualTo("standalone"));
            Assert.That(actual.SecurityGroups[0].Name, Is.EqualTo("security"));
            Assert.That(actual.PubSubKeyPushTargets[0].ApplicationUri, Is.EqualTo("urn:target"));
            incoming.WriterGroups[0].DataSetWriters[0].DataSetWriterId =
                actual.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetWriterId;
            incoming.WriterGroups[0].DataSetWriters[0].KeyFrameCount = 7;
            incoming.ReaderGroups[0].DataSetReaders[0].MessageReceiveTimeout = 2000;
            PubSubConfigurationImportResult modified = await Import(app, configuration, [Ref(16, 4), Ref(32, 4)]);
            Assert.That(modified.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            actual = (PubSubConfiguration2DataType)app.GetConfiguration();
            Assert.That(actual.Connections[0].WriterGroups[0].DataSetWriters[0].KeyFrameCount, Is.EqualTo(7u));
            Assert.That(actual.Connections[0].ReaderGroups[0].DataSetReaders[0].MessageReceiveTimeout,
                Is.EqualTo(2000));
            PubSubConfigurationImportResult removed = await Import(app, configuration,
                [Ref(16, 8), Ref(32, 8), Ref(1024, 8), Ref(2048, 8), Ref(4096, 8)]);
            Assert.That(removed.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            actual = (PubSubConfiguration2DataType)app.GetConfiguration();
            Assert.That(actual.Connections[0].WriterGroups[0].DataSetWriters.Count, Is.Zero);
            Assert.That(actual.Connections[0].ReaderGroups[0].DataSetReaders.Count, Is.Zero);
            Assert.That(actual.SubscribedDataSets.Count, Is.Zero);
            Assert.That(actual.SecurityGroups.Count, Is.Zero);
            Assert.That(actual.PubSubKeyPushTargets.Count, Is.Zero);
        }

        [Test]
        public async Task WriterAndReaderGroupsMatchExactFieldsIgnoringExtraPropertiesAndChildren()
        {
            PubSubConnectionDataType initial = Connection("existing");
            initial.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "writer-group",
                    WriterGroupId = 10,
                    PublishingInterval = 1000,
                    GroupProperties = [new KeyValuePair { Key = new QualifiedName("extra"), Value = Variant.From(1) }]
                }
            ];
            initial.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "reader-group",
                    GroupProperties = [new KeyValuePair { Key = new QualifiedName("extra"), Value = Variant.From(1) }]
                }
            ];
            await using IPubSubApplication app = Build(new PubSubConfiguration2DataType { Connections = [initial] });
            PubSubConnectionDataType incoming = Connection("existing");
            incoming.WriterGroups = [new WriterGroupDataType { PublishingInterval = 1000 }];
            incoming.ReaderGroups = [new ReaderGroupDataType()];
            var configuration = new PubSubConfiguration2DataType { Connections = [incoming] };
            PubSubConfigurationImportResult matched = await Import(app, configuration, [Ref(64, 2), Ref(128, 2)]);
            Assert.That(matched.ChangesApplied, Is.False);
            Assert.That(matched.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(matched.ConfigurationValues[0].Name, Is.EqualTo("writer-group"));
            Assert.That(matched.ConfigurationValues[0].Identifier, Is.EqualTo(Variant.From((ushort)10)));
            Assert.That(matched.ConfigurationValues[1].Name, Is.EqualTo("reader-group"));
            incoming.WriterGroups[0].KeepAliveTime = 2000;
            incoming.ReaderGroups[0].MaxNetworkMessageSize = 100;
            PubSubConfigurationImportResult mismatch = await Import(app, configuration, [Ref(64, 2), Ref(128, 2)]);
            Assert.That(mismatch.ReferenceResults.ToList(), Is.All.EqualTo((StatusCode)StatusCodes.BadNoMatch));
            Assert.That(mismatch.ChangesApplied, Is.False);
        }

        [Test]
        public async Task MatchAddAddsOnlyOnNoMatchAndGroupHeaderMatchReturnsBadInvalidState()
        {
            await using IPubSubApplication app = Build();
            PubSubConnectionDataType incoming = Connection(null);
            incoming.PublisherId = Variant.Null;
            incoming.WriterGroups =
            [
                new WriterGroupDataType
                {
                    PublishingInterval = 1000,
                    MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)UadpNetworkMessageContentMask.GroupHeader
                    })
                }
            ];
            var configuration = new PubSubConfiguration2DataType { Connections = [incoming] };
            PubSubConfigurationImportResult added = await Import(app, configuration, [Ref(256, 3)]);
            Assert.That(added.ChangesApplied, Is.True);
            PubSubConfigurationImportResult matched = await Import(app, configuration, [Ref(256, 3)]);
            Assert.That(matched.ChangesApplied, Is.False);
            Assert.That(app.GetConfiguration().Connections.Count, Is.EqualTo(1));
            PubSubConfigurationImportResult invalid = await Import(app, configuration, [Ref(256, 2), Ref(64, 2)]);
            Assert.That(invalid.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(invalid.ReferenceResults[1], Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task DuplicateAddMissingModifyAndMissingRemoveHaveDistinctElementResults()
        {
            await using IPubSubApplication app = Build(
                new PubSubConfiguration2DataType { Connections = [Connection("exists")] });
            var input = new PubSubConfiguration2DataType
            {
                Connections = [Connection("exists"), Connection("missing")]
            };
            PubSubConfigurationImportResult result = await Import(app, input,
                [Ref(256, 1), Ref(256, 4, connection: 1), Ref(256, 8, connection: 1)], false);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(result.ReferenceResults[1], Is.EqualTo((StatusCode)StatusCodes.BadNoMatch));
            Assert.That(result.ReferenceResults[2], Is.EqualTo((StatusCode)StatusCodes.BadNoMatch));
            Assert.That(result.ChangesApplied, Is.False);
        }

        [Test]
        public async Task EmptyReferenceArrayReturnsBadNothingToDo()
        {
            await using IPubSubApplication app = Build();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Import(app, new PubSubConfiguration2DataType(), []));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FailedPersistenceLeavesRuntimeUnchangedAndReleasesProvisionalAllocation()
        {
            using var inner = new InMemoryPubSubCommunicationIdReservations(new PubSubDefaultPublisherIdPolicy(
                [new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7u))]));
            var provider = new Mock<IPubSubCommunicationIdReservations>();
            var allocator = provider.As<IPubSubCommunicationIdAllocation>();
            allocator.Setup(a => a.AllocateAsync(It.IsAny<string>(), It.IsAny<Variant>(), It.IsAny<bool>(),
                It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()))
                .Returns((string scope, Variant publisher, bool writer, PubSubConfigurationDataType configuration,
                    CancellationToken ct) => inner.AllocateAsync(scope, publisher, writer, configuration, ct));
            allocator.Setup(a => a.ValidateOwnershipAsync(
                It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()))
                .Returns((PubSubConfigurationDataType configuration, CancellationToken ct) =>
                    inner.ValidateOwnershipAsync(configuration, ct));
            bool fail = true;
            provider.Setup(p => p.PersistAsync(It.IsAny<PubSubConfigurationDataType>(),
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns((PubSubConfigurationDataType configuration, Func<CancellationToken, ValueTask> persist,
                    CancellationToken ct) =>
                {
                    if (fail)
                    {
                        fail = false;
                        return new ValueTask(Task.FromException(
                            new ServiceResultException(StatusCodes.BadUnexpectedError)));
                    }
                    return inner.PersistAsync(configuration, persist, ct);
                });
            await using IPubSubApplication app = Build(
                new PubSubConfiguration2DataType { Connections = [Connection("parent")] }, provider.Object);
            PubSubConnectionDataType incoming = Connection("parent");
            incoming.WriterGroups = [new WriterGroupDataType { Name = "automatic", PublishingInterval = 1000 }];
            var input = new PubSubConfiguration2DataType { Connections = [incoming] };
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await Import(app, input, [Ref(64, 1)]));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups.Count, Is.Zero);
            PubSubConfigurationImportResult result = await Import(app, input, [Ref(64, 1)]);
            Assert.That(result.ChangesApplied, Is.True);
            Assert.That(app.GetConfiguration().Connections[0].WriterGroups[0].WriterGroupId,
                Is.EqualTo((ushort)0x8000));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task NullReferenceOrElementReturnsBadInvalidArgument(bool nullReference)
        {
            await using IPubSubApplication app = Build();
            var input = new PubSubConfiguration2DataType { Connections = [null!] };
            PubSubConfigurationImportResult result = await Import(
                app, input, [nullReference ? null! : Ref(256, 1)]);
            Assert.That(result.ReferenceResults[0], Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(result.ChangesApplied, Is.False);
        }

        [Test]
        public async Task ImportSerializesWithOrdinaryMutationsWithoutLosingEitherUpdate()
        {
            using var inner = new InMemoryPubSubCommunicationIdReservations();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPubSubCommunicationIdReservations>();
            int calls = 0;
            provider.Setup(p => p.PersistAsync(It.IsAny<PubSubConfigurationDataType>(),
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(async (PubSubConfigurationDataType configuration, Func<CancellationToken, ValueTask> persist,
                    CancellationToken ct) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        entered.SetResult(true);
                        _ = await release.Task.ConfigureAwait(false);
                    }
                    await inner.PersistAsync(configuration, persist, ct).ConfigureAwait(false);
                });
            await using IPubSubApplication app = Build(reservations: provider.Object);
            Task<PubSubConfigurationImportResult> import = Import(app,
                new PubSubConfiguration2DataType { Connections = [Connection("imported")] }, [Ref(256, 1)]).AsTask();
            _ = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task<NodeId> ordinary = app.AddConnectionAsync(Connection("ordinary")).AsTask();
            try
            {
                Assert.That(ordinary.IsCompleted, Is.False);
                Assert.That(app.GetConfiguration().Connections.Count, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That((await import).ChangesApplied, Is.True);
            Assert.That((await ordinary).IsNull, Is.False);
            Assert.That(app.GetConfiguration().Connections.Count, Is.EqualTo(2));
            Assert.That(app.GetConfiguration().Connections.ToList().Select(c => c.Name),
                Does.Contain("imported").And.Contain("ordinary"));
        }

        private static ValueTask<PubSubConfigurationImportResult> Import(
            IPubSubApplication app, PubSubConfiguration2DataType configuration,
            ArrayOf<PubSubConfigurationRefDataType> references, bool complete = true)
        {
            return ((IPubSubConfigurationImportApplication)app)
                .ImportConfigurationAsync(configuration, references, complete);
        }

        private static PubSubConfigurationRefDataType Ref(
            uint kind, uint operation, ushort connection = 0, ushort group = 0, ushort element = 0)
        {
            return new PubSubConfigurationRefDataType
            {
                ConfigurationMask = kind | operation,
                ConnectionIndex = connection,
                GroupIndex = group,
                ElementIndex = element
            };
        }

        private static PubSubConnectionDataType Connection(string? name)
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                PublisherId = Variant.From(7u),
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
        }

        private static IPubSubApplication Build(
            PubSubConfigurationDataType? configuration = null,
            IPubSubCommunicationIdReservations? reservations = null)
        {
            var factory = new Mock<IPubSubTransportFactory>();
            factory.SetupGet(f => f.TransportProfileUri).Returns(Profiles.PubSubUdpUadpTransport);
            factory.Setup(f => f.Create(It.IsAny<PubSubConnectionDataType>(), It.IsAny<ITelemetryContext>(),
                It.IsAny<TimeProvider>())).Returns(() =>
                {
                    var transport = new Mock<IPubSubTransport>();
                    transport.SetupGet(t => t.Direction).Returns(PubSubTransportDirection.SendReceive);
                    transport.Setup(t => t.DisposeAsync()).Returns(default(ValueTask));
                    return transport.Object;
                });
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(configuration ?? new PubSubConfiguration2DataType())
                .WithCommunicationIdReservations(reservations ??
                    new InMemoryPubSubCommunicationIdReservations(new PubSubDefaultPublisherIdPolicy(
                        [new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7u))])))
                .UseAllStandardEncoders()
                .AddTransportFactory(factory.Object)
                .Build();
        }
    }
}
