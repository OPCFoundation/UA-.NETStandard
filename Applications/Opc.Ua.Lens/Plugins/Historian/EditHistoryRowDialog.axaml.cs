/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Modal editor for a single history row prior to a HistoryUpdate.  The
/// user picks Insert / Replace / Update (per Part 11 §6.8.2) and types a
/// new value; the result of <see cref="ShowDialog{T}(Window)"/> is the
/// populated <see cref="EditHistoryRowResult"/> or <c>null</c> on cancel.
/// </summary>
internal sealed partial class EditHistoryRowDialog : Window
{
    private readonly NodeId m_nodeId;
    private readonly DateTime m_timestamp;
    private readonly double m_originalValue;
    private readonly StatusCode m_originalStatus;

    public EditHistoryRowResult? Result { get; private set; }

    public EditHistoryRowDialog()
    {
        m_nodeId = NodeId.Null;
        m_timestamp = DateTime.UtcNow;
        m_originalValue = 0;
        m_originalStatus = StatusCodes.Good;
        InitializeComponent();
    }

    public EditHistoryRowDialog(NodeId nodeId, HistoryRow row)
    {
        m_nodeId = nodeId;
        m_timestamp = row.SourceTimestamp;
        m_originalValue = row.IsNumeric ? row.Numeric : 0.0;
        m_originalStatus = row.StatusCode;
        InitializeComponent();

        this.RequiredControl<TextBlock>("NodeIdLabel").Text = nodeId.ToString() ?? "(null)";
        this.RequiredControl<TextBlock>("TimestampLabel").Text = row.DisplayTimestamp;

        ComboBox combo = this.RequiredControl<ComboBox>("ActionCombo");
        combo.ItemsSource = new[] { "Insert", "Replace", "Update" };
        combo.SelectedIndex = 1;

        TextBox valueText = this.RequiredControl<TextBox>("ValueText");
        valueText.Text = m_originalValue.ToString("R", CultureInfo.InvariantCulture);

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
            PerformUpdateType action = (combo.SelectedIndex) switch
            {
                0 => PerformUpdateType.Insert,
                2 => PerformUpdateType.Update,
                _ => PerformUpdateType.Replace
            };
            Result = new EditHistoryRowResult
            {
                NodeId = m_nodeId,
                Timestamp = m_timestamp,
                Action = action,
                Value = new Variant(parsed),
                Status = m_originalStatus
            };
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
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
