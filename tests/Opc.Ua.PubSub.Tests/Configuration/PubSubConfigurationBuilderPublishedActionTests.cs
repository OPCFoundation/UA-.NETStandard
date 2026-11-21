// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Tests for PublishedAction support in <see cref="PubSubConfigurationBuilder"/>.
    /// </summary>
    [TestFixture]
    public sealed class PubSubConfigurationBuilderPublishedActionTests
    {
        [Test]
        public void AddPublishedActionCreatesPublishedActionDataSet()
        {
            DataSetMetaDataType requestMetaData = CreateRequestMetaData();
            ArrayOf<ActionTargetDataType> targets = CreateTargets();

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddPublishedAction("ActionDataSet", requestMetaData, targets)
                .Build();

            PublishedDataSetDataType publishedDataSet = configuration.PublishedDataSets[0];
            Assert.That(publishedDataSet.Name, Is.EqualTo("ActionDataSet"));
            Assert.That(publishedDataSet.DataSetMetaData, Is.SameAs(requestMetaData));
            Assert.That(
                publishedDataSet.DataSetSource.TryGetValue(out PublishedActionDataType? action),
                Is.True);
            Assert.That(action, Is.Not.Null);
            Assert.That(action!.RequestDataSetMetaData, Is.SameAs(requestMetaData));
            Assert.That(action.ActionTargets[0].ActionTargetId, Is.EqualTo(targets[0].ActionTargetId));
        }

        [Test]
        public void AddPublishedActionWithMethodsCreatesPublishedActionMethodDataSet()
        {
            DataSetMetaDataType requestMetaData = CreateRequestMetaData();
            ArrayOf<ActionTargetDataType> targets = CreateTargets();
            ArrayOf<ActionMethodDataType> methods =
            [
                new ActionMethodDataType
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_GetMonitoredItems
                }
            ];

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddPublishedAction("MethodActionDataSet", requestMetaData, targets, methods)
                .Build();

            PublishedDataSetDataType publishedDataSet = configuration.PublishedDataSets[0];
            Assert.That(
                publishedDataSet.DataSetSource.TryGetValue(out PublishedActionMethodDataType? action),
                Is.True);
            Assert.That(action, Is.Not.Null);
            Assert.That(action!.RequestDataSetMetaData, Is.SameAs(requestMetaData));
            Assert.That(action.ActionTargets[0].ActionTargetId, Is.EqualTo(targets[0].ActionTargetId));
            Assert.That(action.ActionMethods[0].MethodId, Is.EqualTo(methods[0].MethodId));
        }

        [Test]
        public void AddPublishedActionWithNullRequestMetadataThrowsArgumentNullException()
        {
            var builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddPublishedAction("ActionDataSet", null!, CreateTargets()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("requestMetaData"));
        }

        private static DataSetMetaDataType CreateRequestMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ActionRequest",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
        }

        private static ArrayOf<ActionTargetDataType> CreateTargets()
        {
            return
            [
                new ActionTargetDataType
                {
                    ActionTargetId = 10,
                    Name = "Target",
                    Description = new LocalizedText("en-US", "Target action")
                }
            ];
        }
    }
}
