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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Contract tests for the in-memory PubSub HA providers.
    /// </summary>
    [TestFixture]
    public class InMemoryPubSubProviderTests
    {
        [Test]
        [Description("OPC 10000-14 §9.1.6: configuration versions are externally persisted per PublishedDataSet.")]
        public async Task ConfigurationStorePersistsPublishedDataSetConfigurationVersionAsync()
        {
            var store = new InMemoryPubSubConfigurationStore(new PubSubConfigurationDataType
            {
                PublishedDataSets =
                [
                    new PublishedDataSetDataType
                    {
                        Name = "DataSet1",
                        DataSetMetaData = new DataSetMetaDataType()
                    }
                ]
            });
            var version = new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 2
            };

            await store.SetPublishedDataSetConfigurationVersionAsync("DataSet1", version).ConfigureAwait(false);

            ConfigurationVersionDataType? actual =
                await store.GetPublishedDataSetConfigurationVersionAsync("DataSet1").ConfigureAwait(false);
            PubSubConfigurationDataType configuration = await store.LoadAsync().ConfigureAwait(false);

            Assert.That(actual?.MajorVersion, Is.EqualTo(version.MajorVersion));
            Assert.That(actual?.MinorVersion, Is.EqualTo(version.MinorVersion));
            Assert.That(
                configuration.PublishedDataSets[0].DataSetMetaData.ConfigurationVersion?.MajorVersion,
                Is.EqualTo(version.MajorVersion));
        }

        [Test]
        [Description("OPC 10000-14 §9.1.6: HA id allocation is monotonic and shared by server instances.")]
        public async Task IdAllocatorAllocatesMonotonicReservedIdsAndFileHandlesAsync()
        {
            var allocator = new InMemoryPubSubIdAllocator();

            ArrayOf<uint> reservedIds = await allocator.ReserveIdsAsync(3).ConfigureAwait(false);
            uint firstHandle = await allocator.AllocateFileHandleAsync().ConfigureAwait(false);
            uint secondHandle = await allocator.AllocateFileHandleAsync().ConfigureAwait(false);

            Assert.That(reservedIds, Is.EqualTo(new uint[] { 1, 2, 3 }));
            Assert.That(firstHandle, Is.EqualTo(1u));
            Assert.That(secondHandle, Is.EqualTo(2u));
        }

        [Test]
        [Description("OPC 10000-14 Table 2: component PubSubState is externally persisted for HA resume.")]
        public async Task RuntimeStateStorePersistsComponentStateAsync()
        {
            var store = new InMemoryPubSubRuntimeStateStore();

            await store.SetStateAsync("pubsub:connection:Connection1", PubSubState.Operational).ConfigureAwait(false);

            PubSubState? state =
                await store.GetStateAsync("pubsub:connection:Connection1").ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        [Description("OPC 10000-14 §8.3.1: SKS SecurityGroup key material can be externalized.")]
        public async Task SecurityKeyStorePersistsSecurityGroupsAsync()
        {
            var store = new InMemoryPubSubSecurityKeyStore();
            var group = new SksSecurityGroup(
                "Group1",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                1,
                1,
                []);

            await store.SaveSecurityGroupAsync(group).ConfigureAwait(false);

            ArrayOf<string> groupIds = await store.GetSecurityGroupIdsAsync().ConfigureAwait(false);
            SksSecurityGroup? actual = await store.GetSecurityGroupAsync("Group1").ConfigureAwait(false);
            bool removed = await store.RemoveSecurityGroupAsync("Group1").ConfigureAwait(false);

            Assert.That(groupIds, Is.EqualTo(s_expectedGroupIds));
            Assert.That(actual, Is.SameAs(group));
            Assert.That(removed, Is.True);
        }

        private static readonly string[] s_expectedGroupIds = ["Group1"];
    }
}
