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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.ComplexTypes;
using UaLens.StructuredValues;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class StructuredValueDraftTests
{
    [Test]
    public async Task GeneratedStructureRoundTripRetainsItsGeneratedTypeAndArrayFieldsAsync()
    {
        using var context = new StructuredValueTestContext();
        var original = new Argument
        {
            Name = "Before",
            DataType = DataTypeIds.Int32,
            ValueRank = ValueRanks.OneDimension,
            ArrayDimensions = [2u],
            Description = new LocalizedText("de", "Beschreibung")
        };
        StructuredValueDraft draft = await OpenArgumentAsync(context, original).ConfigureAwait(false);
        Assert.That(draft.Fields.Count, Is.EqualTo(5));
        ArrayOf<StructuredFieldEdit> edits = Edits(draft).ConvertAll(edit => edit.Name switch
        {
            nameof(Argument.Name) => edit with { Value = Variant.From("After") },
            nameof(Argument.ArrayDimensions) => edit with { Value = Variant.From((ArrayOf<uint>)[4u, 5u]) },
            _ => edit
        });

        Assert.That(draft.TryCommit(edits, out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue<Argument>(out Argument? result, context.MessageContext), Is.True);
        Assert.That(result!.Name, Is.EqualTo("After"));
        Assert.That(result.ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[4u, 5u]));
        Assert.That(result.Description.Locale, Is.EqualTo("de"));
        Assert.That(result.Description.Text, Is.EqualTo("Beschreibung"));
        Assert.That(original.Name, Is.EqualTo("Before"));
        Assert.That(original.ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[2u]));
        Assert.That(context.Reads, Is.Zero,
            "Registered generated accessors need no runtime reflection or server browse.");
    }

    [Test]
    public async Task FreshGeneratedStructureUsesTheRegisteredNativeActivatorAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.Argument,
            ArgumentActivator.Instance.GetDataTypeDefinition(context.MessageContext.NamespaceUris),
            Variant.Null).ConfigureAwait(false);
        ArrayOf<StructuredFieldEdit> edits = Edits(draft).ConvertAll(edit =>
            edit.Name == nameof(Argument.Name) ? edit with { Value = Variant.From("Created") } : edit);

        Assert.That(draft.TryCommit(edits, out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue<Argument>(out Argument? argument, context.MessageContext), Is.True);
        Assert.That(argument!.Name, Is.EqualTo("Created"));
    }

    [Test]
    public async Task DefaultAdapterRoundTripSetsAndClearsOptionalMasksIncludingTypedNullAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("Optional",
            StructureType.StructureWithOptionalFields,
            [
                StructuredValueTestContext.Field("Required", DataTypeIds.Int32),
                StructuredValueTestContext.Field("Number", DataTypeIds.Int32, optional: true),
                StructuredValueTestContext.Field("Bytes", DataTypeIds.ByteString, optional: true)
            ]);
        IEncodeable original = type.Type.CreateInstance();
        var source = (IStructure)original;
        source["Required"] = Variant.From(1);
        source["Number"] = Variant.From(7);
        source["Bytes"] = Variant.From(default(ByteString));
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(original)).ConfigureAwait(false);

        Assert.That(draft.Fields[1].IsIncluded, Is.True);
        Assert.That(draft.Fields[2].IsIncluded, Is.True, "A typed-null ByteString is still present.");
        Assert.That(draft.TryCommit(
            [
                new StructuredFieldEdit("Required", Variant.From(2)),
                new StructuredFieldEdit("Number", Variant.Null, IsIncluded: false),
                new StructuredFieldEdit("Bytes", Variant.From(default(ByteString)))
            ], out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue<Opc.Ua.Encoders.StructureWithOptionalFields>(
            out Opc.Ua.Encoders.StructureWithOptionalFields? result, context.MessageContext), Is.True);
        Assert.That(result!.EncodingMask, Is.EqualTo(2u));
        Assert.That(result["Number"].IsNull, Is.True);
        Assert.That(result["Bytes"].TryGetValue(out ByteString bytes), Is.True);
        Assert.That(bytes.IsNull, Is.True);
        Assert.That(((Opc.Ua.Encoders.StructureWithOptionalFields)original).EncodingMask, Is.EqualTo(3u));
        Assert.That(source["Required"].TryGetValue(out int unchanged), Is.True);
        Assert.That(unchanged, Is.EqualTo(1));
    }

    [TestCase(StructureType.Union)]
    [TestCase(StructureType.UnionWithSubtypedValues)]
    public async Task UnionSelectionAndEmptyUnionRoundTripWithoutMutatingEarlierResultsAsync(StructureType kind)
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("Choice", kind,
            [
                StructuredValueTestContext.Field("Number", DataTypeIds.Int32),
                StructuredValueTestContext.Field("Text", DataTypeIds.String),
                StructuredValueTestContext.Field("Last", DataTypeIds.Double)
            ]);
        IEncodeable original = type.Type.CreateInstance();
        ((IStructure)original)["Text"] = Variant.From("Original");
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(original)).ConfigureAwait(false);
        Assert.That(draft.Fields[0].IsIncluded, Is.False);
        Assert.That(draft.Fields[1].IsIncluded, Is.True);
        Assert.That(draft.Fields[2].IsIncluded, Is.False);
        ArrayOf<StructuredFieldEdit> edits =
        [
            new("Number", Variant.From(0)),
            new("Text", Variant.Null, IsIncluded: false),
            new("Last", Variant.Null, IsIncluded: false)
        ];

        Assert.That(draft.TryCommit(edits, out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue<Opc.Ua.Encoders.Union>(
            out Opc.Ua.Encoders.Union? selected, context.MessageContext), Is.True);
        Assert.That(selected!.SwitchField, Is.EqualTo(1u), "Omitted later arms must not reset the selected arm.");
        Assert.That(selected["Number"].TryGetValue(out int zero), Is.True);
        Assert.That(zero, Is.Zero, "A selected zero is not an empty union.");

        Assert.That(draft.TryCommit(edits.ConvertAll(edit => edit with { IsIncluded = false }),
            out Variant empty, out error), Is.True, error);
        Assert.That(empty.TryGetValue<Opc.Ua.Encoders.Union>(
            out Opc.Ua.Encoders.Union? cleared, context.MessageContext), Is.True);
        Assert.That(cleared!.SwitchField, Is.Zero);
        Assert.That(selected.SwitchField, Is.EqualTo(1u));
        Assert.That(((Opc.Ua.Encoders.Union)original).SwitchField, Is.EqualTo(2u));
    }

    [TestCase(StructureType.StructureWithOptionalFields)]
    [TestCase(StructureType.Union)]
    public async Task PresentNullVariantRetainsItsOptionalMaskOrUnionSelectorAsync(StructureType kind)
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("PresentNull", kind,
            [StructuredValueTestContext.Field("Value", DataTypeIds.BaseDataType,
                optional: kind == StructureType.StructureWithOptionalFields)]);
        IEncodeable source = type.Type.CreateInstance();
        using var stream = new MemoryStream();
        using (var encoder = new BinaryEncoder(stream, context.MessageContext, leaveOpen: true))
        {
            if (kind == StructureType.Union)
            {
                encoder.WriteSwitchField(1, out _);
            }
            else
            {
                encoder.WriteEncodingMask(1);
            }
            encoder.WriteVariant(null, Variant.Null);
        }
        stream.Position = 0;
        using (var decoder = new BinaryDecoder(stream, context.MessageContext, leaveOpen: true))
        {
            source.Decode(decoder);
        }
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(source)).ConfigureAwait(false);
        Assert.That(draft.Fields[0].Value.IsNull, Is.True);
        Assert.That(draft.Fields[0].IsIncluded, Is.True, "Null payload does not mean absent field.");

        Assert.That(draft.TryCommit(Edits(draft), out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue<Opc.Ua.Encoders.Structure>(
            out Opc.Ua.Encoders.Structure? result, context.MessageContext), Is.True);
        uint presence = result switch
        {
            Opc.Ua.Encoders.Union union => union.SwitchField,
            Opc.Ua.Encoders.StructureWithOptionalFields optional => optional.EncodingMask,
            _ => uint.MaxValue
        };
        Assert.That(presence, Is.EqualTo(1u));
        Assert.That(result!["Value"].IsNull, Is.True);
    }

    [TestCase(StructureType.StructureWithOptionalFields)]
    [TestCase(StructureType.Union)]
    public async Task CodecOnlyNativeTypesUseTheGeneratedContractForOptionalAndUnionValuesAsync(StructureType kind)
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("CodecOnly", kind,
            [StructuredValueTestContext.Field("Value", DataTypeIds.Int32,
                optional: kind == StructureType.StructureWithOptionalFields)]);
        IEncodeable body = type.Type.CreateInstance();
        ((IStructure)body)["Value"] = Variant.From(11);
        var source = new CodecOnlyValue(body);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(source)).ConfigureAwait(false);

        Assert.That(draft.TryCommit([new("Value", Variant.From(7))],
            out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue<CodecOnlyValue>(
            out CodecOnlyValue? result, context.MessageContext), Is.True);
        Assert.That(result!.Presence, Is.EqualTo(1u));
        Assert.That(result.ReadValue().TryGetValue(out int actual), Is.True);
        Assert.That(actual, Is.EqualTo(7));
        Assert.That(source.ReadValue().TryGetValue(out int unchanged), Is.True);
        Assert.That(unchanged, Is.EqualTo(11));
        Assert.That(draft.TryCommit([new("Value", Variant.Null, IsIncluded: false)],
            out Variant cleared, out error), Is.True, error);
        Assert.That(cleared.TryGetValue<CodecOnlyValue>(out CodecOnlyValue? empty, context.MessageContext), Is.True);
        Assert.That(empty!.Presence, Is.Zero);
        Assert.That(result.Presence, Is.EqualTo(1u));
        Assert.That(source.Presence, Is.EqualTo(1u));
    }

    [Test]
    public async Task NestedGeneratedAndArrayEditsRemainIsolatedUntilTheParentCommitsAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("Parent", StructureType.Structure,
            [
                StructuredValueTestContext.Field("Child", DataTypeIds.Argument),
                StructuredValueTestContext.Field("Children", DataTypeIds.Argument, rank: ValueRanks.OneDimension),
                StructuredValueTestContext.Field("Count", DataTypeIds.Int32)
            ]);
        var child = new Argument { Name = "Original", DataType = DataTypeIds.String };
        ArrayOf<Argument> children = [child];
        IEncodeable original = type.Type.CreateInstance();
        var source = (IStructure)original;
        source["Child"] = Variant.FromStructure(child);
        source["Children"] = Variant.FromStructure(children);
        source["Count"] = Variant.From(1);
        StructuredValueDraft parent = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(original)).ConfigureAwait(false);
        StructuredValueDraft nested = await context.Service.OpenAsync(
            DataTypeIds.Argument,
            ArgumentActivator.Instance.GetDataTypeDefinition(context.MessageContext.NamespaceUris),
            parent.Fields[0].Value).ConfigureAwait(false);
        Assert.That(nested.TryCommit(Edits(nested).ConvertAll(edit =>
            edit.Name == nameof(Argument.Name) ? edit with { Value = Variant.From("Edited") } : edit),
            out Variant nestedValue, out string? error), Is.True, error);
        StructuredArrayValue array = StructuredArrayValue.Read(parent.Fields[1].Value, context.MessageContext);
        Variant editedArray = array.WithElements([nestedValue], context.MessageContext);

        Assert.That(child.Name, Is.EqualTo("Original"), "Closing/canceling the parent at this point changes nothing.");
        ArrayOf<StructuredFieldEdit> edits =
        [
            new("Child", nestedValue),
            new("Children", editedArray),
            new("Count", Variant.From("invalid"))
        ];
        Assert.That(parent.TryCommit(edits, out Variant failed, out error), Is.False);
        Assert.That(failed.IsNull, Is.True);
        Assert.That(error, Does.Contain("Count"));
        Assert.That(child.Name, Is.EqualTo("Original"));
        Assert.That(children[0].Name, Is.EqualTo("Original"));

        Assert.That(parent.TryCommit(
            edits.ReplaceItem(new StructuredFieldEdit("Count", Variant.From(2)), 2),
            out Variant committed, out error), Is.True, error);
        IStructure result = ReadStructure(committed, context.MessageContext);
        Assert.That(result["Child"].TryGetValue<Argument>(out Argument? resultChild, context.MessageContext), Is.True);
        Assert.That(resultChild!.Name, Is.EqualTo("Edited"));
        Assert.That(result["Children"].TryGetValue(
            out ArrayOf<Argument> resultChildren, context.MessageContext), Is.True);
        Assert.That(resultChildren[0].Name, Is.EqualTo("Edited"));
        Assert.That(child.Name, Is.EqualTo("Original"));
    }

    [Test]
    public async Task NativeSetterFailureAfterAnEarlierAssignmentDoesNotChangeTheSourceAsync()
    {
        using var context = new StructuredValueTestContext();
        var source = new FailingStructure();
        source["First"] = Variant.From(1);
        source["Second"] = Variant.From(2);
        var definition = (StructureDefinition)source.GetDataTypeDefinition(context.MessageContext.NamespaceUris);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            new NodeId(9000u), definition, Variant.FromStructure(source)).ConfigureAwait(false);

        Assert.That(draft.TryCommit(
            [new("First", Variant.From(10)), new("Second", Variant.From(20))],
            out Variant failed, out string? error), Is.False);
        Assert.That(failed.IsNull, Is.True);
        Assert.That(error, Does.Contain("Rejected second field"));
        Assert.That(source["First"].TryGetValue(out int first), Is.True);
        Assert.That(first, Is.EqualTo(1));
        Assert.That(source["Second"].TryGetValue(out int second), Is.True);
        Assert.That(second, Is.EqualTo(2));
    }

    [Test]
    public async Task MultipleUnionArmsAndOmittedRequiredFieldsAreRejectedAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("Choice", StructureType.Union,
            [
                StructuredValueTestContext.Field("One", DataTypeIds.Int32),
                StructuredValueTestContext.Field("Two", DataTypeIds.Int32)
            ]);
        StructuredValueDraft union = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.Null).ConfigureAwait(false);
        Assert.That(union.TryCommit([new("One", Variant.From(1)), new("Two", Variant.From(2))],
            out _, out string? error), Is.False);
        Assert.That(error, Does.Contain("at most one"));

        StructuredValueDraft argument = await OpenArgumentAsync(context, new Argument()).ConfigureAwait(false);
        Assert.That(argument.TryCommit(Edits(argument).ConvertAll(edit => edit with { IsIncluded = false }),
            out _, out error), Is.False);
        Assert.That(error, Does.Contain("required"));
    }

    [Test]
    public async Task GeneratedEnumerationRetainsTypedValuesAndUnlistedOriginalValuesAsync()
    {
        using var context = new StructuredValueTestContext();
        DataTypeDefinition definition = NamingRuleTypeActivator.Instance.GetDataTypeDefinition(
            context.MessageContext.NamespaceUris);
        StructuredValueDraft draft = await context.Service.OpenAsync(
            DataTypeIds.NamingRuleType, definition, Variant.From(NamingRuleType.Mandatory)).ConfigureAwait(false);

        Assert.That(draft.TryCommitEnum((int)NamingRuleType.Optional, out Variant value, out string? error),
            Is.True, error);
        Assert.That(value.TryGetValue(out NamingRuleType result), Is.True);
        Assert.That(result, Is.EqualTo(NamingRuleType.Optional));
        Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        Assert.That(value.TryGetValue(out EnumValue enumValue), Is.True);
        Assert.That(enumValue.Symbol, Is.EqualTo(nameof(NamingRuleType.Optional)));
        Assert.That(draft.TryCommitEnum(99, out _, out error), Is.False);

        StructuredValueDraft unlisted = await context.Service.OpenAsync(
            DataTypeIds.NamingRuleType, definition, Variant.From(99)).ConfigureAwait(false);
        Assert.That(unlisted.TryCommitEnum(99, out Variant retained, out error), Is.True, error);
        Assert.That(retained.TryGetValue(out int number), Is.True);
        Assert.That(number, Is.EqualTo(99));
    }

    [Test]
    public async Task RefreshInvalidatesOpenDraftsInsteadOfCommittingStaleMetadataAsync()
    {
        using var context = new StructuredValueTestContext();
        var source = new Argument { Name = "Unchanged" };
        StructuredValueDraft draft = await OpenArgumentAsync(context, source).ConfigureAwait(false);
        context.Service.Refresh();

        Assert.That(draft.TryCommit(Edits(draft), out Variant value, out string? error), Is.False);
        Assert.That(value.IsNull, Is.True);
        Assert.That(error, Does.Contain("Reload"));
        Assert.That(source.Name, Is.EqualTo("Unchanged"));
    }

    [Test]
    public async Task OpaqueOrWrongTypeValuesCannotBecomeSuccessfulNoOpEditsAsync()
    {
        using var context = new StructuredValueTestContext();
        DataTypeDefinition definition = ArgumentActivator.Instance.GetDataTypeDefinition(
            context.MessageContext.NamespaceUris);
        Variant opaque = Variant.From(new ExtensionObject(
            new ExpandedNodeId(987654u), ByteString.From([1, 2, 3])));

        await Assert.ThatAsync(() => context.Service.OpenAsync(DataTypeIds.Argument, definition, opaque),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        await Assert.ThatAsync(() => context.Service.OpenAsync(
            DataTypeIds.Argument, definition, Variant.FromStructure(new ReadValueId())),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task DefaultComplexTypeSystemCancellationPropagatesAndCanBeRetriedAsync()
    {
        using var context = new StructuredValueTestContext();
        using var cancellation = new CancellationTokenSource();
        var resolver = new Mock<IComplexTypeResolver>(MockBehavior.Strict);
        resolver.SetupGet(value => value.NamespaceUris).Returns(context.MessageContext.NamespaceUris);
        resolver.SetupGet(value => value.FactoryBuilder).Returns(context.MessageContext.Factory.Builder);
        resolver.Setup(value => value.LoadDataTypesAsync(
            It.IsAny<ExpandedNodeId>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<ArrayOf<INode>>(cancellation.Token);
            });
        var factory = new Mock<IComplexTypeSystemFactory>(MockBehavior.Strict);
        factory.Setup(value => value.Create(context.Session.Object))
            .Returns(new ComplexTypeSystem(resolver.Object, context.MessageContext.Telemetry));
        using var service = new SessionStructuredValueService(context.Session.Object, factory.Object);
        var dataType = new NodeId(5000u, context.NamespaceIndex);

        await Assert.ThatAsync(() => service.OpenAsync(
            dataType, new StructureDefinition(), Variant.Null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        StructuredValueTestContext.NativeType type = context.Register(
            "Available", StructureType.Structure, [StructuredValueTestContext.Field("Value", DataTypeIds.Int32)]);
        StructuredValueDraft draft = await service.OpenAsync(type.DataTypeId, type.Definition, Variant.Null)
            .ConfigureAwait(false);
        Assert.That(draft.TryCommit([new("Value", Variant.From(4))], out Variant result, out string? error),
            Is.True, error);
        Assert.That(ReadStructure(result, context.MessageContext)["Value"].TryGetValue(out int actual), Is.True);
        Assert.That(actual, Is.EqualTo(4));
        factory.Verify(value => value.Create(context.Session.Object), Times.Once);
    }

    [Test]
    public async Task FirstOpaqueReadLoadsTheDefaultNativeTypeBeforeEditingAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register("LateType", StructureType.Structure,
            [StructuredValueTestContext.Field("Value", DataTypeIds.Int32)]);
        IEncodeable source = type.Type.CreateInstance();
        ((IStructure)source)["Value"] = Variant.From(17);
        ByteString bytes;
        using (var encoder = new BinaryEncoder(context.MessageContext))
        {
            encoder.WriteEncodeable(null, source, source.TypeId);
            bytes = ByteString.From(encoder.CloseAndReturnBuffer());
        }
        Variant opaque = Variant.From(new ExtensionObject(source.BinaryEncodingId, bytes));
        var messageContext = new ServiceMessageContext(context.MessageContext.Telemetry, EncodeableFactory.Create());
        messageContext.NamespaceUris.Update(context.MessageContext.NamespaceUris.ToArray());
        context.MessageContext = messageContext;
        IEncodeableFactoryBuilder builder = context.MessageContext.Factory.Builder;
        var resolver = new Mock<IComplexTypeResolver>(MockBehavior.Strict);
        resolver.SetupGet(value => value.NamespaceUris).Returns(context.MessageContext.NamespaceUris);
        resolver.SetupGet(value => value.FactoryBuilder).Returns(builder);
        resolver.Setup(value => value.LoadDataTypesAsync(
            It.IsAny<ExpandedNodeId>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                builder.AddEncodeableType(type.Type);
                return Task.FromResult<ArrayOf<INode>>([]);
            });
        var factory = new Mock<IComplexTypeSystemFactory>(MockBehavior.Strict);
        factory.Setup(value => value.Create(context.Session.Object))
            .Returns(new ComplexTypeSystem(resolver.Object, context.MessageContext.Telemetry));
        using var service = new SessionStructuredValueService(context.Session.Object, factory.Object);

        StructuredValueDraft draft = await service.OpenAsync(type.DataTypeId, type.Definition, opaque)
            .ConfigureAwait(false);

        Assert.That(draft.Fields[0].Value.TryGetValue(out int initial), Is.True);
        Assert.That(initial, Is.EqualTo(17));
        Assert.That(draft.TryCommit([new("Value", Variant.From(23))],
            out Variant committed, out string? error), Is.True, error);
        Assert.That(ReadStructure(committed, context.MessageContext)["Value"].TryGetValue(out int edited), Is.True);
        Assert.That(edited, Is.EqualTo(23));
        Assert.That(opaque.TryGetValue(out ExtensionObject original), Is.True);
        Assert.That(original.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
        factory.Verify(value => value.Create(context.Session.Object), Times.Once);
    }

    [Test]
    public async Task NestedOptionSetAndMatrixEditsShareTheParentTransactionAndNativeEncodingAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeOptionSet flagsType = context.RegisterOptionSet(
            "Flags", [new EnumField { Name = "Enabled", Value = 0 }]);
        var flags = (OptionSet)flagsType.Type.CreateInstance();
        flags.Value = ByteString.From([0x80]);
        flags.ValidBits = ByteString.From([0xFF]);
        StructureField cells = StructuredValueTestContext.Field("Cells", DataTypeIds.Int32, rank: 2);
        cells.ArrayDimensions = [2u, 3u];
        StructuredValueTestContext.NativeType type = context.Register(
            "EditorParent", StructureType.Structure,
            [StructuredValueTestContext.Field("Flags", flagsType.DataTypeId), cells]);
        IEncodeable original = type.Type.CreateInstance();
        var source = (IStructure)original;
        source["Flags"] = Variant.FromStructure(flags);
        source["Cells"] = Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3]));
        StructuredValueDraft parent = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.FromStructure(original)).ConfigureAwait(false);
        StructuredValueDraft bitDraft = await context.Service.OpenAsync(
            flagsType.DataTypeId, flagsType.Definition, parent.Fields[0].Value).ConfigureAwait(false);
        Assert.That(bitDraft.TryCommitOptionSet([new(0, true, false)],
            out Variant editedFlags, out string? error), Is.True, error);
        StructuredArrayDraft matrixDraft = await context.Service.OpenArrayAsync(
            cells.DataType, cells.ValueRank, cells.ArrayDimensions, parent.Fields[1].Value,
            isStructureField: true).ConfigureAwait(false);
        StructuredArrayValue reshaped = matrixDraft.Reshape(matrixDraft.InitialValue, [2, 2], allowResize: true);
        Assert.That(matrixDraft.TryCommit(reshaped, out Variant editedCells, out error), Is.True, error);

        Variant invalidShape = Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([3, 2]));
        Assert.That(parent.TryCommit([new("Flags", editedFlags), new("Cells", invalidShape)],
            out Variant failed, out error), Is.False);
        Assert.That(failed.IsNull, Is.True);
        Assert.That(error, Does.Contain("Cells").And.Contain("declared maximum"));
        Assert.That(flags.Value, Is.EqualTo(ByteString.From([0x80])));
        Assert.That(flags.ValidBits, Is.EqualTo(ByteString.From([0xFF])));

        Assert.That(parent.TryCommit([new("Flags", editedFlags), new("Cells", editedCells)],
            out Variant committed, out error), Is.True, error);
        IStructure result = ReadStructure(context.RoundTrip(committed), context.MessageContext);
        Assert.That(result["Flags"].TryGetValue<OptionSet>(out OptionSet? encodedFlags, context.MessageContext),
            Is.True);
        Assert.That(encodedFlags!.Value, Is.EqualTo(ByteString.From([0x81])));
        Assert.That(encodedFlags.ValidBits, Is.EqualTo(ByteString.From([0xFE])));
        Assert.That(result["Cells"].TryGetValue(out MatrixOf<int> encodedCells), Is.True);
        Assert.That(encodedCells.Dimensions, Is.EqualTo(s_reshapedDimensions));
        Assert.That(encodedCells.ToArrayOf(), Is.EqualTo((ArrayOf<int>)[1, 2, 3, 4]));
        Assert.That(source["Cells"].TryGetValue(out MatrixOf<int> untouched), Is.True);
        Assert.That(untouched.Dimensions, Is.EqualTo(s_originalDimensions));
        Assert.That(untouched.ToArrayOf(), Is.EqualTo((ArrayOf<int>)[1, 2, 3, 4, 5, 6]));
        Assert.That(flags.Value, Is.EqualTo(ByteString.From([0x80])));
    }

    [Test]
    public async Task FreshDefaultStructureMatrixFieldCanBeCreatedWithoutImportingAValueAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredValueTestContext.NativeType type = context.Register(
            "FreshMatrix", StructureType.Structure,
            [StructuredValueTestContext.Field("Cells", DataTypeIds.Int32, rank: 2)]);
        StructuredValueDraft parent = await context.Service.OpenAsync(
            type.DataTypeId, type.Definition, Variant.Null).ConfigureAwait(false);
        StructuredArrayDraft matrix = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], parent.Fields[0].Value, isStructureField: true).ConfigureAwait(false);
        Assert.That(matrix.InitialValue.IsNull, Is.True);
        StructuredArrayValue created = matrix.Reshape(matrix.InitialValue, [1, 2], allowResize: true);
        created = matrix.WithElements(created, [Variant.From(7), Variant.From(8)]);
        Assert.That(matrix.TryCommit(created, out Variant cells, out string? error), Is.True, error);
        Assert.That(parent.TryCommit([new("Cells", cells)], out Variant committed, out error), Is.True, error);

        IStructure result = ReadStructure(context.RoundTrip(committed), context.MessageContext);
        Assert.That(result["Cells"].TryGetValue(out MatrixOf<int> encoded), Is.True);
        Assert.That(encoded.Dimensions, Is.EqualTo(s_freshDimensions));
        Assert.That(encoded.ToArrayOf(), Is.EqualTo((ArrayOf<int>)[7, 8]));
    }

    private static Task<StructuredValueDraft> OpenArgumentAsync(StructuredValueTestContext context, Argument value)
    {
        return context.Service.OpenAsync(
            DataTypeIds.Argument,
            ArgumentActivator.Instance.GetDataTypeDefinition(context.MessageContext.NamespaceUris),
            Variant.FromStructure(value));
    }

    private static ArrayOf<StructuredFieldEdit> Edits(StructuredValueDraft draft)
    {
        return draft.Fields.ConvertAll(field =>
            new StructuredFieldEdit(field.Definition.Name!, field.Value, field.IsIncluded));
    }

    private static IStructure ReadStructure(Variant value, IServiceMessageContext context)
    {
        Assert.That(value.TryGetValue(out ExtensionObject extension), Is.True);
        Assert.That(extension.TryGetValue(out IEncodeable? body, context), Is.True);
        Assert.That(body, Is.InstanceOf<IStructure>());
        return (IStructure)body!;
    }

    private static readonly int[] s_reshapedDimensions = [2, 2];
    private static readonly int[] s_originalDimensions = [2, 3];
    private static readonly int[] s_freshDimensions = [1, 2];

    private sealed class CodecOnlyValue : IEncodeable
    {
        public CodecOnlyValue(IEncodeable body)
        {
            m_body = body;
        }

        public ExpandedNodeId TypeId => m_body.TypeId;
        public ExpandedNodeId BinaryEncodingId => m_body.BinaryEncodingId;
        public ExpandedNodeId XmlEncodingId => m_body.XmlEncodingId;

        public uint Presence => m_body switch
        {
            Opc.Ua.Encoders.Union union => union.SwitchField,
            Opc.Ua.Encoders.StructureWithOptionalFields optional => optional.EncodingMask,
            _ => throw new InvalidOperationException("This fixture requires presence metadata.")
        };

        public Variant ReadValue()
        {
            return ((IStructure)m_body)["Value"];
        }

        public void Encode(IEncoder encoder)
        {
            m_body.Encode(encoder);
        }

        public void Decode(IDecoder decoder)
        {
            m_body.Decode(decoder);
        }

        public bool IsEqual(IEncodeable? encodeable)
        {
            return encodeable is CodecOnlyValue other && m_body.IsEqual(other.m_body);
        }

        public object Clone()
        {
            return new CodecOnlyValue(CoreUtils.Clone(m_body)!);
        }

        private readonly IEncodeable m_body;
    }

    private sealed class FailingStructure : Opc.Ua.Encoders.Structure
    {
        public FailingStructure()
            : base(new XmlQualifiedName("Failing", Namespaces.OpcUa),
                new ExpandedNodeId(9000u), new ExpandedNodeId(9001u), new ExpandedNodeId(9002u),
                new StructureDefinition
                {
                    Fields =
                    [
                        StructuredValueTestContext.Field("First", DataTypeIds.Int32),
                        StructuredValueTestContext.Field("Second", DataTypeIds.Int32)
                    ]
                },
                new Dictionary<string, BuiltInType>
                {
                    ["First"] = BuiltInType.Int32,
                    ["Second"] = BuiltInType.Int32
                })
        {
        }

        private FailingStructure(FailingStructure source)
            : base(source)
        {
            m_rejectSecond = true;
        }

        public override Variant this[string name]
        {
            get => base[name];
            set
            {
                if (m_rejectSecond && name == "Second")
                {
                    throw new InvalidOperationException("Rejected second field.");
                }
                base[name] = value;
            }
        }

        public override object Clone()
        {
            return new FailingStructure(this);
        }

        private readonly bool m_rejectSecond;
    }
}
