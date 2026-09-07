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
