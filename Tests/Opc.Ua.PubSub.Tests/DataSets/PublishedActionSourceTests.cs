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
