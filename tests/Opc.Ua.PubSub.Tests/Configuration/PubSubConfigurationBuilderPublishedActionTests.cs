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
