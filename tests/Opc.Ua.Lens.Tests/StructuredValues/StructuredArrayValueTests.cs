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

using NUnit.Framework;
using Opc.Ua;
using UaLens.StructuredValues;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class StructuredArrayValueTests
{
    [Test]
    public void TypedArrayElementsAreNotStringifiedAndEmptyArraysKeepTheirElementType()
    {
        using var context = new StructuredValueTestContext();
        Variant original = Variant.From((ArrayOf<int>)[1, 2]);
        StructuredArrayValue snapshot = StructuredArrayValue.Read(original, context.MessageContext);
        Assert.That(snapshot.Elements[0].TryGetValue(out int first), Is.True);
        Assert.That(first, Is.EqualTo(1));
        Variant edited = snapshot.WithElements([Variant.From(3), Variant.From(4)], context.MessageContext);
        Assert.That(edited.TryGetValue(out ArrayOf<int> values), Is.True);
        Assert.That(values, Is.EqualTo((ArrayOf<int>)[3, 4]));
        Assert.That(original.TryGetValue(out ArrayOf<int> unchanged), Is.True);
        Assert.That(unchanged, Is.EqualTo((ArrayOf<int>)[1, 2]));

        Variant empty = snapshot.WithElements([], context.MessageContext);
        Assert.That(empty.TryGetValue(out ArrayOf<int> emptyValues), Is.True);
        Assert.That(emptyValues.IsEmpty, Is.True);
        Assert.That(emptyValues.IsNull, Is.False);
    }

    [Test]
    public void NullAndEmptyArraysRemainDistinct()
    {
        using var context = new StructuredValueTestContext();
        ArrayOf<int> nullArray = default;
        StructuredArrayValue snapshot = StructuredArrayValue.Read(
            Variant.From(nullArray), context.MessageContext);
        Assert.That(snapshot.IsNull, Is.True);

        Variant retained = snapshot.ToVariant(context.MessageContext);
        Assert.That(retained.TryGetValue(out ArrayOf<int> retainedValues), Is.True);
        Assert.That(retainedValues.IsNull, Is.True);
        Variant emptied = snapshot.WithElements([], context.MessageContext);
        Assert.That(emptied.TryGetValue(out ArrayOf<int> emptyValues), Is.True);
        Assert.That(emptyValues.IsNull, Is.False);
        Assert.That(emptyValues.IsEmpty, Is.True);
    }

    [Test]
    public void ByteStringAndEnumerationArraysPreserveTheirWireTypes()
    {
        using var context = new StructuredValueTestContext();
        ArrayOf<ByteString> bytes = [ByteString.From([0, 255]), default(ByteString)];
        StructuredArrayValue byteSnapshot = StructuredArrayValue.Read(Variant.From(bytes), context.MessageContext);
        Variant copiedBytes = byteSnapshot.ToVariant(context.MessageContext);
        Assert.That(copiedBytes.TryGetValue(out ArrayOf<ByteString> resultBytes), Is.True);
        Assert.That(resultBytes, Is.EqualTo(bytes));

        ArrayOf<NamingRuleType> enums = [NamingRuleType.Mandatory, NamingRuleType.Optional];
        StructuredArrayValue enumSnapshot = StructuredArrayValue.Read(Variant.From(enums), context.MessageContext);
        Variant edited = enumSnapshot.WithElements(
            [Variant.From(NamingRuleType.Optional), Variant.From(NamingRuleType.Mandatory)], context.MessageContext);
        Assert.That(edited.TryGetValue(out ArrayOf<NamingRuleType> resultEnums), Is.True);
        Assert.That(resultEnums,
            Is.EqualTo((ArrayOf<NamingRuleType>)[NamingRuleType.Optional, NamingRuleType.Mandatory]));
        Assert.That(edited.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
    }

    [Test]
    public void MatrixEditsRetainDimensionsAndRejectAccidentalResizing()
    {
        using var context = new StructuredValueTestContext();
        ArrayOf<int> flat = [1, 2, 3, 4];
        Variant original = Variant.From(flat.ToMatrix([2, 2]));
        StructuredArrayValue snapshot = StructuredArrayValue.Read(original, context.MessageContext);
        Assert.That(snapshot.Dimensions, Is.EqualTo((ArrayOf<int>)[2, 2]));

        Variant edited = snapshot.WithElements(
            [Variant.From(4), Variant.From(3), Variant.From(2), Variant.From(1)], context.MessageContext);
        Assert.That(edited.TryGetValue(out MatrixOf<int> matrix), Is.True);
        Assert.That(matrix.Dimensions, Is.EqualTo(s_matrixDimensions));
        Assert.That(matrix.ToArrayOf(), Is.EqualTo((ArrayOf<int>)[4, 3, 2, 1]));
        Assert.That(() => snapshot.WithElements([Variant.From(1)], context.MessageContext),
            Throws.TypeOf<ServiceResultException>());
    }

    [Test]
    public void MismatchedArrayElementCannotBeSilentlyCoercedOrDropped()
    {
        using var context = new StructuredValueTestContext();
        StructuredArrayValue snapshot = StructuredArrayValue.Read(
            Variant.From((ArrayOf<int>)[1]), context.MessageContext);
        Assert.That(() => snapshot.WithElements([Variant.From("not an integer")], context.MessageContext),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(snapshot.Elements[0].TryGetValue(out int original), Is.True);
        Assert.That(original, Is.EqualTo(1));
    }

    [Test]
    public void VariantArraySupportsHeterogeneousElementsWithoutBoxing()
    {
        using var context = new StructuredValueTestContext();
        var snapshot = new StructuredArrayValue(BuiltInType.Variant, [Variant.From(1), Variant.From("two")]);
        Variant value = snapshot.ToVariant(context.MessageContext);
        Assert.That(value.TryGetValue(out ArrayOf<Variant> elements), Is.True);
        Assert.That(elements[0].TryGetValue(out int first), Is.True);
        Assert.That(first, Is.EqualTo(1));
        Assert.That(elements[1].TryGetValue(out string second), Is.True);
        Assert.That(second, Is.EqualTo("two"));
    }

    [Test]
    public void ScalarEditsPreserveWhitespaceLocalesTypedNullAndByteStringFormat()
    {
        Variant text = Variant.From("  original  ");
        Assert.That(StructuredScalarValue.TryParse(BuiltInType.String, "  edited  ", text,
            out Variant editedText, out string? error), Is.True, error);
        Assert.That(editedText.TryGetValue(out string parsed), Is.True);
        Assert.That(parsed, Is.EqualTo("  edited  "));

        Variant localized = Variant.From(new LocalizedText("de", "Original"));
        Assert.That(StructuredScalarValue.TryParse(BuiltInType.LocalizedText, "Updated", localized,
            out Variant editedLocalized, out error), Is.True, error);
        Assert.That(editedLocalized.TryGetValue(out LocalizedText result), Is.True);
        Assert.That(result.Locale, Is.EqualTo("de"));
        Assert.That(result.Text, Is.EqualTo("Updated"));

        Variant bytes = Variant.From(ByteString.From([0, 255]));
        Assert.That(StructuredScalarValue.Format(bytes), Is.EqualTo("AP8="));
        Variant nullBytes = Variant.From(default(ByteString));
        Assert.That(StructuredScalarValue.TryParse(BuiltInType.ByteString, string.Empty, nullBytes,
            out Variant retainedNull, out error), Is.True, error);
        Assert.That(retainedNull.TryGetValue(out ByteString retainedBytes), Is.True);
        Assert.That(retainedBytes.IsNull, Is.True);

        Assert.That(StructuredScalarValue.TryParse(BuiltInType.Variant, string.Empty, Variant.Null,
            out Variant nullVariant, out error), Is.True, error);
        Assert.That(nullVariant.IsNull, Is.True, "A present optional Variant may have a null payload.");
    }

    private static readonly int[] s_matrixDimensions = [2, 2];
}
