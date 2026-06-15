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
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.MetaData
{
    /// <summary>
    /// Coverage for <see cref="DataSetMetaDataRegistry"/>: identity-keyed
    /// lookup with version classification, atomic re-register semantics,
    /// removal, key snapshots, and change-notification event raising.
    /// </summary>
    [TestFixture]
    [TestSpec("5.2.3", Summary = "DataSetMetaData identity and registration")]
    [TestSpec("6.2.9.4", Summary = "DataSetReader DataSetMetaData version classification")]
    [TestSpec("7.2.4.6.4", Summary = "DataSetMetaData NetworkMessage processing")]
    public class DataSetMetaDataRegistryTests
    {
        private static DataSetMetaDataKey NewKey(
            ushort writerGroupId = 100,
            ushort dataSetWriterId = 200,
            uint majorVersion = 1)
        {
            return new DataSetMetaDataKey(
                PublisherId.FromUInt16(42),
                writerGroupId,
                dataSetWriterId,
                Uuid.Empty,
                majorVersion);
        }

        private static DataSetMetaDataType NewMeta(
            uint majorVersion = 1,
            uint minorVersion = 0,
            string name = "DS1")
        {
            return new DataSetMetaDataType
            {
                Name = name,
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                }
            };
        }

        [Test]
        public void Constructor_DefaultLoggerIsAccepted()
        {
            var sut = new DataSetMetaDataRegistry();
            Assert.That(sut.Keys, Is.Empty);
        }

        [Test]
        public void Keys_EmptyBeforeAnyRegister()
        {
            var sut = new DataSetMetaDataRegistry();
            Assert.That(sut.Keys, Is.Empty);
        }

        [Test]
        public void Register_AddsKeyToSnapshot()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey();
            DataSetMetaDataType meta = NewMeta();

            sut.Register(key, meta);

            Assert.That(sut.Keys, Has.Count.EqualTo(1));
            Assert.That(sut.Keys, Has.Member(key));
        }

        [Test]
        public void Register_NullMetaDataThrows()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey();
            Assert.That(() => sut.Register(key, null!), Throws.ArgumentNullException);
        }

        [Test]
        public void TryGet_NotFoundReturnsNotFound()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey();
            MetaDataMatchResult result = sut.TryGet(key, out DataSetMetaDataType? meta);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(MetaDataMatchResult.NotFound));
                Assert.That(meta, Is.Null);
            });
        }

        [Test]
        public void TryGet_MatchingMajorVersionReturnsMatch()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey(majorVersion: 1);
            DataSetMetaDataType meta = NewMeta(majorVersion: 1);
            sut.Register(key, meta);

            MetaDataMatchResult result = sut.TryGet(key, out DataSetMetaDataType? out1);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
                Assert.That(out1, Is.SameAs(meta));
            });
        }

        [Test]
        public void TryGet_MajorVersionMismatchReturnsMajorVersionMismatch()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey storedKey = NewKey(majorVersion: 1);
            DataSetMetaDataType meta = NewMeta(majorVersion: 1);
            sut.Register(storedKey, meta);

            DataSetMetaDataKey lookupKey = NewKey(majorVersion: 2);
            MetaDataMatchResult result = sut.TryGet(lookupKey, out DataSetMetaDataType? out1);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(MetaDataMatchResult.MajorVersionMismatch));
                Assert.That(out1, Is.SameAs(meta), "registered meta returned for diagnostics");
            });
        }

        [Test]
        public void TryGet_PerComponentOverload_MatchOnVersion()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataType meta = NewMeta(majorVersion: 3, minorVersion: 7);
            sut.Register(NewKey(majorVersion: 3), meta);

            MetaDataMatchResult result = sut.TryGet(
                PublisherId.FromUInt16(42),
                100,
                200,
                majorVersion: 3,
                minorVersion: 7,
                out DataSetMetaDataType? out1);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
                Assert.That(out1, Is.SameAs(meta));
            });
        }

        [Test]
        public void TryGet_PerComponentOverload_MajorVersionMismatch()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataType meta = NewMeta(majorVersion: 3, minorVersion: 7);
            sut.Register(NewKey(majorVersion: 3), meta);

            MetaDataMatchResult result = sut.TryGet(
                PublisherId.FromUInt16(42),
                100,
                200,
                majorVersion: 99,
                minorVersion: 7,
                out _);
            Assert.That(result, Is.EqualTo(MetaDataMatchResult.MajorVersionMismatch));
        }

        [Test]
        public void TryGet_PerComponentOverload_MinorVersionMismatch()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataType meta = NewMeta(majorVersion: 3, minorVersion: 7);
            sut.Register(NewKey(majorVersion: 3), meta);

            MetaDataMatchResult result = sut.TryGet(
                PublisherId.FromUInt16(42),
                100,
                200,
                majorVersion: 3,
                minorVersion: 12,
                out DataSetMetaDataType? out1);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(MetaDataMatchResult.MinorVersionMismatch));
                Assert.That(out1, Is.SameAs(meta));
            });
        }

        [Test]
        public void TryGet_PerComponentOverload_NotFound()
        {
            var sut = new DataSetMetaDataRegistry();
            MetaDataMatchResult result = sut.TryGet(
                PublisherId.FromUInt16(42),
                100,
                200,
                majorVersion: 1,
                minorVersion: 0,
                out DataSetMetaDataType? out1);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(MetaDataMatchResult.NotFound));
                Assert.That(out1, Is.Null);
            });
        }

        [Test]
        public void TryGet_HandlesEntryWithNullConfigurationVersion()
        {
            var sut = new DataSetMetaDataRegistry();
            var meta = new DataSetMetaDataType
            {
                Name = "x"
            };
            sut.Register(NewKey(majorVersion: 0), meta);

            MetaDataMatchResult result = sut.TryGet(NewKey(majorVersion: 0), out _);
            Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
        }

        [Test]
        public void Register_TwiceForSameIdentityReplacesEntry()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key1 = NewKey(majorVersion: 1);
            DataSetMetaDataType meta1 = NewMeta(majorVersion: 1, name: "old");

            DataSetMetaDataKey key2 = NewKey(majorVersion: 2);
            DataSetMetaDataType meta2 = NewMeta(majorVersion: 2, name: "new");

            sut.Register(key1, meta1);
            sut.Register(key2, meta2);

            Assert.Multiple(() =>
            {
                Assert.That(sut.Keys, Has.Count.EqualTo(1), "identity replacement");
                MetaDataMatchResult r = sut.TryGet(key2, out DataSetMetaDataType? out1);
                Assert.That(r, Is.EqualTo(MetaDataMatchResult.Match));
                Assert.That(out1, Is.SameAs(meta2));
            });
        }

        [Test]
        public void Register_RaisesMetaDataChangedWithNullPreviousOnFirstRegister()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey();
            DataSetMetaDataType meta = NewMeta();

            DataSetMetaDataChangedEventArgs? raised = null;
            sut.MetaDataChanged += (_, e) => raised = e;

            sut.Register(key, meta);

            Assert.Multiple(() =>
            {
                Assert.That(raised, Is.Not.Null);
                Assert.That(raised!.Previous, Is.Null);
                Assert.That(raised.Current, Is.SameAs(meta));
                Assert.That(raised.Key, Is.EqualTo(key));
            });
        }

        [Test]
        public void Register_RaisesMetaDataChangedWithPreviousOnReplace()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key1 = NewKey(majorVersion: 1);
            DataSetMetaDataType meta1 = NewMeta(majorVersion: 1);
            DataSetMetaDataKey key2 = NewKey(majorVersion: 2);
            DataSetMetaDataType meta2 = NewMeta(majorVersion: 2);

            sut.Register(key1, meta1);
            DataSetMetaDataChangedEventArgs? raised = null;
            sut.MetaDataChanged += (_, e) => raised = e;

            sut.Register(key2, meta2);

            Assert.Multiple(() =>
            {
                Assert.That(raised, Is.Not.Null);
                Assert.That(raised!.Previous, Is.SameAs(meta1));
                Assert.That(raised.Current, Is.SameAs(meta2));
            });
        }

        [Test]
        public void Register_SwallowsHandlerExceptions()
        {
            var sut = new DataSetMetaDataRegistry();
            sut.MetaDataChanged += (_, _) => throw new InvalidOperationException("boom");

            Assert.That(
                () => sut.Register(NewKey(), NewMeta()),
                Throws.Nothing);
        }

        [Test]
        public void Remove_DeletesEntry()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey();
            sut.Register(key, NewMeta());

            sut.Remove(key);

            Assert.Multiple(() =>
            {
                Assert.That(sut.Keys, Is.Empty);
                MetaDataMatchResult r = sut.TryGet(key, out _);
                Assert.That(r, Is.EqualTo(MetaDataMatchResult.NotFound));
            });
        }

        [Test]
        public void Remove_NonexistentKeyIsNoOp()
        {
            var sut = new DataSetMetaDataRegistry();
            DataSetMetaDataKey key = NewKey();
            Assert.That(() => sut.Remove(key), Throws.Nothing);
        }

        [Test]
        public void Keys_ReturnsIndependentSnapshot()
        {
            var sut = new DataSetMetaDataRegistry();
            sut.Register(NewKey(writerGroupId: 1, dataSetWriterId: 1), NewMeta());
            sut.Register(NewKey(writerGroupId: 1, dataSetWriterId: 2), NewMeta());

            IReadOnlyCollection<DataSetMetaDataKey> snapshot1 = sut.Keys;
            sut.Register(NewKey(writerGroupId: 1, dataSetWriterId: 3), NewMeta());
            IReadOnlyCollection<DataSetMetaDataKey> snapshot2 = sut.Keys;

            Assert.Multiple(() =>
            {
                Assert.That(snapshot1, Has.Count.EqualTo(2));
                Assert.That(snapshot2, Has.Count.EqualTo(3));
            });
        }
    }
}
