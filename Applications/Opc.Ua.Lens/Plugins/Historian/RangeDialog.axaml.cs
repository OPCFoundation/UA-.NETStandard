/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Modal dialog that lets the user pick a Start/End UTC range using
/// two <see cref="UtcDateTimePicker"/>s.  Result is the picked
/// <c>(Start, End)</c> tuple, or null on cancel.
/// </summary>
internal sealed partial class RangeDialog : Window
{
    public (DateTime Start, DateTime End)? Result { get; private set; }

    public RangeDialog()
        : this(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow)
    {
    }

    public RangeDialog(DateTime start, DateTime end)
    {
        InitializeComponent();
        var startPicker = this.RequiredControl<UtcDateTimePicker>("StartPicker");
        var endPicker = this.RequiredControl<UtcDateTimePicker>("EndPicker");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        startPicker.Value = start.Kind == DateTimeKind.Utc ? start : start.ToUniversalTime();
        endPicker.Value = end.Kind == DateTimeKind.Utc ? end : end.ToUniversalTime();

        ok.Click += (_, _) =>
        {
            Result = (startPicker.Value, endPicker.Value);
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
