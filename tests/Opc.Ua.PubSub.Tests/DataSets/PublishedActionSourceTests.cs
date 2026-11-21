// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Tests.DataSets
{
    /// <summary>
    /// Tests for <see cref="PublishedActionSource"/>.
    /// </summary>
    [TestFixture]
    public sealed class PublishedActionSourceTests
    {
        [Test]
        public void ConstructorWithNullActionThrowsArgumentNullException()
        {
            Assert.That(
                () => new PublishedActionSource(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("action"));
        }

        [Test]
        public void BuildMetaDataReturnsRequestDataSetMetaData()
        {
            DataSetMetaDataType metaData = CreateMetaData();
            var action = new PublishedActionDataType
            {
                RequestDataSetMetaData = metaData,
                ActionTargets = CreateTargets()
            };

            var source = new PublishedActionSource(action);

            Assert.That(source.BuildMetaData(), Is.SameAs(metaData));
            Assert.That(source.ActionTargets, Has.Count.EqualTo(action.ActionTargets.Count));
            Assert.That(source.ActionMethods, Is.Empty);
        }

        [Test]
        public void ActionMethodsReturnsConfiguredMethodBindings()
        {
            ArrayOf<ActionMethodDataType> methods =
            [
                new ActionMethodDataType
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_GetMonitoredItems
                }
            ];
            var action = new PublishedActionMethodDataType
            {
                RequestDataSetMetaData = CreateMetaData(),
                ActionTargets = CreateTargets(),
                ActionMethods = methods
            };

            var source = new PublishedActionSource(action);

            Assert.That(source.Action, Is.SameAs(action));
            Assert.That(source.ActionMethods[0].MethodId, Is.EqualTo(methods[0].MethodId));
        }

        [Test]
        public async Task SampleAsyncReturnsEmptySnapshotWithMetadataVersionAsync()
        {
            DataSetMetaDataType metaData = CreateMetaData();
            var action = new PublishedActionDataType
            {
                RequestDataSetMetaData = metaData,
                ActionTargets = CreateTargets()
            };
            var source = new PublishedActionSource(action);

            PublishedDataSetSnapshot snapshot = await source.SampleAsync(metaData).ConfigureAwait(false);

            Assert.That(snapshot.MetaDataVersion, Is.SameAs(metaData.ConfigurationVersion));
            Assert.That(snapshot.Fields, Is.Empty);
        }

        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ActionRequest",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 7,
                    MinorVersion = 2
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Input",
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static ArrayOf<ActionTargetDataType> CreateTargets()
        {
            return
            [
                new ActionTargetDataType
                {
                    ActionTargetId = 1,
                    Name = "Target",
                    Description = new LocalizedText("en-US", "Target action")
                }
            ];
        }
    }
}
