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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.ComplexTypes;
using UaLens.StructuredValues;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class StructuredOptionSetValueTests
{
    [TestCase(BuiltInType.Byte, 1)]
    [TestCase(BuiltInType.UInt16, 2)]
    [TestCase(BuiltInType.UInt32, 4)]
    [TestCase(BuiltInType.UInt64, 8)]
    public async Task UnsignedEditsPreserveWireWidthAndUnknownHighBitsAsync(BuiltInType type, int width)
    {
        using var context = new StructuredValueTestContext();
        ulong highBit = 1UL << ((width * 8) - 1);
        Variant initial = Unsigned(type, highBit | 1);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            new NodeId((uint)type), Definition([0, 1]), initial).ConfigureAwait(false);

        Assert.That(draft.OptionSet!.StorageType, Is.EqualTo(type));
        Assert.That(draft.OptionSet.ByteLength, Is.EqualTo(width));
        Assert.That(draft.OptionSet.HasValidity, Is.False);
        Assert.That(draft.TryCommitEnum(1, out Variant notEnum, out string? enumError), Is.False);
        Assert.That(notEnum.IsNull, Is.True);
        Assert.That(enumError, Does.Contain("not an enumeration"));
        Assert.That(draft.TryCommitOptionSet([new(0, false), new(1, true)],
            out Variant committed, out string? error), Is.True, error);

        AssertUnsignedWire(context, committed, type, width, highBit | 2);
        AssertUnsignedWire(context, initial, type, width, highBit | 1);
        Assert.That(context.Reads, Is.Zero);
    }

    [TestCase(BuiltInType.Byte, 1)]
    [TestCase(BuiltInType.UInt16, 2)]
    [TestCase(BuiltInType.UInt32, 4)]
    [TestCase(BuiltInType.UInt64, 8)]
    public async Task FreshUnsignedOptionSetsSetTheHighestBitWithoutInt32ConversionAsync(BuiltInType type, int width)
    {
        using var context = new StructuredValueTestContext();
        int index = (width * 8) - 1;
        StructuredValueDraft draft = await context.Service.OpenAsync(
            new NodeId((uint)type), Definition([index]), Variant.Null).ConfigureAwait(false);

        Assert.That(draft.TryCommitOptionSet([new(index, true)],
            out Variant committed, out string? error), Is.True, error);
        AssertUnsignedWire(context, committed, type, width, 1UL << index);
    }

    [Test]
    public async Task UnsignedOptionSetsRejectSignedEnumerationArrayAndWrongWidthValuesAsync()
    {
        using var context = new StructuredValueTestContext();
        ArrayOf<Variant> invalid =
        [
            Variant.From(-1),
            Variant.From(NamingRuleType.Mandatory),
            Variant.From((ArrayOf<uint>)[1u]),
            Variant.From(1UL),
            Variant.From("1")
        ];
        for (int i = 0; i < invalid.Count; i++)
        {
            Variant value = invalid[i];
            await Assert.ThatAsync(() => context.Service.OpenAsync(
                DataTypeIds.UInt32, Definition([0]), value),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch))
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task UnresolvedCustomOptionSetCannotInferItsWidthFromTheCurrentValueAsync()
    {
        using var context = new StructuredValueTestContext();
        var factory = new Mock<IComplexTypeSystemFactory>(MockBehavior.Strict);
        factory.Setup(value => value.Create(context.Session.Object))
            .Throws(new ServiceResultException(StatusCodes.BadDataTypeIdUnknown));
        using var service = new SessionStructuredValueService(context.Session.Object, factory.Object);

        await Assert.ThatAsync(() => service.OpenAsync(
            new NodeId(5000u, context.NamespaceIndex), Definition([0]), Variant.From(1u)),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadDataTypeIdUnknown)).ConfigureAwait(false);
        factory.Verify(value => value.Create(context.Session.Object), Times.Once);
    }

    [TestCase(-1L)]
    [TestCase(32L)]
    [TestCase(long.MaxValue)]
    public async Task InvalidOrOutOfWidthBitIndexesAreRejectedBeforeShiftingAsync(long index)
    {
        using var context = new StructuredValueTestContext();
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.UInt32, Definition([index]), Variant.From(0u)),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadOutOfRange))
            .ConfigureAwait(false);
    }

    [Test]
    public async Task DuplicateDefinitionsAndUnknownDuplicateOrValidityEditsFailExplicitlyAsync()
    {
        using var context = new StructuredValueTestContext();
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.UInt32, Definition([0, 0]), Variant.From(0u)),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.UInt32, Definition([0]), Variant.From(0x80000000u)).ConfigureAwait(false);
        ArrayOf<ArrayOf<StructuredOptionBitEdit>> invalid =
        [
            [new(1, true)],
            [new(0, true), new(0, false)],
            [new(0, true, IsValid: false)]
        ];
        foreach (ArrayOf<StructuredOptionBitEdit> edits in invalid)
        {
            Assert.That(draft.TryCommitOptionSet(edits, out Variant failed, out string? error), Is.False);
            Assert.That(failed.IsNull, Is.True);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }
        Assert.That(draft.TryCommitOptionSet([], out Variant retained, out string? retainedError),
            Is.True, retainedError);
        AssertUnsignedWire(context, retained, BuiltInType.UInt32, 4, 0x80000000);
    }

    [Test]
    public async Task AnEmptyDefinitionRetainsEveryUnsignedBitAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.UInt64, Definition([]), Variant.From(ulong.MaxValue)).ConfigureAwait(false);

        Assert.That(draft.OptionSet!.Bits.IsEmpty, Is.True);
        Assert.That(draft.TryCommitOptionSet([], out Variant retained, out string? error), Is.True, error);
        AssertUnsignedWire(context, retained, BuiltInType.UInt64, 8, ulong.MaxValue);
    }

    [Test]
    public async Task GeneratedOptionSetKeepsValueAndValidityIndependentAndItsNativeCodecAsync()
    {
        using var context = new StructuredValueTestContext();
        context.RegisterOptionSetBaseCodec();
        var source = new OptionSet
        {
            Value = ByteString.From([0x81, 0x80]),
            ValidBits = ByteString.From([0x01, 0x80])
        };
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.OptionSet, Definition([0, 8]), Variant.FromStructure(source)).ConfigureAwait(false);

        Assert.That(draft.OptionSet!.HasValidity, Is.True);
        Assert.That(draft.OptionSet.Bits[0].IsSet, Is.True);
        Assert.That(draft.OptionSet.Bits[0].IsValid, Is.True);
        Assert.That(draft.OptionSet.Bits[1].IsValid, Is.False);
        Assert.That(draft.TryCommitOptionSet([new(0, false, true), new(8, true, false)],
            out Variant committed, out string? error), Is.True, error);

        OptionSet result = ReadOptionSet(context.RoundTrip(committed), context);
        Assert.That(result.GetType(), Is.EqualTo(typeof(OptionSet)));
        Assert.That(result.TypeId, Is.EqualTo(source.TypeId));
        Assert.That(result.BinaryEncodingId, Is.EqualTo(source.BinaryEncodingId));
        Assert.That(result.Value, Is.EqualTo(ByteString.From([0x80, 0x81])));
        Assert.That(result.ValidBits, Is.EqualTo(ByteString.From([0x01, 0x80])));
        Assert.That(source.Value, Is.EqualTo(ByteString.From([0x81, 0x80])));
        Assert.That(source.ValidBits, Is.EqualTo(ByteString.From([0x01, 0x80])));
    }

    [Test]
    public async Task DefaultOptionSetAdapterPreservesUnknownBytesAndConcreteEncodingIdsAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeOptionSet type = context.RegisterOptionSet("Flags", Definition([0, 2]).Fields);
        var source = (OptionSet)type.Type.CreateInstance();
        source.Value = ByteString.From([0x80, 0xAB, 0xCD]);
        source.ValidBits = ByteString.From([0xFF, 0x12, 0x80]);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(source)).ConfigureAwait(false);

        Assert.That(draft.OptionSet!.ByteLength, Is.EqualTo(3),
            "The partial definition must not shorten existing data.");
        Assert.That(draft.TryCommitOptionSet([new(0, true, false)],
            out Variant committed, out string? error), Is.True, error);
        OptionSet result = ReadOptionSet(context.RoundTrip(committed), context);
        Assert.That(result, Is.TypeOf<Opc.Ua.Encoders.OptionSet>());
        Assert.That(result.TypeId, Is.EqualTo(source.TypeId));
        Assert.That(result.BinaryEncodingId, Is.EqualTo(source.BinaryEncodingId));
        Assert.That(result.Value, Is.EqualTo(ByteString.From([0x81, 0xAB, 0xCD])));
        Assert.That(result.ValidBits, Is.EqualTo(ByteString.From([0xFE, 0x12, 0x80])));
        Assert.That(source.Value, Is.EqualTo(ByteString.From([0x80, 0xAB, 0xCD])));
        Assert.That(source.ValidBits, Is.EqualTo(ByteString.From([0xFF, 0x12, 0x80])));
        Assert.That(draft.TryCommitOptionSet([new(2, true)], out _, out error), Is.True, error);
        Assert.That(result.Value, Is.EqualTo(ByteString.From([0x81, 0xAB, 0xCD])));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OmittedValidityMaterializesAllValidUnknownBitsBeforeClearingOneAsync(bool empty)
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeOptionSet type = context.RegisterOptionSet("Flags", Definition([0, 9]).Fields);
        var source = (OptionSet)type.Type.CreateInstance();
        source.Value = ByteString.From([0x81, 0x80]);
        source.ValidBits = empty ? ByteString.Empty : default;
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(source)).ConfigureAwait(false);

        Assert.That(draft.OptionSet!.Bits[0].IsValid, Is.True);
        Assert.That(draft.OptionSet.Bits[1].IsValid, Is.True);
        Assert.That(draft.TryCommitOptionSet([new(0, true, false)],
            out Variant committed, out string? error), Is.True, error);
        OptionSet result = ReadOptionSet(context.RoundTrip(committed), context);
        Assert.That(result.Value, Is.EqualTo(ByteString.From([0x81, 0x80])));
        Assert.That(result.ValidBits, Is.EqualTo(ByteString.From([0xFE, 0xFF])));
        Assert.That(source.ValidBits.IsEmpty, Is.True);
        Assert.That(source.ValidBits.IsNull, Is.EqualTo(!empty));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnchangedOptionSetPreservesNullVersusEmptyByteStringsAsync(bool empty)
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeOptionSet type = context.RegisterOptionSet("Flags", Definition([0, 8]).Fields);
        var source = (OptionSet)type.Type.CreateInstance();
        source.Value = empty ? ByteString.Empty : default;
        source.ValidBits = empty ? ByteString.Empty : default;
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(source)).ConfigureAwait(false);

        Assert.That(draft.TryCommitOptionSet([new(0, false, true), new(8, false, true)],
            out Variant committed, out string? error), Is.True, error);
        OptionSet result = ReadOptionSet(context.RoundTrip(committed), context);
        Assert.That(result.Value.IsEmpty, Is.True);
        Assert.That(result.Value.IsNull, Is.EqualTo(!empty));
        Assert.That(result.ValidBits.IsEmpty, Is.True);
        Assert.That(result.ValidBits.IsNull, Is.EqualTo(!empty));
    }

    [Test]
    public async Task FreshDefaultOptionSetUsesNativeActivationAndAllowsIndependentValidityAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeOptionSet type = context.RegisterOptionSet(
            "Fresh", Definition([0, 15]).Fields);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.Null).ConfigureAwait(false);

        Assert.That(draft.TryCommitOptionSet([new(15, true, false)],
            out Variant committed, out string? error), Is.True, error);
        OptionSet result = ReadOptionSet(context.RoundTrip(committed), context);
        Assert.That(result, Is.TypeOf<Opc.Ua.Encoders.OptionSet>());
        Assert.That(result.Value, Is.EqualTo(ByteString.From([0x00, 0x80])));
        Assert.That(result.ValidBits, Is.EqualTo(ByteString.From([0xFF, 0x7F])));
    }

    [Test]
    public async Task StructuredOptionSetRejectsMismatchedMasksWrongBodiesAndOutOfWidthMetadataAsync()
    {
        using var context = new StructuredValueTestContext();
        var source = new OptionSet
        {
            Value = ByteString.From([1]),
            ValidBits = ByteString.From([1, 0])
        };
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.OptionSet, Definition([0]), Variant.FromStructure(source)),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch))
            .ConfigureAwait(false);

        source.ValidBits = ByteString.Empty;
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.OptionSet, Definition([8]), Variant.FromStructure(source)),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadOutOfRange))
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.Argument, Definition([0]), Variant.FromStructure(source)),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.Argument, Definition([0]), Variant.FromStructure(new Argument())),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task OptionSetByteCapacityAcceptsTheLimitAndRejectsTheNextByteAsync()
    {
        using var context = new StructuredValueTestContext();
        context.MessageContext.MaxByteStringLength = 2;
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.OptionSet, Definition([15]),
            Variant.FromStructure(new OptionSet { Value = ByteString.From([0, 0]) })).ConfigureAwait(false);
        Assert.That(draft.OptionSet!.ByteLength, Is.EqualTo(2));
        Assert.That(draft.TryCommitOptionSet([new(15, true)], out Variant committed, out string? error),
            Is.True, error);
        Assert.That(ReadOptionSet(committed, context).Value, Is.EqualTo(ByteString.From([0, 0x80])));

        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.OptionSet, Definition([16]), Variant.FromStructure(new OptionSet())),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded))
            .ConfigureAwait(false);
    }

    [Test]
    public async Task OpaqueOptionSetIsDecodedByItsRegisteredAdapterBeforeEditingAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeOptionSet type = context.RegisterOptionSet("Opaque", Definition([0]).Fields);
        var source = (OptionSet)type.Type.CreateInstance();
        source.Value = ByteString.From([0x80]);
        source.ValidBits = ByteString.From([0xFF]);
        ByteString bytes;
        using (var encoder = new BinaryEncoder(context.MessageContext))
        {
            encoder.WriteEncodeable(null, source, source.TypeId);
            bytes = ByteString.From(encoder.CloseAndReturnBuffer());
        }
        Variant opaque = Variant.From(new ExtensionObject(source.BinaryEncodingId, bytes));
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, opaque).ConfigureAwait(false);
        Assert.That(draft.TryCommitOptionSet([new(0, true)],
            out Variant committed, out string? error), Is.True, error);
        Assert.That(ReadOptionSet(context.RoundTrip(committed), context).Value, Is.EqualTo(ByteString.From([0x81])));
        Assert.That(opaque.TryGetValue(out ExtensionObject untouched), Is.True);
        Assert.That(untouched.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
    }

    [Test]
    public async Task CancellationBeforeOpeningAndAfterOpeningCannotPublishOptionSetEditsAsync()
    {
        using var context = new StructuredValueTestContext();
        using var cancellation = new CancellationTokenSource();
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.UInt64, Definition([0]), Variant.From(1UL << 63), cancellation.Token).ConfigureAwait(false);
        cancellation.Cancel();

        Assert.That(draft.TryCommitOptionSet([new(0, true)], out Variant committed, out string? error), Is.False);
        Assert.That(committed.IsNull, Is.True);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.UInt64, Definition([0]), Variant.Null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        AssertUnsignedWire(context, draft.InitialValue, BuiltInType.UInt64, 8, 1UL << 63);
    }

    [TestCase("Refresh")]
    [TestCase("Session")]
    [TestCase("Namespaces")]
    [TestCase("Endpoint")]
    [TestCase("Server")]
    [TestCase("Disconnect")]
    [TestCase("Context")]
    public async Task StaleOptionSetDraftsCannotCommitAfterSessionIdentityChangesAsync(string change)
    {
        using var context = new StructuredValueTestContext();
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.UInt32, Definition([0]), Variant.From(0x80000000u)).ConfigureAwait(false);
        context.Invalidate(change);

        Assert.That(draft.TryCommitOptionSet([new(0, true)], out Variant committed, out string? error), Is.False);
        Assert.That(committed.IsNull, Is.True);
        Assert.That(error, Does.Contain("Reload"));
        Assert.That(draft.InitialValue.TryGetValue(out uint initial), Is.True);
        Assert.That(initial, Is.EqualTo(0x80000000u));
    }

    private static EnumDefinition Definition(ArrayOf<long> indexes)
    {
        return new EnumDefinition
        {
            IsOptionSet = true,
            Fields = indexes.ConvertAll(index => new EnumField { Name = $"Bit{index}", Value = index })
        };
    }

    private static Variant Unsigned(BuiltInType type, ulong value)
    {
        return type switch
        {
            BuiltInType.Byte => Variant.From(checked((byte)value)),
            BuiltInType.UInt16 => Variant.From(checked((ushort)value)),
            BuiltInType.UInt32 => Variant.From(checked((uint)value)),
            BuiltInType.UInt64 => Variant.From(value),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static void AssertUnsignedWire(
        StructuredValueTestContext context, Variant value, BuiltInType type, int width, ulong expected)
    {
        var bytes = new byte[width + 1];
        bytes[0] = (byte)type;
        for (int i = 0; i < width; i++)
        {
            bytes[i + 1] = (byte)((expected >> (i * 8)) & 0xFF);
        }
        Assert.That(context.EncodeVariant(value), Is.EqualTo(ByteString.From(bytes)));
        Variant decoded = context.RoundTrip(value);
        Assert.That(decoded.TypeInfo.BuiltInType, Is.EqualTo(type));
        Assert.That(decoded.TypeInfo.IsScalar, Is.True);
        Assert.That(decoded, Is.EqualTo(Unsigned(type, expected)));
    }

    private static OptionSet ReadOptionSet(Variant value, StructuredValueTestContext context)
    {
        Assert.That(value.TryGetValue<OptionSet>(out OptionSet? result, context.MessageContext), Is.True);
        return result!;
    }
}
