// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Tests.Connection
{
    [TestFixture]
    public sealed class DataValueSnapshotTests
    {
        [TestCaseSource(nameof(Values))]
        public void SnapshotPreservesDeclaredTypeAndExactWireValue(Variant value)
        {
            var context = ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { }));
            byte[] expected = DataValueCodec.EncodeVariant(value, EncodingFormat.Binary, context);

            Variant snapshot = DataValueCodec.Snapshot(value, context);

            Assert.That(snapshot.TypeInfo, Is.EqualTo(value.TypeInfo));
            Assert.That(snapshot.IsNull, Is.EqualTo(value.IsNull));
            Assert.That(DataValueCodec.EncodeVariant(snapshot, EncodingFormat.Binary, context), Is.EqualTo(expected));
        }

        [Test]
        public void SnapshotOwnsPrimitiveArrayStorageInBothDirections()
        {
            var context = ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { }));
            var source = Variant.From([7u, 11u]);
            Variant snapshot = DataValueCodec.Snapshot(source, context);
            Assert.That(source.TryGetValue(out ArrayOf<uint> original), Is.True);
            Assert.That(snapshot.TryGetValue(out ArrayOf<uint> detached), Is.True);
            Assert.That(MemoryMarshal.TryGetArray(original.Memory, out ArraySegment<uint> originalStorage), Is.True);
            Assert.That(MemoryMarshal.TryGetArray(detached.Memory, out ArraySegment<uint> detachedStorage), Is.True);
            uint[] originalArray = originalStorage.Array ?? throw new AssertionException("Expected original array.");
            uint[] detachedArray = detachedStorage.Array ?? throw new AssertionException("Expected copied array.");

            originalArray[originalStorage.Offset] = 99;
            detachedArray[detachedStorage.Offset + 1] = 55;

            Assert.That(original[0], Is.EqualTo(99u));
            Assert.That(original[1], Is.EqualTo(11u));
            Assert.That(detached[0], Is.EqualTo(7u));
            Assert.That(detached[1], Is.EqualTo(55u));
        }

        [Test]
        public void SnapshotHonorsTheEncodingContextArrayLimit()
        {
            var context = ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { }));
            context.MaxArrayLength = 2;
            var value = Variant.From([1u, 2u, 3u]);

            Assert.That(() => DataValueCodec.Snapshot(value, context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(() => DataValueCodec.Snapshot(value, null!), Throws.ArgumentNullException);
        }

        private static IEnumerable<TestCaseData> Values()
        {
            yield return new TestCaseData(Variant.Null).SetName("SnapshotPreservesNullVariant");
            yield return new TestCaseData(Variant.From(true)).SetName("SnapshotPreservesBoolean");
            yield return new TestCaseData(Variant.From(uint.MaxValue)).SetName("SnapshotPreservesUnsignedWidth");
            yield return new TestCaseData(Variant.From(long.MinValue)).SetName("SnapshotPreservesSignedWidth");
            yield return new TestCaseData(Variant.From(new EnumValue(3))).SetName("SnapshotPreservesEnumeration");
            yield return new TestCaseData(Variant.From((string)null!)).SetName("SnapshotPreservesNullString");
            yield return new TestCaseData(Variant.From(string.Empty)).SetName("SnapshotPreservesEmptyString");
            yield return new TestCaseData(Variant.From(ArrayOf<uint>.Null)).SetName("SnapshotPreservesNullArray");
            yield return new TestCaseData(Variant.From(ArrayOf<uint>.Empty)).SetName("SnapshotPreservesEmptyArray");
            yield return new TestCaseData(Variant.From([7u, 11u])).SetName("SnapshotPreservesArray");
            yield return new TestCaseData(Variant.From(default(ByteString))).SetName("SnapshotPreservesNullBytes");
            yield return new TestCaseData(Variant.From(ByteString.Empty)).SetName("SnapshotPreservesEmptyBytes");
            yield return new TestCaseData(Variant.From(ByteString.From("abc"u8))).SetName("SnapshotPreservesBytes");
            yield return new TestCaseData(Variant.From(
                MatrixOf<uint>.CreateFromArray(new uint[,] { { 1, 2 }, { 3, 4 } })))
                .SetName("SnapshotPreservesMatrixShape");
        }
    }
}
