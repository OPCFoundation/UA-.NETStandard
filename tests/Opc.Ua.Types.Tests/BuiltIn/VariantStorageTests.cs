/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using ObjectLayoutInspector;
#endif

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Pins the boxed representation's semantics independently of scalar constructor storage.
    /// </summary>
    [TestFixture]
    [Category("VariantStorage")]
    public sealed class VariantStorageTests
    {
        public static IEnumerable<object> Values
        {
            get
            {
                foreach (ushort ns in new ushort[] { 0, 1, ushort.MaxValue })
                {
                    yield return new QualifiedName(null, ns);
                    yield return new QualifiedName(string.Empty, ns);
                    yield return new QualifiedName("same", ns);
                    yield return new QualifiedName("different", ns);
                    yield return new NodeId(0u, ns);
                    yield return new NodeId(uint.MaxValue, ns);
                    yield return new NodeId(null, ns);
                    yield return new NodeId(string.Empty, ns);
                    yield return new NodeId("same", ns);
                    yield return new NodeId(Guid.Empty, ns);
                    yield return new NodeId(new Guid("00112233-4455-6677-8899-aabbccddeeff"), ns);
                    yield return new NodeId(ByteString.Empty, ns);
                    yield return new NodeId(default(ByteString), ns);
                    yield return new NodeId(new ByteString(new byte[] { 8, 1, 2, 9 }.AsMemory(1, 2)), ns);
                }
                yield return default(ByteString);
                yield return ByteString.Empty;
                yield return new ByteString(new byte[] { 8, 1, 2, 9 }.AsMemory(1, 2));
                yield return default(LocalizedText);
                yield return new LocalizedText(string.Empty);
                yield return new LocalizedText("same");
                yield return new LocalizedText(string.Empty, "same");
                yield return new LocalizedText("en", "same");
                yield return new LocalizedText("key", "en", "Value {0}", 7);
                yield return new LocalizedText("key", null, "Value {0}", 7);
                yield return new LocalizedText(new Dictionary<string, string>
                {
                    ["en"] = "same",
                    ["de"] = "gleich"
                });
            }
        }

        [TestCaseSource(nameof(Values))]
        public void ScalarIngressExtractionBoxingCopyAndDefaultsMatchBoxedContract(object input)
        {
            Variant actual = Typed(input);
            Variant expected = Boxed(input);
            AssertEquivalent(actual, expected);
            AssertEquivalent(VariantHelper.CastFrom(input), expected);
            AssertEquivalent(VariantTests.CreateLegacyStorageValue(input), expected);
            Assert.That(VariantHelper.TryCastFrom(input, out Variant generic), Is.True);
            AssertEquivalent(generic, expected);
            AssertEquivalent(actual.Copy(), expected);
            Assert.That(VariantTests.GetLegacyStorageValue(actual),
                Is.EqualTo(VariantTests.GetLegacyStorageValue(expected)));
            Assert.That(actual.Raw?.GetType(), Is.EqualTo(input.GetType()));
            Assert.That(actual.Raw, Is.EqualTo(input));
            foreach (Variant.BoxingBehavior mode in new[]
            {
                Variant.BoxingBehavior.None, Variant.BoxingBehavior.Legacy, Variant.BoxingBehavior.LegacyWithMatrix
            })
            {
                Assert.That(actual.AsBoxedObject(mode), Is.EqualTo(expected.AsBoxedObject(mode)));
                Assert.That(actual.AsBoxedObject(mode)?.GetType(),
                    Is.EqualTo(expected.AsBoxedObject(mode)?.GetType()));
            }
            Assert.That(actual.TryGetValue(out double wrong), Is.False);
            Assert.That(wrong, Is.Zero);
            Assert.That(actual.GetDouble(19), Is.EqualTo(19));
            Assert.That(actual.TryGetValue(out Variant self), Is.True);
            Assert.That(self, Is.EqualTo(actual));
            Assert.Throws<FormatException>(() => actual.ToString("invalid", CultureInfo.InvariantCulture));
            switch (input)
            {
                case QualifiedName value:
                    AssertTyped(actual, value, default(VariantBuilder));
                    Assert.That(actual.GetQualifiedName().Name, Is.SameAs(value.Name));
                    Assert.That(actual.GetQualifiedName().NamespaceIndex, Is.EqualTo(value.NamespaceIndex));
                    Assert.That(actual.ConvertToQualifiedName(), Is.EqualTo(expected));
                    break;
                case NodeId value:
                    AssertTyped(actual, value, default(VariantBuilder));
                    Assert.That(actual.GetNodeId().IdType, Is.EqualTo(value.IdType));
                    Assert.That(actual.GetNodeId().NamespaceIndex, Is.EqualTo(value.NamespaceIndex));
                    Assert.That(actual.ConvertToNodeId(), Is.EqualTo(expected));
                    break;
                case ByteString value:
                    AssertTyped(actual, value, default(VariantBuilder));
                    AssertMemoryIdentity(actual.GetByteString().Memory, value.Memory);
                    Assert.That(actual.GetByteString().IsNull, Is.EqualTo(value.IsNull));
                    Assert.That(actual.GetByteArray().Span.ToArray(), Is.EqualTo(value.Span.ToArray()));
                    Assert.That(actual.GetByteArray().IsNull, Is.EqualTo(expected.GetByteArray().IsNull));
                    Assert.That(new Variant(new ArrayOf<byte>(value.Memory)).GetByteString().Span.ToArray(),
                        Is.EqualTo(value.Span.ToArray()));
                    break;
                case LocalizedText value:
                    AssertTyped(actual, value, default(VariantBuilder));
                    Assert.That(actual.GetLocalizedText().Text, Is.EqualTo(value.Text));
                    Assert.That(actual.GetLocalizedText().Locale, Is.EqualTo(value.Locale));
                    Assert.That(actual.GetLocalizedText().Translations, Is.SameAs(value.Translations));
                    break;
            }
        }

        [Test]
        public void PairwiseOperationsPreserveBothDirectionsIncludingCrossTypeExceptions()
        {
            var pairs = Values.Select(value => (Actual: Typed(value), Expected: Boxed(value))).ToList();
            Variant[] controls =
            [
                default, new Variant(0), new Variant(1), new Variant(65535), new Variant(1u),
                new Variant(1L), new Variant(1UL), new Variant(true), new Variant((short)1),
                new Variant((ushort)1), new Variant((byte)1), new Variant((sbyte)1),
                new Variant(1f), new Variant(1d), new Variant(default(DateTimeUtc)),
                new Variant("same"), new Variant(new Uuid(Guid.Empty)),
                new Variant(-1L), new Variant(ulong.MaxValue),
                new Variant(new EnumValue(-1, "all")), new Variant(new EnumValue(1, "one")),
                new Variant(new ExpandedNodeId(new NodeId(1u, 2))),
                new Variant(new StatusCode(7, "same")), new Variant(default(ExtensionObject)),
                new Variant(ArrayOf<byte>.Empty), new Variant(ArrayOf<QualifiedName>.Empty),
                new Variant(MatrixOf<NodeId>.Null)
            ];
            pairs.AddRange(controls.Select(value => (value, value)));
            foreach (TypeInfo type in new[]
            {
                TypeInfo.Scalars.QualifiedName, TypeInfo.Scalars.NodeId,
                TypeInfo.Scalars.ByteString, TypeInfo.Scalars.LocalizedText
            })
            {
                var empty = Variant.CreateDefault(type);
                pairs.Add((empty, empty));
            }
            foreach ((Variant Actual, Variant Expected) in pairs)
            {
                foreach ((Variant Actual, Variant Expected) right in pairs)
                {
                    Assert.That(Actual.Equals(right.Actual),
                        Is.EqualTo(Expected.Equals(right.Expected)), "strict equality");
                    Assert.That(Actual.ValueEquals(right.Actual),
                        Is.EqualTo(Expected.ValueEquals(right.Expected)), "value equality");
                    Assert.That(Observe(() => Actual.CompareTo(right.Actual)),
                        Is.EqualTo(Observe(() => Expected.CompareTo(right.Expected))), "comparison");
                    Assert.That(Observe(() => Actual & right.Actual),
                        Is.EqualTo(Observe(() => Expected & right.Expected)), "bitwise AND");
                    Assert.That(Observe(() => Actual | right.Actual),
                        Is.EqualTo(Observe(() => Expected | right.Expected)), "bitwise OR");
                }
            }
        }

        [Test]
        public void QualifiedNamesKeepEveryNamespaceIndex()
        {
            for (int index = 0; index <= ushort.MaxValue; index++)
            {
                var input = new QualifiedName("shared", (ushort)index);
                var value = Variant.From(input);
                Assert.That(value.TryGetValue(out QualifiedName actual), Is.True);
                Assert.That(actual.NamespaceIndex, Is.EqualTo((ushort)index));
                Assert.That(actual.Name, Is.SameAs(input.Name));
                Assert.That(value.TypeInfo, Is.EqualTo(TypeInfo.Scalars.QualifiedName));
            }
        }

        [TestCaseSource(nameof(Values))]
        public void ExplicitTypeInfoRetainsMismatchedPayloadAndExceptionalBehavior(object input)
        {
            foreach (TypeInfo type in new[]
            {
                TypeInfo.Unknown, TypeInfo.Scalars.Int32, TypeInfo.Scalars.String,
                TypeInfo.Scalars.NodeId, TypeInfo.Scalars.ByteString, TypeInfo.Scalars.QualifiedName,
                TypeInfo.Scalars.LocalizedText, TypeInfo.Arrays.NodeId,
                new TypeInfo(BuiltInType.QualifiedName, 2), new TypeInfo(BuiltInType.NodeId, -3)
            })
            {
                Variant actual = VariantTests.CreateLegacyStorageValue(input, type);
                var expected = new Variant(default, type, input);
                Assert.That(actual.TypeInfo, Is.EqualTo(type));
                Assert.That(actual.IsNull, Is.EqualTo(expected.IsNull));
                Assert.That(actual.GetHashCode(), Is.EqualTo(expected.GetHashCode()));
                Assert.That(actual.ValueIsDefaultOrNull, Is.EqualTo(expected.ValueIsDefaultOrNull));
                Assert.That(Observe(() => actual.ToString()), Is.EqualTo(Observe(() => expected.ToString())));
                Assert.That(Observe(() => actual.Equals(actual)), Is.EqualTo(Observe(() => expected.Equals(expected))));
                Assert.That(actual.ValueEquals(actual), Is.EqualTo(expected.ValueEquals(expected)));
                Assert.That(Observe(() => actual.GetNodeId()), Is.EqualTo(Observe(() => expected.GetNodeId())));
                Assert.That(Observe(() => actual.GetQualifiedName()),
                    Is.EqualTo(Observe(() => expected.GetQualifiedName())));
                Assert.That(Observe(() => actual.GetByteString()),
                    Is.EqualTo(Observe(() => expected.GetByteString())));
                Assert.That(Observe(() => actual.GetLocalizedText()),
                    Is.EqualTo(Observe(() => expected.GetLocalizedText())));
                Assert.That(Observe(() => actual.Raw), Is.EqualTo(Observe(() => expected.Raw)));
            }
        }

        [TestCase(1UL)]
        [TestCase(4294967296UL)]
        [TestCase(18446744073709551615UL)]
        public void ExplicitPrimitiveTypeInfoCannotBeConfusedWithPackedMetadata(ulong bits)
        {
            foreach (TypeInfo type in new[]
            {
                TypeInfo.Scalars.QualifiedName, TypeInfo.Scalars.NodeId,
                TypeInfo.Scalars.ByteString, TypeInfo.Scalars.LocalizedText
            })
            {
                Variant actual = VariantTests.CreateLegacyStorageValue(bits, type);
                var expected = new Variant(new Variant.Union { UInt64 = bits }, type);
                Assert.That(actual.GetNodeId().IsNull, Is.True);
                Assert.That(actual.GetQualifiedName().Name, Is.Null);
                Assert.That(actual.GetHashCode(), Is.Zero);
                Assert.That(actual.ToString(), Is.EqualTo("<null>"));
                // A UInt64 is not comparable to a QualifiedName/NodeId/ByteString/
                // LocalizedText variant - the raw union payload must never be used.
                Assert.That(new Variant(1UL).CompareTo(actual), Is.EqualTo(int.MinValue));
                Assert.That(actual.GetNodeId(), Is.EqualTo(expected.GetNodeId()));
                Assert.That(actual.GetQualifiedName(), Is.EqualTo(expected.GetQualifiedName()));
                Assert.That(actual.GetByteString(), Is.EqualTo(expected.GetByteString()));
                Assert.That(actual.GetLocalizedText(), Is.EqualTo(expected.GetLocalizedText()));
                Assert.That(actual.GetHashCode(), Is.EqualTo(expected.GetHashCode()));
                Assert.That(actual.ToString(), Is.EqualTo(expected.ToString()));
                Assert.That(actual.ValueEquals(default), Is.EqualTo(expected.ValueEquals(default)));
                Assert.That(new Variant(1UL).CompareTo(actual), Is.EqualTo(new Variant(1UL).CompareTo(expected)));
                // The bitwise operators read the right hand operand through its
                // own type, so a non integer operand yields a null variant.
                Assert.That((new Variant(ulong.MaxValue) & actual).IsNull, Is.True);
                Assert.That((new Variant(0UL) | actual).IsNull, Is.True);
            }
        }

        [Test]
        public void BorrowedOpaquePayloadKeepsMutationVisibilityAndInsertionHash()
        {
            byte[] buffer = [9, 1, 2, 8];
            var bytes = new ByteString(buffer.AsMemory(1, 2));
            var nodeId = new NodeId(bytes, ushort.MaxValue);
            int insertedHash = nodeId.GetHashCode();
            var byteVariant = new Variant(bytes);
            var nodeVariant = new Variant(nodeId);
            buffer[1] = 7;
            Assert.That(byteVariant.GetByteString()[0], Is.EqualTo(7));
            Assert.That(byteVariant.GetHashCode(), Is.EqualTo(bytes.GetHashCode()));
            NodeId extracted = nodeVariant.GetNodeId();
            Assert.That(extracted.TryGetValue(out ByteString opaque), Is.True);
            AssertMemoryIdentity(opaque.Memory, bytes.Memory);
            Assert.That(opaque[0], Is.EqualTo(7));
            Assert.That(extracted.GetHashCode(), Is.EqualTo(insertedHash));
            Assert.That(extracted, Is.EqualTo(nodeId));
            Assert.That(nodeId, Is.EqualTo(extracted));
            Assert.That(nodeVariant.Copy().GetNodeId().GetHashCode(), Is.EqualTo(insertedHash));
        }

        [Test]
        public void NodeIdStorageRetainsCompleteInnerStateAndExistingIdentifierBoxes()
        {
            foreach (NodeId input in Values.OfType<NodeId>())
            {
                input.GetRawState(out object identifier, out NodeId.Inner inner);
                inner.Reserved = 173;
                NodeId raw = NodeId.SetRawState(identifier, inner);
                NodeId extracted = new Variant(raw).GetNodeId();
                extracted.GetRawState(out object restoredIdentifier, out NodeId.Inner restored);
                Assert.That(restoredIdentifier, Is.SameAs(identifier));
                Assert.That(restored.Numeric, Is.EqualTo(inner.Numeric));
                Assert.That(restored.NamespaceIdx, Is.EqualTo(inner.NamespaceIdx));
                Assert.That(restored.Type, Is.EqualTo(inner.Type));
                Assert.That(restored.Reserved, Is.EqualTo(173));
                Assert.That(extracted.GetHashCode(), Is.EqualTo(raw.GetHashCode()));
            }
        }

        [Test]
        public void TextOnlyEligibilityUsesRawLocaleAndTranslationState()
        {
            foreach (string text in new[] { null, string.Empty, "raw" })
            {
                var plain = new LocalizedText(text);
                Assert.That(plain.TryGetTextOnly(out string raw), Is.True);
                Assert.That(raw, Is.SameAs(text));
                var emptyLocale = new LocalizedText(string.Empty, text);
                Assert.That(emptyLocale.TryGetTextOnly(out _), Is.False);
                Assert.That(new Variant(emptyLocale).GetLocalizedText().Locale, Is.EqualTo(string.Empty));
            }
            var formatted = new LocalizedText("key", null, "Value {0}", 7);
            Assert.That(formatted.TryGetTextOnly(out _), Is.False);
            LocalizedText extracted = new Variant(formatted).GetLocalizedText();
            Assert.That(extracted.TranslationInfo, Is.EqualTo(formatted.TranslationInfo));
            Assert.That(extracted.Text, Is.EqualTo("Value 7"));
        }

        [Test]
        public void MemoryManagerSlicesRetainOwnerFlagsAndLifetime()
        {
            using var manager = new TrackingMemoryManager();
            var bytes = new ByteString(manager.Memory.Slice(1, 3).Slice(1, 2));
            var actual = new Variant(bytes);
            AssertMemoryIdentity(actual.GetByteString().Memory, bytes.Memory);
            Assert.That(actual.GetByteString().Span.ToArray(), Is.EqualTo(new byte[] { 2, 3 }));
            manager.GetSpan()[2] = 42;
            Assert.That(actual.GetByteString()[0], Is.EqualTo(42));
            Assert.That(actual.GetHashCode(), Is.EqualTo(bytes.GetHashCode()));
            ((IDisposable)manager).Dispose();
            Assert.Throws<ObjectDisposedException>(() => _ = actual.GetByteString().Span.Length);
        }

        [Test]
        public void ByteStringAndByteArrayConversionsKeepTheirExistingCopyOwnership()
        {
            byte[] source = [9, 1, 2, 8];
            var bytes = new Variant(new ByteString(source.AsMemory(1, 2)));
            var array = new Variant(new ArrayOf<byte>(source.AsMemory(1, 2)));
            ArrayOf<byte> copiedArray = bytes.GetByteArray();
            ByteString copiedBytes = array.GetByteString();
            source[1] = 42;
            Assert.That(copiedArray[0], Is.EqualTo(1));
            Assert.That(copiedBytes[0], Is.EqualTo(1));
            Assert.That(bytes.GetByteString()[0], Is.EqualTo(42));
            Assert.That(array.GetByteArray()[0], Is.EqualTo(42));
        }

        [TestCaseSource(nameof(Values))]
        public void BinaryCodecAndNestedValuesRetainSemanticTypes(object input)
        {
            Variant value = Typed(input);
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(context);
            encoder.WriteVariant(null, value);
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer(), context);
            Variant decoded = decoder.ReadVariant(null);
            Assert.That(decoded.TypeInfo, Is.EqualTo(value.TypeInfo));
            Assert.That(decoded, Is.EqualTo(value));
            var data = new DataValue(value);
            var nested = Variant.From(data);
            Assert.That(nested.GetDataValue().WrappedValue, Is.EqualTo(value));
            Assert.That(nested.Copy().GetDataValue().WrappedValue, Is.EqualTo(value));
        }

        [Test]
        public void ArraysMatricesAndGenericPropertiesUseTheSameScalarContract()
        {
            CheckContainers(new QualifiedName("container", 7), default(VariantBuilder),
                default(VariantBuilder), default(VariantBuilder));
            CheckContainers(new NodeId("container", 7), default(VariantBuilder),
                default(VariantBuilder), default(VariantBuilder));
            CheckContainers(new ByteString(new byte[] { 1, 2 }), default(VariantBuilder),
                default(VariantBuilder), default(VariantBuilder));
            CheckContainers(new LocalizedText("container"), default(VariantBuilder),
                default(VariantBuilder), default(VariantBuilder));
        }

        private static void CheckContainers<T, TBuilder>(
            T input,
            TBuilder scalarBuilder,
            IVariantBuilder<ArrayOf<T>> arrayBuilder,
            IVariantBuilder<MatrixOf<T>> matrixBuilder)
            where TBuilder : struct, IVariantBuilder<T>
        {
            var property = PropertyState<T>.With<TBuilder>(null, input);
            Assert.That(property.Value, Is.EqualTo(input));
            Assert.That(((BaseVariableState)property).Value, Is.EqualTo(scalarBuilder.WithValue(input)));
            ArrayOf<T> array = [input, input];
            Variant arrayValue = arrayBuilder.WithValue(array);
            Assert.That(arrayBuilder.GetValue(arrayValue), Is.EqualTo(array));
            Assert.That(arrayValue.Copy(), Is.EqualTo(arrayValue));
            var matrix = array.Memory.ToMatrixOf([1, 2]);
            Variant matrixValue = matrixBuilder.WithValue(matrix);
            Assert.That(matrixBuilder.GetValue(matrixValue), Is.EqualTo(matrix));
            Assert.That(matrixValue.Copy(), Is.EqualTo(matrixValue));
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            using var encoder = new JsonEncoder(context);
            encoder.WriteVariant("array", arrayValue);
            encoder.WriteVariant("matrix", matrixValue);
            using var decoder = new JsonDecoder(encoder.CloseAndReturnText(), context);
            Assert.That(decoder.ReadVariant("array"), Is.EqualTo(arrayValue));
            Assert.That(decoder.ReadVariant("matrix"), Is.EqualTo(matrixValue));
        }

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        [Test]
        public void PinnedArrayMemoryRetainsRawOwnerIndexAndLength()
        {
            byte[] buffer = GC.AllocateArray<byte>(8, pinned: true);
            buffer[3] = 42;
            var input = new ByteString(
                System.Runtime.InteropServices.MemoryMarshal.CreateFromPinnedArray(buffer, 2, 4).Slice(1, 2));
            ByteString actual = Variant.From(input).GetByteString();
            ReadOnlyMemory before = ReadOnlyMemoryHelper.From(in input);
            ReadOnlyMemory after = ReadOnlyMemoryHelper.From(in actual);
            Assert.That(after.Object, Is.SameAs(before.Object));
            Assert.That(after.Index, Is.EqualTo(before.Index));
            Assert.That(after.Length, Is.EqualTo(before.Length));
            Assert.That(actual[0], Is.EqualTo(42));
        }
