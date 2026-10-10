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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace UaLens.Views;

/// <summary>
/// Composite picker for a UTC <see cref="DateTime"/> — combines an
/// Avalonia <see cref="CalendarDatePicker"/> (date part) with a
/// <see cref="TimePicker"/> (time-of-day) and exposes a single
/// <see cref="Value"/> bindable property kept in UTC.
/// </summary>
internal sealed partial class UtcDateTimePicker : UserControl
{
    public static readonly StyledProperty<DateTime> ValueProperty =
        AvaloniaProperty.Register<UtcDateTimePicker, DateTime>(
            nameof(Value),
            defaultValue: DateTime.UtcNow,
            defaultBindingMode: BindingMode.TwoWay);

    public DateTime Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private CalendarDatePicker m_date = null!;
    private TimePicker m_time = null!;
    private bool m_suppress;

    public UtcDateTimePicker()
    {
        InitializeComponent();
        m_date = this.FindControl<CalendarDatePicker>("DatePart")
                 ?? throw new InvalidOperationException("DatePart control missing.");
        m_time = this.FindControl<TimePicker>("TimePart")
                 ?? throw new InvalidOperationException("TimePart control missing.");
        m_date.SelectedDateChanged += (_, _) => Recompose();
        m_time.SelectedTimeChanged += (_, _) => Recompose();
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (m_suppress || m_date is null || m_time is null)
        {
            return;
        }
        m_suppress = true;
        try
        {
            DateTime v = Value.Kind == DateTimeKind.Utc ? Value : Value.ToUniversalTime();
            m_date.SelectedDate = v.Date;
            m_time.SelectedTime = v.TimeOfDay;
        }
        finally
        {
            m_suppress = false;
        }
    }

    private void Recompose()
    {
        if (m_suppress)
        {
            return;
        }
        DateTime date = m_date.SelectedDate?.Date ?? DateTime.UtcNow.Date;
        TimeSpan time = m_time.SelectedTime ?? TimeSpan.Zero;
        m_suppress = true;
        try
        {
            Value = DateTime.SpecifyKind(date.Add(time), DateTimeKind.Utc);
        }
        finally
        {
            m_suppress = false;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
