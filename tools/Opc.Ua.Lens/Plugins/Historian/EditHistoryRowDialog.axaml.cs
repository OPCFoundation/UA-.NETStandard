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
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Two modes the <see cref="EditHistoryRowDialog"/> can operate in:
/// edit an existing history row (timestamp is fixed, value is editable)
/// or insert a brand-new row (timestamp picker is editable).
/// </summary>
internal enum EditHistoryRowMode
{
    Edit,
    Insert
}

/// <summary>
/// Configuration object for <see cref="EditHistoryRowDialog"/>. Keeps the
/// dialog generic across the per-row edit flow (selected row in the table
/// or chart) and the "Insert…" flows opened from the toolbar / context
/// menus where no existing row is available to seed from.
/// </summary>
internal sealed record EditHistoryRowDialogOptions(
    EditHistoryRowMode Mode,
    NodeId NodeId,
    DateTime InitialTimestamp,
    double InitialValue,
    StatusCode InitialStatus,
    IReadOnlyList<PerformUpdateType>? AllowedActions = null,
    PerformUpdateType? DefaultAction = null,
    string? Hint = null);

/// <summary>
/// Modal editor for a single history row prior to a HistoryUpdate.  The
/// user picks Insert / Replace / Update (per Part 11 §6.8.2) and types a
/// new value; the result of <see cref="ShowDialog{T}(Window)"/> is the
/// populated <see cref="EditHistoryRowResult"/> or <c>null</c> on cancel.
/// </summary>
internal sealed partial class EditHistoryRowDialog : Window
{
    private readonly EditHistoryRowDialogOptions m_options;

    public EditHistoryRowResult? Result { get; private set; }

    /// <summary>Designer-only ctor (no-arg).</summary>
    public EditHistoryRowDialog()
        : this(new EditHistoryRowDialogOptions(
            EditHistoryRowMode.Edit,
            NodeId.Null,
            DateTime.UtcNow,
            0,
            StatusCodes.Good))
    {
    }

    /// <summary>
    /// Backwards-compatible per-row Edit ctor — delegates to the
    /// options-based ctor with <see cref="EditHistoryRowMode.Edit"/>.
    /// </summary>
    public EditHistoryRowDialog(NodeId nodeId, HistoryRow row)
        : this(new EditHistoryRowDialogOptions(
            EditHistoryRowMode.Edit,
            nodeId,
            row.SourceTimestamp,
            row.IsNumeric ? row.Numeric : 0.0,
            row.StatusCode,
            AllowedActions: new[]
            {
                PerformUpdateType.Insert,
                PerformUpdateType.Replace,
                PerformUpdateType.Update
            },
            DefaultAction: PerformUpdateType.Replace,
            Hint: "Edit the value + status for the selected history row. "
                + "Action selects the HistoryUpdate variant per OPC UA Part 11 §6.8."))
    {
    }

    public EditHistoryRowDialog(EditHistoryRowDialogOptions options)
    {
        m_options = options ?? throw new ArgumentNullException(nameof(options));
        InitializeComponent();
        Title = m_options.Mode == EditHistoryRowMode.Insert
            ? "Insert history row"
            : "Edit history row";

        TextBlock hint = this.RequiredControl<TextBlock>("HintLabel");
        if (!string.IsNullOrEmpty(m_options.Hint))
        {
            hint.Text = m_options.Hint;
        }
        else if (m_options.Mode == EditHistoryRowMode.Insert)
        {
            hint.Text = "Insert a new history row at the chosen timestamp. "
                + "Action selects the HistoryUpdate variant per OPC UA Part 11 §6.8.";
        }

        this.RequiredControl<TextBlock>("NodeIdLabel").Text =
            m_options.NodeId.ToString() ?? "(null)";

        var tsLabel = this.RequiredControl<TextBlock>("TimestampLabel");
        var tsPicker = this.RequiredControl<UtcDateTimePicker>("TimestampPicker");
        if (m_options.Mode == EditHistoryRowMode.Insert)
        {
            tsPicker.IsVisible = true;
            tsPicker.Value = m_options.InitialTimestamp;
        }
        else
        {
            tsLabel.IsVisible = true;
            tsLabel.Text = m_options.InitialTimestamp.ToString(
                "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        ComboBox combo = this.RequiredControl<ComboBox>("ActionCombo");
        IReadOnlyList<PerformUpdateType> allowed = m_options.AllowedActions
            ?? new[]
            {
                PerformUpdateType.Insert,
                PerformUpdateType.Replace,
                PerformUpdateType.Update
            };
        combo.ItemsSource = ActionLabels(allowed);
        PerformUpdateType defaultAction = m_options.DefaultAction
            ?? (m_options.Mode == EditHistoryRowMode.Insert
                ? PerformUpdateType.Insert
                : PerformUpdateType.Replace);
        int defaultIndex = 0;
        for (int i = 0; i < allowed.Count; i++)
        {
            if (allowed[i] == defaultAction)
            {
                defaultIndex = i;
                break;
            }
        }
        combo.SelectedIndex = defaultIndex;

        TextBox valueText = this.RequiredControl<TextBox>("ValueText");
        valueText.Text = m_options.InitialValue.ToString("R", CultureInfo.InvariantCulture);

        Button ok = this.RequiredControl<Button>("OkButton");
        Button cancel = this.RequiredControl<Button>("CancelButton");
        TextBlock resultLabel = this.RequiredControl<TextBlock>("ResultLabel");

        ok.Click += (_, _) =>
        {
            if (!double.TryParse(valueText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                resultLabel.Text = "Value must parse as a double.";
                return;
            }
            DateTime ts = m_options.Mode == EditHistoryRowMode.Insert
                ? DateTime.SpecifyKind(tsPicker.Value, DateTimeKind.Utc)
                : m_options.InitialTimestamp;
            PerformUpdateType action = combo.SelectedIndex >= 0
                && combo.SelectedIndex < allowed.Count
                    ? allowed[combo.SelectedIndex]
                    : defaultAction;
            Result = new EditHistoryRowResult
            {
                NodeId = m_options.NodeId,
                Timestamp = ts,
                Action = action,
                Value = new Variant(parsed),
                Status = m_options.InitialStatus
            };
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private static string[] ActionLabels(IReadOnlyList<PerformUpdateType> actions)
    {
        var labels = new string[actions.Count];
        for (int i = 0; i < actions.Count; i++)
        {
            labels[i] = actions[i].ToString();
        }
        return labels;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>Strongly-typed return value from <see cref="EditHistoryRowDialog"/>.</summary>
internal sealed class EditHistoryRowResult
{
    public required NodeId NodeId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required PerformUpdateType Action { get; init; }
    public required Variant Value { get; init; }
    public required StatusCode Status { get; init; }
}