#elif NET_STANDARD_TESTS
        [Test]
        public void NetStandardByteStringFallbackPreservesTheOriginalSlice()
        {
            byte[] buffer = [8, 1, 2, 9];
            var input = new ByteString(buffer.AsMemory(1, 2));

            Assert.That(Variant.From(input).TryGetValue(out ByteString actual), Is.True);
            Assert.That(actual.Memory, Is.EqualTo(input.Memory));
            buffer[1] = 42;
            Assert.That(actual[0], Is.EqualTo(42));
        }
#endif

#if NET8_0_OR_GREATER
        [Test]
        public void LayoutKeepsThreeFieldsAndExpectedX64Stride()
        {
            if (Environment.Is64BitProcess)
            {
                Assert.That(Unsafe.SizeOf<Variant>(), Is.EqualTo(24));
            }
            var layout = TypeLayout.GetLayout<Variant>();
            Assert.That(layout.Fields.OfType<FieldLayout>().Count(), Is.EqualTo(3));
        }
#endif

        private static void AssertTyped<T>(Variant variant, T value, IVariantBuilder<T> builder)
        {
            Assert.That(variant.TryCastTo(out T extracted), Is.True);
            Assert.That(extracted, Is.EqualTo(value));
            Assert.That(builder.GetValue(variant), Is.EqualTo(value));
            Assert.That(builder.WithValue(value), Is.EqualTo(variant));
        }

        private static void AssertEquivalent(Variant actual, Variant expected)
        {
            Assert.That(actual.TypeInfo, Is.EqualTo(expected.TypeInfo));
            Assert.That(actual.IsNull, Is.EqualTo(expected.IsNull));
            Assert.That(actual.ValueIsDefaultOrNull, Is.EqualTo(expected.ValueIsDefaultOrNull));
            Assert.That(actual.GetHashCode(), Is.EqualTo(expected.GetHashCode()));
            Assert.That(actual.ToString(), Is.EqualTo(expected.ToString()));
            bool forward = actual.Equals(expected);
            bool reverse = expected.Equals(actual);
            Assert.That(forward, Is.True);
            Assert.That(reverse, Is.True);
            Assert.That(actual.ValueEquals(expected), Is.True);
            Assert.That(expected.ValueEquals(actual), Is.True);
        }

        private static void AssertMemoryIdentity(ReadOnlyMemory<byte> actual, ReadOnlyMemory<byte> expected)
        {
            bool sameOwnerAndSlice = actual.Equals(expected);
            Assert.That(sameOwnerAndSlice, Is.True);
        }

        private static Variant Typed(object value)
        {
            return value switch
            {
                QualifiedName v => new Variant(v),
                NodeId v => new Variant(v),
                ByteString v => new Variant(v),
                LocalizedText v => new Variant(v),
                _ => throw new ArgumentException("Unexpected contract input.", nameof(value))
            };
        }

        private static Variant Boxed(object value)
        {
            TypeInfo type = value switch
            {
                QualifiedName => TypeInfo.Scalars.QualifiedName,
                NodeId => TypeInfo.Scalars.NodeId,
                ByteString => TypeInfo.Scalars.ByteString,
                LocalizedText => TypeInfo.Scalars.LocalizedText,
                _ => throw new ArgumentException("Unexpected contract input.", nameof(value))
            };
            return new Variant(default, type, value);
        }

        private static object Observe<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (Exception exception)
            {
                // Exception type is part of the legacy cross-type comparison contract.
                return exception.GetType();
            }
        }

        private sealed class TrackingMemoryManager : MemoryManager<byte>
        {
            public override Span<byte> GetSpan()
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(TrackingMemoryManager));
                }
                return m_buffer;
            }

            public override MemoryHandle Pin(int elementIndex = 0)
            {
                throw new NotSupportedException();
            }

            public override void Unpin()
            {
            }

            protected override void Dispose(bool disposing)
            {
                m_disposed = true;
            }

            private readonly byte[] m_buffer = [0, 1, 2, 3, 4];
            private bool m_disposed;
        }
    }
}
