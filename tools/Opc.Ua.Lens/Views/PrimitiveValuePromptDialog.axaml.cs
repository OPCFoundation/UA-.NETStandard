// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using UaLens.StructuredValues;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Simple modal one-line text prompt used by <see cref="EditArrayDialog"/>
/// to edit a primitive array element.  Parses the entered text via
/// <see cref="VariantParser"/> using the supplied element DataType and
/// exposes the parsed Variant only after a successful commit.
/// </summary>
internal sealed partial class PrimitiveValuePromptDialog : Window
{
    public PrimitiveValuePromptDialog(BuiltInType elementType, Variant initial)
    {
        InitializeComponent();
        this.RequiredControl<TextBlock>("HeaderLabel").Text = $"Edit element  ({elementType})";
        this.RequiredControl<TextBlock>("HintLabel").Text =
            $"Element type: {elementType}. Enter a value in invariant culture; " +
            "press OK to commit.";

        TextBox box = this.RequiredControl<TextBox>("ValueText");
        TextBlock status = this.RequiredControl<TextBlock>("StatusLabel");
        box.Text = StructuredScalarValue.Format(initial);

        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            string txt = box.Text ?? string.Empty;
            if (!StructuredScalarValue.TryParse(elementType, txt, initial,
                out Variant parsed, out string? err))
            {
                status.Text = $"Parse error: {err}";
                status.Foreground = (Application.Current?.FindResource("ErrorText") as IBrush)
                    ?? Brushes.Transparent;
                return;
            }
            Result = parsed;
            WasCommitted = true;
            Close();
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close();
    }

    public Variant Result { get; private set; }

    public bool WasCommitted { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
