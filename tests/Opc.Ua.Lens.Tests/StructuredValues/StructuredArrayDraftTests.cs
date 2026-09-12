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
using NUnit.Framework;
using Opc.Ua;
using UaLens.StructuredValues;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class StructuredArrayDraftTests
{
    [Test]
    public async Task FreshMatrixUsesTypedDefaultsAndRoundTripsActualRowMajorValuesAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [2u, 3u], Variant.Null).ConfigureAwait(false);
        Assert.That(draft.InitialValue.IsNull, Is.True);
        StructuredArrayValue created = draft.Reshape(draft.InitialValue, [2, 3], allowResize: true);
        Assert.That(created.IsNull, Is.False);
        Assert.That(created.Elements.Count, Is.EqualTo(6));
        foreach (Variant value in created.Elements)
        {
            Assert.That(value.TryGetValue(out int initial), Is.True);
            Assert.That(initial, Is.Zero);
        }

        ArrayOf<int> values = [11, 22, 33, 44, 55, 66];
        StructuredArrayValue edited = draft.WithElements(created, values.ConvertAll(value => Variant.From(value)));
        Assert.That(draft.TryCommit(edited, out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        AssertMatrix(context.RoundTrip(committed), [2, 3], values);
        Assert.That(draft.InitialValue.IsNull, Is.True, "Opening and creating a candidate do not change the original.");
        Assert.That(context.Reads, Is.Zero);
    }

    [TestCase(ValueRanks.Any)]
    [TestCase(ValueRanks.OneOrMoreDimensions)]
    public async Task ReshapeChangesRankWithoutChangingFlattenedOrderAsync(int rank)
    {
        using var context = new StructuredValueTestContext();
        Variant original = Matrix([1, 2, 3, 4, 5, 6], [2, 3]);
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, rank, [], original).ConfigureAwait(false);
        StructuredArrayValue reshaped = draft.Reshape(draft.InitialValue, [3, 1, 2]);

        Assert.That(draft.TryCommit(reshaped, out Variant committed, out string? error), Is.True, error);
        AssertMatrix(context.RoundTrip(committed), [3, 1, 2], [1, 2, 3, 4, 5, 6]);
        AssertMatrix(original, [2, 3], [1, 2, 3, 4, 5, 6]);
    }

    [Test]
    public async Task ReshapeRejectsImplicitShrinkAndGrowthButExplicitResizeRetainsThePrefixAsync()
    {
        using var context = new StructuredValueTestContext();
        Variant original = Matrix([1, 2, 3, 4], [2, 2]);
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], original).ConfigureAwait(false);
        Assert.That(() => draft.Reshape(draft.InitialValue, [1, 3]),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
        Assert.That(() => draft.Reshape(draft.InitialValue, [1, 5]),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));

        StructuredArrayValue grown = draft.Reshape(draft.InitialValue, [2, 3], allowResize: true);
        Assert.That(draft.TryCommit(grown, out Variant expanded, out string? error), Is.True, error);
        AssertMatrix(context.RoundTrip(expanded), [2, 3], [1, 2, 3, 4, 0, 0]);
        StructuredArrayValue shrunk = draft.Reshape(draft.InitialValue, [1, 3], allowResize: true);
        Assert.That(draft.TryCommit(shrunk, out Variant reduced, out error), Is.True, error);
        AssertMatrix(context.RoundTrip(reduced), [1, 3], [1, 2, 3]);
        AssertMatrix(expanded, [2, 3], [1, 2, 3, 4, 0, 0]);
        AssertMatrix(original, [2, 2], [1, 2, 3, 4]);
    }

    [Test]
    public async Task FixedRankAndDeclaredDimensionCountAreValidatedBeforeCreatingElementsAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], Variant.Null).ConfigureAwait(false);
        Assert.That(() => draft.Reshape(draft.InitialValue, [1, 1, 1], allowResize: true),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch));
        await Assert.ThatAsync(() => context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [2u], Variant.Null),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch))
            .ConfigureAwait(false);
        Assert.That(draft.InitialValue.IsNull, Is.True);
        Assert.That(draft.InitialValue.Elements.IsEmpty, Is.True);
    }

    [TestCase(ValueRanks.Scalar)]
    [TestCase(-4)]
    [TestCase(StructuredArrayDraft.MaximumRank + 1)]
    public async Task ScalarInvalidAndOverCapacityRanksCannotOpenArrayDraftsAsync(int rank)
    {
        using var context = new StructuredValueTestContext();
        await Assert.ThatAsync(() => context.Service.OpenArrayAsync(DataTypeIds.Int32, rank, [], Variant.Null),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadOutOfRange))
            .ConfigureAwait(false);
    }

    [TestCase(ValueRanks.OneDimension)]
    [TestCase(ValueRanks.ScalarOrOneDimension)]
    public async Task ArrayOnlyRanksRejectMatrixShapesAsync(int rank)
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, rank, [], Variant.From((ArrayOf<int>)[1, 2])).ConfigureAwait(false);
        Assert.That(() => draft.Reshape(draft.InitialValue, [1, 2]),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch));
        Assert.That(draft.TryCommit(draft.InitialValue, out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue(out ArrayOf<int> values), Is.True);
        Assert.That(values, Is.EqualTo((ArrayOf<int>)[1, 2]));
    }

    [Test]
    public async Task DeclaredDimensionsAreMaximumsWithZeroAsAnUnspecifiedBoundAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft bounded = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [2u, 3u], Variant.Null).ConfigureAwait(false);
        StructuredArrayValue below = bounded.Reshape(bounded.InitialValue, [1, 3], allowResize: true);
        Assert.That(bounded.TryCommit(below, out Variant smaller, out string? error), Is.True, error);
        AssertMatrix(context.RoundTrip(smaller), [1, 3], [0, 0, 0]);
        StructuredArrayValue at = bounded.Reshape(bounded.InitialValue, [2, 3], allowResize: true);
        Assert.That(bounded.TryCommit(at, out Variant maximum, out error), Is.True, error);
        AssertMatrix(context.RoundTrip(maximum), [2, 3], [0, 0, 0, 0, 0, 0]);
        Assert.That(() => bounded.Reshape(bounded.InitialValue, [3, 2], allowResize: true),
            Throws.TypeOf<ServiceResultException>().With.Message.Contains("declared maximum"));

        StructuredArrayDraft unspecified = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, ValueRanks.Any, [0u, 3u], Variant.Null).ConfigureAwait(false);
        StructuredArrayValue wider = unspecified.Reshape(unspecified.InitialValue, [4, 3], allowResize: true);
        Assert.That(unspecified.TryCommit(wider, out Variant unbounded, out error), Is.True, error);
        AssertMatrix(context.RoundTrip(unbounded), [4, 3], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.That(() => unspecified.Reshape(unspecified.InitialValue, [1, 4], allowResize: true),
            Throws.TypeOf<ServiceResultException>().With.Message.Contains("declared maximum"));
    }

    [Test]
    public async Task NegativeDimensionsAndCheckedProductsCannotWrapOrAllocateAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, ValueRanks.Any, [], Variant.Null).ConfigureAwait(false);
        ArrayOf<ArrayOf<int>> invalid = [[-1, -1], [0, -1], [65536, 65536], [int.MaxValue, int.MaxValue, int.MaxValue]];
        foreach (ArrayOf<int> dimensions in invalid)
        {
            Assert.That(() => draft.Reshape(draft.InitialValue, dimensions, allowResize: true),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadOutOfRange));
        }
        Assert.That(draft.InitialValue.Elements.IsEmpty, Is.True);
        Assert.That(draft.InitialValue.IsNull, Is.True);
    }

    [Test]
    public async Task SessionCapacityAcceptsBelowAndAtTheLimitAndRejectsOneMoreElementAsync()
    {
        using var context = new StructuredValueTestContext();
        context.MessageContext.MaxArrayLength = 4;
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], Variant.Null).ConfigureAwait(false);
        Assert.That(draft.ElementCapacity, Is.EqualTo(4));
        StructuredArrayValue below = draft.Reshape(draft.InitialValue, [1, 3], allowResize: true);
        StructuredArrayValue at = draft.Reshape(draft.InitialValue, [2, 2], allowResize: true);
        Assert.That(draft.TryCommit(below, out Variant three, out string? error), Is.True, error);
        AssertMatrix(context.RoundTrip(three), [1, 3], [0, 0, 0]);
        Assert.That(draft.TryCommit(at, out Variant four, out error), Is.True, error);
        AssertMatrix(context.RoundTrip(four), [2, 2], [0, 0, 0, 0]);
        Assert.That(() => draft.Reshape(draft.InitialValue, [1, 5], allowResize: true),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public async Task AnUnlimitedSessionStillHonorsTheExactEditorAllocationLimitAsync()
    {
        using var context = new StructuredValueTestContext();
        context.MessageContext.MaxArrayLength = 0;
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], Variant.Null).ConfigureAwait(false);
        Assert.That(draft.ElementCapacity, Is.EqualTo(StructuredArrayDraft.MaximumElementCount));
        StructuredArrayValue maximum = draft.Reshape(
            draft.InitialValue, [1, StructuredArrayDraft.MaximumElementCount], allowResize: true);
        Assert.That(maximum.Elements.Count, Is.EqualTo(StructuredArrayDraft.MaximumElementCount));
        Assert.That(maximum.Elements[^1].TryGetValue(out int last), Is.True);
        Assert.That(last, Is.Zero);
        Assert.That(() => draft.Reshape(
            draft.InitialValue, [1, StructuredArrayDraft.MaximumElementCount + 1], allowResize: true),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public async Task NullAndEmptyArraysHaveDifferentActualWireLengthsAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, ValueRanks.OneDimension, [], Variant.Null).ConfigureAwait(false);
        Assert.That(draft.TryCommit(draft.InitialValue, out Variant nullValue, out string? error), Is.True, error);
        StructuredArrayValue empty = draft.WithElements(draft.InitialValue, []);
        Assert.That(draft.TryCommit(empty, out Variant emptyValue, out error), Is.True, error);
        Assert.That(context.EncodeVariant(nullValue), Is.EqualTo(ByteString.From([0x86, 0xFF, 0xFF, 0xFF, 0xFF])));
        Assert.That(context.EncodeVariant(emptyValue), Is.EqualTo(ByteString.From([0x86, 0, 0, 0, 0])));
        Assert.That(context.RoundTrip(nullValue).TryGetValue(out ArrayOf<int> nullArray), Is.True);
        Assert.That(nullArray.IsNull, Is.True);
        Assert.That(context.RoundTrip(emptyValue).TryGetValue(out ArrayOf<int> emptyArray), Is.True);
        Assert.That(emptyArray.IsNull, Is.False);
        Assert.That(emptyArray.IsEmpty, Is.True);
        Assert.That(draft.InitialValue.IsNull, Is.True);
    }

    [Test]
    public async Task NullAndEmptyMatrixFieldsRetainRankAndDistinctRawEncodingsAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [2u, 3u], Variant.Null, isStructureField: true).ConfigureAwait(false);
        Assert.That(draft.TryCommit(draft.InitialValue, out Variant nullValue, out string? error), Is.True, error);
        StructuredArrayValue empty = draft.Reshape(draft.InitialValue, [0, 3]);
        Assert.That(draft.TryCommit(empty, out Variant emptyValue, out error), Is.True, error);
        Assert.That(context.RoundTrip(nullValue, raw: true).TryGetValue(out MatrixOf<int> nullMatrix), Is.True);
        Assert.That(nullMatrix.IsNull, Is.True);
        Assert.That(nullMatrix.Dimensions, Is.EqualTo(s_nullMatrixDimensions));
        Assert.That(context.RoundTrip(emptyValue, raw: true).TryGetValue(out MatrixOf<int> emptyMatrix), Is.True);
        Assert.That(emptyMatrix.IsNull, Is.False);
        Assert.That(emptyMatrix.IsEmpty, Is.True);
        Assert.That(emptyMatrix.Dimensions, Is.EqualTo(s_emptyMatrixDimensions));
        Assert.That(context.EncodeVariant(nullValue, raw: true),
            Is.Not.EqualTo(context.EncodeVariant(emptyValue, raw: true)));
        Assert.That(draft.InitialValue.IsNull, Is.True);
    }

    [Test]
    public async Task EmptyMatrixVariantsFailBeforePublishingInsteadOfBeingSilentlyFlattenedAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], Variant.Null).ConfigureAwait(false);
        Assert.That(draft.TryCommit(draft.InitialValue, out Variant nullResult, out string? nullError), Is.False);
        Assert.That(nullResult.IsNull, Is.True);
        Assert.That(nullError, Does.Contain("matrix"));
        StructuredArrayValue empty = draft.Reshape(draft.InitialValue, [0, 3]);
        Assert.That(draft.TryCommit(empty, out Variant emptyResult, out string? emptyError), Is.False);
        Assert.That(emptyResult.IsNull, Is.True);
        Assert.That(emptyError, Does.Contain("matrix"));
        Assert.That(empty.Dimensions, Is.EqualTo((ArrayOf<int>)[0, 3]));
        Assert.That(empty.IsNull, Is.False);
    }

    [Test]
    public async Task SettingNullRequiresExplicitDiscardAndDoesNotMutateEarlierResultsAsync()
    {
        using var context = new StructuredValueTestContext();
        Variant original = Matrix([1, 2], [1, 2]);
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], original, isStructureField: true).ConfigureAwait(false);
        Assert.That(() => draft.AsNull(draft.InitialValue),
            Throws.TypeOf<ServiceResultException>().With.Message.Contains("Explicitly"));
        StructuredArrayValue cleared = draft.AsNull(draft.InitialValue, allowDiscard: true);
        Assert.That(draft.TryCommit(cleared, out Variant committed, out string? error), Is.True, error);
        Assert.That(committed.TryGetValue(out MatrixOf<int> result), Is.True);
        Assert.That(result.IsNull, Is.True);
        AssertMatrix(original, [1, 2], [1, 2]);
        Assert.That(draft.InitialValue.Elements.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task MatrixElementCountTypeAndNullContradictionsCannotCommitAsync()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], Matrix([1, 2], [1, 2])).ConfigureAwait(false);
        ArrayOf<StructuredArrayValue> invalid =
        [
            new(BuiltInType.Int32, [Variant.From(1)], [1, 2]),
            new(BuiltInType.String, [Variant.From("one"), Variant.From("two")], [1, 2]),
            new(BuiltInType.Int32, [Variant.From("one"), Variant.From(2)], [1, 2]),
            new(BuiltInType.Int32, [Variant.From(1), Variant.From(2)], [1, 2], isNull: true),
            new(BuiltInType.Int32, [Variant.From(1), Variant.From(2)], [2])
        ];
        foreach (StructuredArrayValue value in invalid)
        {
            Assert.That(draft.TryCommit(value, out Variant failed, out string? error), Is.False);
            Assert.That(failed.IsNull, Is.True);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }
        Assert.That(draft.TryCommit(draft.InitialValue, out Variant retained, out string? retainedError),
            Is.True, retainedError);
        AssertMatrix(retained, [1, 2], [1, 2]);
    }

    [Test]
    public async Task StructuredElementsDimensionsAndDiscardedCandidatesAreDeeplyIsolatedAsync()
    {
        using var context = new StructuredValueTestContext();
        var child = new Argument { Name = "Original", DataType = DataTypeIds.Int32 };
        int[] dimensions = [1, 2];
        var source = new StructuredArrayValue(
            BuiltInType.ExtensionObject, [Variant.FromStructure(child), Variant.FromStructure(child)], dimensions);
        dimensions[0] = 99;
        Assert.That(source.Dimensions, Is.EqualTo((ArrayOf<int>)[1, 2]));
        Variant original = source.ToVariant(context.MessageContext);
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Argument, 2, [], original).ConfigureAwait(false);
        StructuredArrayValue reshaped = draft.Reshape(draft.InitialValue, [2, 1]);
        Assert.That(reshaped.Elements[0].TryGetValue<Argument>(out Argument? candidate, context.MessageContext),
            Is.True);
        candidate!.Name = "Discarded";

        Assert.That(draft.InitialValue.Elements[0].TryGetValue<Argument>(
            out Argument? unchanged, context.MessageContext), Is.True);
        Assert.That(unchanged!.Name, Is.EqualTo("Original"));
        Assert.That(child.Name, Is.EqualTo("Original"));
        Assert.That(draft.TryCommit(draft.InitialValue, out Variant retained, out string? error), Is.True, error);
        StructuredArrayValue roundTrip = StructuredArrayValue.Read(context.RoundTrip(retained), context.MessageContext);
        Assert.That(roundTrip.Dimensions, Is.EqualTo((ArrayOf<int>)[1, 2]));
        Assert.That(roundTrip.Elements[0].TryGetValue<Argument>(
            out Argument? encoded, context.MessageContext), Is.True);
        Assert.That(encoded!.Name, Is.EqualTo("Original"));
    }

    [TestCase("")]
    [TestCase("2")]
    [TestCase("2,")]
    [TestCase("2,,3")]
    [TestCase("2,-1")]
    [TestCase("2,3.5")]
    [TestCase("2,2147483648")]
    [TestCase("2x3")]
    public void InvalidDimensionTextFailsExplicitly(string text)
    {
        Assert.That(() => StructuredArrayDraft.ParseDimensions(text), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void DimensionTextPreservesZeroAndTheMaximumSupportedRank()
    {
        Assert.That(StructuredArrayDraft.ParseDimensions(" 2, 0, 3 "), Is.EqualTo((ArrayOf<int>)[2, 0, 3]));
        var parts = new string[StructuredArrayDraft.MaximumRank];
        Array.Fill(parts, "1");
        string maximum = string.Join(",", parts);
        Assert.That(StructuredArrayDraft.ParseDimensions(maximum).Count,
            Is.EqualTo(StructuredArrayDraft.MaximumRank));
        Assert.That(() => StructuredArrayDraft.ParseDimensions($"{maximum},1"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public async Task CancellationBeforeAndAfterOpeningPreventsArrayPublicationAndFurtherReshapeAsync()
    {
        using var context = new StructuredValueTestContext();
        using var cancellation = new CancellationTokenSource();
        Variant original = Matrix([1, 2], [1, 2]);
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], original, cancellationToken: cancellation.Token).ConfigureAwait(false);
        StructuredArrayValue candidate = draft.Reshape(draft.InitialValue, [2, 1]);
        cancellation.Cancel();

        Assert.That(draft.TryCommit(candidate, out Variant failed, out string? error), Is.False);
        Assert.That(failed.IsNull, Is.True);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
        Assert.That(() => draft.Reshape(candidate, [1, 2]), Throws.InstanceOf<OperationCanceledException>());
        await Assert.ThatAsync(() => context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], original, cancellationToken: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        AssertMatrix(original, [1, 2], [1, 2]);
    }

    [TestCase("Refresh")]
    [TestCase("Session")]
    [TestCase("Namespaces")]
    [TestCase("Endpoint")]
    [TestCase("Server")]
    [TestCase("Disconnect")]
    [TestCase("Context")]
    public async Task StaleArrayDraftsCannotPublishReshapedCandidatesAsync(string change)
    {
        using var context = new StructuredValueTestContext();
        Variant original = Matrix([1, 2], [1, 2]);
        StructuredArrayDraft draft = await context.Service.OpenArrayAsync(
            DataTypeIds.Int32, 2, [], original).ConfigureAwait(false);
        StructuredArrayValue candidate = draft.Reshape(draft.InitialValue, [2, 1]);
        context.Invalidate(change);

        Assert.That(draft.TryCommit(candidate, out Variant failed, out string? error), Is.False);
        Assert.That(failed.IsNull, Is.True);
        Assert.That(error, Does.Contain("Reload"));
        Assert.That(() => draft.WithElements(candidate, [Variant.From(3), Variant.From(4)]),
            Throws.TypeOf<ServiceResultException>().With.Message.Contains("Reload"));
        AssertMatrix(original, [1, 2], [1, 2]);
    }

    private static Variant Matrix(ArrayOf<int> values, ArrayOf<int> dimensions)
    {
        return Variant.From(values.ToMatrix(dimensions));
    }

    private static void AssertMatrix(Variant value, ArrayOf<int> dimensions, ArrayOf<int> expected)
    {
        Assert.That(value.TryGetValue(out MatrixOf<int> matrix), Is.True);
        Assert.That(matrix.Dimensions, Is.EqualTo(dimensions.ToArray()));
        Assert.That(matrix.ToArrayOf(), Is.EqualTo(expected));
    }

    private static readonly int[] s_nullMatrixDimensions = [0, 0];
    private static readonly int[] s_emptyMatrixDimensions = [0, 3];
}
