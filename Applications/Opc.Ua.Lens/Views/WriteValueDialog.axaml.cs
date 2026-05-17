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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Variable write dialog: reads the selected variable's
/// <c>Value</c> / <c>DataType</c> / <c>ValueRank</c> attributes once,
/// shows the current value pre-formatted, and lets the user enter a
/// replacement.  On OK parses via <see cref="VariantParser"/> and calls
/// <c>session.WriteAsync</c>; surfaces the resulting status code inline.
/// </summary>
internal sealed partial class WriteValueDialog : Window
{
    private readonly NodeViewModel m_node;
    private readonly ManagedSession m_session;
    private NodeId m_dataType = NodeId.Null;
    private int m_valueRank = ValueRanks.Scalar;

    public WriteValueDialog(NodeViewModel node, ManagedSession session)
    {
        m_node = node;
        m_session = session;
        InitializeComponent();

        this.RequiredControl<TextBlock>("NodeIdLabel").Text = node.NodeId.ToString() ?? string.Empty;

        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var import = this.RequiredControl<Button>("ImportButton");
        ok.Click += async (_, _) => await OnWrite().ConfigureAwait(false);
        cancel.Click += (_, _) => Close();
        import.Click += async (_, _) => await OnImportAsync().ConfigureAwait(true);

        // Defer the read until after the window is shown so the dialog
        // appears immediately with a "loading…" placeholder.
        Opened += async (_, _) => await LoadCurrentAsync().ConfigureAwait(false);
    }

    private async Task LoadCurrentAsync()
    {
        var dataTypeLbl = this.RequiredControl<TextBlock>("DataTypeLabel");
        var currentLbl = this.RequiredControl<TextBlock>("CurrentLabel");
        var valueText = this.RequiredControl<TextBox>("ValueText");
        dataTypeLbl.Text = "(loading…)";
        currentLbl.Text = "(loading…)";

        try
        {
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.ValueRank }
            ];
            ReadResponse resp = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                ids, CancellationToken.None).ConfigureAwait(true);

            if (resp.Results.Count >= 3 && !StatusCode.IsBad(resp.Results[1].StatusCode)
                && resp.Results[1].WrappedValue.TryGetValue(out NodeId dt))
            {
                m_dataType = dt;
            }
            if (resp.Results.Count >= 3 && !StatusCode.IsBad(resp.Results[2].StatusCode)
                && resp.Results[2].WrappedValue.TryGetValue(out int vr))
            {
                m_valueRank = vr;
            }
            dataTypeLbl.Text = $"{m_dataType}    rank={m_valueRank}";

            DataValue current = resp.Results.Count >= 1 ? resp.Results[0] : new DataValue();
            string formatted = FormatVariant(current.WrappedValue);
            currentLbl.Text = formatted;
            // Pre-fill the textbox with the current value so the user has a
            // concrete starting point.
            valueText.Text = formatted;
        }
        catch (Exception ex)
        {
            currentLbl.Text = $"(read failed: {ex.Message})";
        }
    }

    private async Task OnImportAsync()
    {
        var result = this.RequiredControl<TextBlock>("ResultLabel");
        var valueText = this.RequiredControl<TextBox>("ValueText");
        try
        {
            (byte[] bytes, UaLens.Connection.EncodingFormat fmt, string name) =
                await UaLens.Views.EncodedValueIO.LoadAsync(this).ConfigureAwait(true);
            if (bytes.Length == 0)
            {
                return;
            }
            DataValue dv = UaLens.Connection.DataValueCodec.DecodeDataValue(
                bytes, fmt, m_session.MessageContext);
            valueText.Text = FormatVariant(dv.WrappedValue);
            result.Text = $"Loaded value from {name} ({fmt}).";
            result.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        }
        catch (OperationCanceledException)
        {
            // user cancelled
        }
        catch (Exception ex)
        {
            result.Text = $"Import failed: {ex.Message}";
            result.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private async Task OnWrite()
    {
        var valueText = this.RequiredControl<TextBox>("ValueText");
        var result = this.RequiredControl<TextBlock>("ResultLabel");
        if (m_dataType.IsNull)
        {
            result.Text = "Cannot write — DataType not loaded.";
            result.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            return;
        }

        if (!VariantParser.TryParse(m_dataType, m_valueRank,
            valueText.Text ?? string.Empty, out Variant parsed, out string? perr))
        {
            result.Text = $"Parse error: {perr}";
            result.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            return;
        }

        try
        {
            ArrayOf<WriteValue> writes =
            [
                new WriteValue
                {
                    NodeId = m_node.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue { WrappedValue = parsed }
                }
            ];
            WriteResponse resp = await m_session.WriteAsync(null, writes, CancellationToken.None).ConfigureAwait(true);
            StatusCode sc = resp.Results.Count > 0 ? resp.Results[0] : StatusCodes.BadInternalError;
            if (StatusCode.IsGood(sc))
            {
                result.Text = $"Write OK: {sc}";
                result.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                // Close after a short delay so the user sees the success.
                await Task.Delay(450).ConfigureAwait(true);
                Close();
            }
            else
            {
                result.Text = $"Write failed: {sc}";
                result.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            }
        }
        catch (Exception ex)
        {
            result.Text = $"Write exception: {ex.Message}";
            result.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private static string FormatVariant(Variant v)
    {
        if (v.IsNull)
        {
            return "(null)";
        }

        object? boxed = v.AsBoxedObject();
        return boxed switch
        {
            null => "(null)",
            string s => s,
            LocalizedText l => l.Text ?? string.Empty,
            QualifiedName q => q.ToString() ?? string.Empty,
            Array a => FormatArray(a),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => boxed.ToString() ?? string.Empty
        };
    }

    private static string FormatArray(Array a)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            object? el = a.GetValue(i);
            sb.Append(el switch
            {
                null => "null",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => el.ToString() ?? string.Empty
            });
        }
        sb.Append(']');
        return sb.ToString();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
