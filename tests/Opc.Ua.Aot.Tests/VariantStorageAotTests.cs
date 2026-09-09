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

#nullable enable

using System.Buffers;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Direct native instantiations of scalar, builder, nesting and codec paths.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class VariantStorageAotTests(AotTestFixture fixture)
    {
        [Test]
        [Arguments(0)]
        [Arguments(65535)]
        public async Task QualifiedNamesPreserveRawNamesAndNamespacesAsync(int ns)
        {
            foreach (string? name in new string?[] { null, string.Empty, "native" })
            {
                var input = new QualifiedName(name, (ushort)ns);
                var value = Variant.From(input);
                await Assert.That(value.TryGetValue(out QualifiedName actual)).IsTrue();
                await Assert.That(actual.Name).IsEqualTo(name);
                await Assert.That(actual.NamespaceIndex).IsEqualTo((ushort)ns);
                await Assert.That(value.GetHashCode()).IsEqualTo(input.GetHashCode());
                await Assert.That(value.AsBoxedObject() is QualifiedName).IsTrue();
                await CheckAsync(input, value, default(VariantBuilder)).ConfigureAwait(false);
            }
        }

        [Test]
        [Arguments(0)]
        [Arguments(65535)]
        public async Task NodeIdsPreserveKindsCachedHashesAndBorrowedPayloadsAsync(int ns)
        {
            byte[] buffer = [9, 1, 2, 8];
            NodeId[] inputs =
            [
                new NodeId(0u, (ushort)ns), new NodeId(uint.MaxValue, (ushort)ns),
                new NodeId(null!, (ushort)ns), new NodeId(default(ByteString), (ushort)ns),
                new NodeId(string.Empty, (ushort)ns), new NodeId("native", (ushort)ns),
                new NodeId(Guid.Empty, (ushort)ns),
                new NodeId(new ByteString(buffer.AsMemory(1, 2)), (ushort)ns)
            ];
            foreach (NodeId input in inputs)
            {
                int hash = input.GetHashCode();
                var value = Variant.From(input);
                buffer[1] = 7;
                await Assert.That(value.TryGetValue(out NodeId actual)).IsTrue();
                await Assert.That(actual.IdType).IsEqualTo(input.IdType);
                await Assert.That(actual.NamespaceIndex).IsEqualTo(input.NamespaceIndex);
                await Assert.That(actual.GetHashCode()).IsEqualTo(hash);
                await Assert.That(actual.Equals(input)).IsTrue();
                await Assert.That(input.Equals(actual)).IsTrue();
                await Assert.That(value.AsBoxedObject() is NodeId).IsTrue();
                await CheckAsync(input, value, default(VariantBuilder)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ByteStringsPreserveSlicesNullsAndByteArrayConversionAsync()
        {
            byte[] buffer = [9, 1, 2, 8];
            foreach (ByteString input in new[]
            {
                default, ByteString.Empty, new ByteString(buffer.AsMemory(1, 2))
            })
            {
                var value = Variant.From(input);
                await Assert.That(value.TryGetValue(out ByteString actual)).IsTrue();
                await Assert.That(actual.IsNull).IsEqualTo(input.IsNull);
                await Assert.That(actual.Memory.Equals(input.Memory)).IsTrue();
                await Assert.That(value.GetByteArray().Span.SequenceEqual(input.Span)).IsTrue();
                buffer[1] = 42;
                await Assert.That(value.GetHashCode()).IsEqualTo(input.GetHashCode());
                await Assert.That(value.AsBoxedObject() is ByteString).IsTrue();
                await CheckAsync(input, value, default(VariantBuilder)).ConfigureAwait(false);
            }
            byte[] source = [1, 2];
            ArrayOf<byte> copiedArray = Variant.From(new ByteString(source)).GetByteArray();
            ByteString copiedBytes = Variant.From(new ArrayOf<byte>(source)).GetByteString();
            source[0] = 42;
            await Assert.That(copiedArray[0]).IsEqualTo((byte)1);
            await Assert.That(copiedBytes[0]).IsEqualTo((byte)1);
        }

        [Test]
        public async Task ByteStringMemoryManagerRetainsSliceFlagsAndDisposalAsync()
        {
            using var manager = new NativeMemoryManager();
            var input = new ByteString(manager.Memory.Slice(1, 3).Slice(1, 2));
            var value = Variant.From(input);
            await Assert.That(value.TryGetValue(out ByteString actual)).IsTrue();
            await Assert.That(actual.Memory.Equals(input.Memory)).IsTrue();
            await Assert.That(actual[0]).IsEqualTo((byte)2);
            manager.GetSpan()[2] = 42;
            await Assert.That(actual[0]).IsEqualTo((byte)42);
            await Assert.That(value.GetHashCode()).IsEqualTo(input.GetHashCode());
            ((IDisposable)manager).Dispose();
            await Assert.That(() => _ = actual.Span.Length).Throws<ObjectDisposedException>();
        }

        [Test]
        public async Task TextOnlyAndRichLocalizedTextKeepTheirRawStateAsync()
        {
            LocalizedText[] inputs =
            [
                default, new LocalizedText(string.Empty), new LocalizedText("native"),
                new LocalizedText(string.Empty, "native"), new LocalizedText("en", "native"),
                new LocalizedText("key", "en", "Value {0}", 7),
                new LocalizedText("key", null!, "Value {0}", 7),
                new LocalizedText(new Dictionary<string, string> { ["en"] = "native", ["de"] = "nativ" })
            ];
            foreach (LocalizedText input in inputs)
            {
                var value = Variant.From(input);
                await Assert.That(value.TryGetValue(out LocalizedText actual)).IsTrue();
                await Assert.That(actual.Locale).IsEqualTo(input.Locale);
                await Assert.That(actual.Text).IsEqualTo(input.Text);
                await Assert.That(ReferenceEquals(actual.Translations, input.Translations)).IsTrue();
                await Assert.That(actual.TranslationInfo.Equals(input.TranslationInfo)).IsTrue();
                await Assert.That(value.CompareTo(value)).IsEqualTo(int.MinValue);
                await Assert.That(value.GetHashCode()).IsEqualTo(input.GetHashCode());
                await CheckAsync(input, value, default(VariantBuilder)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PackedMetadataDoesNotParticipateInBitwiseOperatorsAsync()
        {
            byte[] buffer = [9, 1, 2, 8];
            Variant[] inputs =
            [
                Variant.From(new QualifiedName("native", ushort.MaxValue)),
                Variant.From(new NodeId(uint.MaxValue, ushort.MaxValue)),
                Variant.From(new ByteString(buffer.AsMemory(1, 2))),
                Variant.From(new LocalizedText("native"))
            ];
            foreach (Variant input in inputs)
            {
                Variant andResult = Variant.From(-1L) & input;
                Variant orResult = Variant.From(0L) | input;
                await Assert.That(andResult.TryGetValue(out long andValue)).IsTrue();
                await Assert.That(orResult.TryGetValue(out long orValue)).IsTrue();
                await Assert.That(andValue).IsEqualTo(0L);
                await Assert.That(orValue).IsEqualTo(0L);
                await Assert.That((Variant.From(true) & input).GetBoolean()).IsFalse();
                await Assert.That((Variant.From(false) | input).GetBoolean()).IsFalse();
            }
            await Assert.That((Variant.From(-1L) & Variant.From(7L)).GetInt64()).IsEqualTo(7L);
            await Assert.That((Variant.From(0L) | Variant.From(7L)).GetInt64()).IsEqualTo(7L);
        }

        private async Task CheckAsync<T>(T input, Variant value, IVariantBuilder<T> builder)
        {
            await Assert.That(EqualityComparer<T>.Default.Equals(builder.GetValue(value), input)).IsTrue();
            await Assert.That(builder.WithValue(input).Equals(value)).IsTrue();
            await Assert.That(VariantHelper.CastFrom(input).Equals(value)).IsTrue();
            await Assert.That(value.Copy().Equals(value)).IsTrue();
            var data = new DataValue(value);
            await Assert.That(Variant.From(data).GetDataValue().WrappedValue.Equals(value)).IsTrue();
            var context = ServiceMessageContext.CreateEmpty(fixture.Telemetry);
            using var encoder = new BinaryEncoder(context);
            encoder.WriteVariant(null, value);
            using var decoder = new BinaryDecoder(
                encoder.CloseAndReturnBuffer() ?? throw new InvalidOperationException("No encoded value."), context);
            Variant decoded = decoder.ReadVariant(null);
            await Assert.That(decoded.TypeInfo).IsEqualTo(value.TypeInfo);
            await Assert.That(decoded.Equals(value)).IsTrue();
        }

        private sealed class NativeMemoryManager : MemoryManager<byte>
        {
            public override Span<byte> GetSpan()
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
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
