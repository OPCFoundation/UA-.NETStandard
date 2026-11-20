// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

internal static class StructuredEditorActions
{
    public static StackPanel Body(ComplexValueEditor editor)
    {
        return DesktopInteraction.Control<StackPanel>(editor, "BodyPanel");
    }

    public static void ApplyShape(ComplexValueEditor editor, string shape, bool resize)
    {
        StackPanel body = Body(editor);
        body.Children.OfType<CheckBox>().Single().IsChecked = resize;
        body.Children.OfType<TextBox>().Single().Text = shape;
        DesktopInteraction.Click(body.Children.OfType<Button>()
            .Single(button => button.Content is "Apply shape / create matrix"));
    }

    public static void AssertMatrix(Variant value, int[] dimensions, int[] elements)
    {
        Assert.That(value.TryGetValue(out MatrixOf<int> matrix), Is.True);
        Assert.That(matrix.Dimensions, Is.EqualTo(dimensions));
        Assert.That(matrix.ToArrayOf().ToArray(), Is.EqualTo(elements));
    }

    public static Variant Commit(ComplexValueEditor editor)
    {
        Assert.That(editor.TryCommit(out Variant value, out string? error), Is.True, error);
        return value;
    }
}
