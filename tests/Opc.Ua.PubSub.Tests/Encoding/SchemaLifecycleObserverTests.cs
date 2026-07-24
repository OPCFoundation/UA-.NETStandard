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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture]
    [Category("PubSub")]
    public class SchemaLifecycleObserverTests
    {
        private sealed class FakeMetaDataRegistry : IDataSetMetaDataRegistry
        {
            public readonly Dictionary<DataSetMetaDataKey, DataSetMetaDataType> Store = [];

            public MetaDataMatchResult TryGet(in DataSetMetaDataKey key, out DataSetMetaDataType? metaData)
            {
                if (Store.TryGetValue(key, out DataSetMetaDataType? stored))
                {
                    metaData = stored;
                    return MetaDataMatchResult.Match;
                }
                metaData = null;
                return MetaDataMatchResult.NotFound;
            }

            public void Register(in DataSetMetaDataKey key, DataSetMetaDataType metaData)
            {
                Store[key] = metaData;
            }

            public void Remove(in DataSetMetaDataKey key)
            {
                Store.Remove(key);
            }

            public ArrayOf<DataSetMetaDataKey> Keys => new(Store.Keys.ToArray());

#pragma warning disable CS0067 // the fake never raises the change event
            public event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged;
#pragma warning restore CS0067
        }

        private static DataSetMetaDataKey Key(uint major = 100)
        {
            return new DataSetMetaDataKey(default, 1, 1, default, major);
        }

        private static ByteString SchemaId(byte value)
        {
            return new ByteString(new byte[] { value });
        }

        private static FakeMetaDataRegistry SeedRegistry(DataSetMetaDataKey key, uint major, uint minor)
        {
            var registry = new FakeMetaDataRegistry();
            registry.Register(key, new DataSetMetaDataType
            {
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = major,
                    MinorVersion = minor
                }
            });
            return registry;
        }

        [Test]
        public async Task FirstSchemaDoesNotAdvanceVersionAsync()
        {
            DataSetMetaDataKey key = Key();
            FakeMetaDataRegistry registry = SeedRegistry(key, 100, 5);
            var observer = new SchemaLifecycleObserver(registry);

            await observer.OnSchemaProducedAsync(
                new SchemaChangeNotification(key, SchemaId(1), "avro", "dest"));

            Assert.That(registry.Store[key].ConfigurationVersion.MinorVersion, Is.EqualTo(5u));
            Assert.That(registry.Store[key].ConfigurationVersion.MajorVersion, Is.EqualTo(100u));
        }

        [Test]
        public async Task GrownSchemaAdvancesMinorVersionKeepingMajorAsync()
        {
            DataSetMetaDataKey key = Key();
            FakeMetaDataRegistry registry = SeedRegistry(key, 100, 5);
            var observer = new SchemaLifecycleObserver(registry);

            await observer.OnSchemaProducedAsync(
                new SchemaChangeNotification(key, SchemaId(1), "avro", "dest"));
            await observer.OnSchemaProducedAsync(
                new SchemaChangeNotification(key, SchemaId(2), "avro", "dest"));

            ConfigurationVersionDataType version = registry.Store[key].ConfigurationVersion;
            Assert.That(version.MajorVersion, Is.EqualTo(100u), "MajorVersion must not change on an append-only growth");
            Assert.That(version.MinorVersion, Is.Not.EqualTo(5u), "MinorVersion must advance on an append-only growth");
        }

        [Test]
        public async Task RepeatedSameSchemaDoesNotAdvanceAsync()
        {
            DataSetMetaDataKey key = Key();
            FakeMetaDataRegistry registry = SeedRegistry(key, 100, 5);
            var observer = new SchemaLifecycleObserver(registry);

            await observer.OnSchemaProducedAsync(
                new SchemaChangeNotification(key, SchemaId(1), "avro", "dest"));
            await observer.OnSchemaProducedAsync(
                new SchemaChangeNotification(key, SchemaId(1), "avro", "dest"));

            Assert.That(registry.Store[key].ConfigurationVersion.MinorVersion, Is.EqualTo(5u));
        }
    }
}
