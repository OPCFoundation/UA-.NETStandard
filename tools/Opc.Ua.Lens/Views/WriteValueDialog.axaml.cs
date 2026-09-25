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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.StructuredValues;
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
internal sealed partial class WriteValueDialog : Window, IAsyncDisposable
{
    private readonly NodeViewModel m_node;
    private readonly ISession m_session;
    private NodeId m_dataType = NodeId.Null;
    private int m_valueRank = ValueRanks.Scalar;
    private ArrayOf<uint> m_arrayDimensions;
    private DataTypeDefinition? m_definition;
    private bool m_loaded;

    public WriteValueDialog(
        NodeViewModel node,
        ISession session,
        IStructuredValueService? values = null,
        CancellationToken cancellationToken = default)
    {
        m_node = node ?? throw new ArgumentNullException(nameof(node));
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_values = values ?? (m_ownedValues = new SessionStructuredValueService(session));
        m_lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        InitializeComponent();

        this.RequiredControl<TextBlock>("NodeIdLabel").Text = node.NodeId.ToString() ?? string.Empty;

        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var import = this.RequiredControl<Button>("ImportButton");
        ok.Click += async (_, _) =>
        {
            if (!m_writeTask.IsCompleted)
            {
                return;
            }
            m_writeTask = OnWriteAsync();
            await m_writeTask.ConfigureAwait(true);
        };
        cancel.Click += (_, _) => Close();
        import.Click += async (_, _) =>
        {
            if (!m_importTask.IsCompleted)
            {
                return;
            }
            m_importTask = OnImportAsync();
            await m_importTask.ConfigureAwait(true);
        };

        WireOverride(
            this.RequiredControl<CheckBox>("StatusOverride"),
            this.RequiredControl<TextBox>("StatusText"));
        WireOverride(
            this.RequiredControl<CheckBox>("SourceOverride"),
            this.RequiredControl<UtcDateTimePicker>("SourceTimePicker"));
        WireOverride(
            this.RequiredControl<CheckBox>("ServerOverride"),
            this.RequiredControl<UtcDateTimePicker>("ServerTimePicker"));

        // Defer the read until after the window is shown so the dialog
        // appears immediately with a "loading…" placeholder.
        Opened += async (_, _) =>
        {
            m_loadTask = LoadCurrentAsync();
            await m_loadTask.ConfigureAwait(true);
        };
        Closed += async (_, _) => await StopAsync().ConfigureAwait(true);
    }

    public Task StopAsync()
    {
        return m_shutdown ??= StopCoreAsync();
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(StopAsync());
    }

    private async Task StopCoreAsync()
    {
        m_lifetime.Cancel();
        await this.RequiredControl<ComplexValueEditor>("ComplexEditor").StopAsync().ConfigureAwait(true);
        try
        {
            await Task.WhenAll(m_loadTask, m_writeTask, m_importTask).ConfigureAwait(true);
        }
        finally
        {
            m_ownedValues?.Dispose();
            m_lifetime.Dispose();
        }
    }

    private static void WireOverride(CheckBox toggle, Control partner)
    {
        partner.IsEnabled = toggle.IsChecked == true;
        toggle.IsCheckedChanged += (_, _) => partner.IsEnabled = toggle.IsChecked == true;
    }

    private async Task LoadCurrentAsync()
    {
        var dataTypeLbl = this.RequiredControl<TextBlock>("DataTypeLabel");
        var currentLbl = this.RequiredControl<TextBlock>("CurrentLabel");
        var valueText = this.RequiredControl<TextBox>("ValueText");
        var complexEditor = this.RequiredControl<ComplexValueEditor>("ComplexEditor");
        dataTypeLbl.Text = "(loading…)";
        currentLbl.Text = "(loading…)";
        m_loaded = false;

        try
        {
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.ValueRank },
                new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.ArrayDimensions }
            ];
            ReadResponse resp = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                ids, m_lifetime.Token).ConfigureAwait(true);
            m_lifetime.Token.ThrowIfCancellationRequested();
            if (!StatusCode.IsGood(resp.ResponseHeader.ServiceResult) || resp.Results.Count != 4)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The current-value read did not return all requested attributes.");
            }
            foreach (DataValue attribute in resp.Results)
            {
                if (!StatusCode.IsGood(attribute.StatusCode))
                {
                    throw new ServiceResultException(attribute.StatusCode);
                }
            }

            if (!resp.Results[1].WrappedValue.TryGetValue(out NodeId dataType) ||
                !resp.Results[2].WrappedValue.TryGetValue(out int valueRank) ||
                !resp.Results[3].WrappedValue.TryGetValue(out ArrayOf<uint> dimensions))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "Invalid DataType, ValueRank or ArrayDimensions.");
            }
            m_dataType = dataType;
            m_valueRank = valueRank;
            m_arrayDimensions = CoreUtils.Clone(dimensions);
            dataTypeLbl.Text = $"{m_dataType}    rank={m_valueRank}";

            DataValue current = resp.Results[0];
            string formatted = FormatVariant(current.WrappedValue);
            currentLbl.Text = formatted;
            // Pre-fill the textbox with the current value so the user has a
            // concrete starting point.
            valueText.Text = formatted;

            m_definition = await m_values.ResolveAsync(m_dataType, m_lifetime.Token).ConfigureAwait(true);
            if (m_definition is StructureDefinition or EnumDefinition ||
                StructuredArrayValue.RequiresEditor(m_valueRank, current.WrappedValue))
            {
                await complexEditor.InitializeValueAsync(
                    m_dataType, m_definition, m_values, current.WrappedValue,
                    m_valueRank, m_arrayDimensions, m_lifetime.Token)
                    .ConfigureAwait(true);
                complexEditor.IsVisible = true;
                valueText.IsVisible = false;
            }
            m_loaded = true;
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
            m_lifetime.Token.ThrowIfCancellationRequested();
            if (bytes.Length == 0)
            {
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The imported value file is empty.");
            }
            DataValue dv = UaLens.Connection.DataValueCodec.DecodeDataValue(
                bytes, fmt, m_session.MessageContext);
            if (m_definition is not null || StructuredArrayValue.RequiresEditor(m_valueRank, dv.WrappedValue))
            {
                ComplexValueEditor editor = this.RequiredControl<ComplexValueEditor>("ComplexEditor");
                await editor.InitializeValueAsync(
                    m_dataType, m_definition, m_values, dv.WrappedValue,
                    m_valueRank, m_arrayDimensions, m_lifetime.Token)
                    .ConfigureAwait(true);
                editor.IsVisible = true;
                valueText.IsVisible = false;
            }
            valueText.Text = FormatVariant(dv.WrappedValue);
            result.Text = $"Loaded value from {name} ({fmt}).";
            result.Foreground = (Application.Current?.FindResource("AccentGreen") as IBrush)
                ?? Brushes.Transparent;
        }
        catch (OperationCanceledException)
        {
            // user cancelled
        }
        catch (Exception ex)
        {
            result.Text = $"Import failed: {ex.Message}";
            result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
        }
    }

    private async Task OnWriteAsync()
    {
        var valueText = this.RequiredControl<TextBox>("ValueText");
        var complexEditor = this.RequiredControl<ComplexValueEditor>("ComplexEditor");
        var result = this.RequiredControl<TextBlock>("ResultLabel");
        if (!m_loaded || m_dataType.IsNull)
        {
            result.Text = "Cannot write — the value and its DataType metadata have not loaded successfully.";
            result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
            return;
        }

        Variant parsed;
        if (complexEditor.IsVisible)
        {
            if (!complexEditor.TryCommit(out parsed, out string? cerr))
            {
                result.Text = $"Editor: {cerr}";
                result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
                return;
            }
        }
        else if (!VariantParser.TryParse(m_dataType, m_valueRank,
            valueText.Text ?? string.Empty, out parsed, out string? perr))
        {
            result.Text = $"Parse error: {perr}";
            result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
            return;
        }

        if (!TryBuildDataValue(parsed, out DataValue dataValue, out string? dverr))
        {
            result.Text = dverr;
            result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
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
                    Value = dataValue
                }
            ];
            m_lifetime.Token.ThrowIfCancellationRequested();
            WriteResponse resp = await m_session.WriteAsync(null, writes, m_lifetime.Token).ConfigureAwait(true);
            StatusCode sc = !StatusCode.IsGood(resp.ResponseHeader.ServiceResult)
                ? resp.ResponseHeader.ServiceResult
                : resp.Results.Count == 1 ? resp.Results[0] : StatusCodes.BadDecodingError;
            if (StatusCode.IsGood(sc))
            {
                result.Text = $"Write OK: {sc}";
                result.Foreground = (Application.Current?.FindResource("AccentGreen") as IBrush)
                    ?? Brushes.Transparent;
                // Close after a short delay so the user sees the success.
                await Task.Delay(450, m_lifetime.Token).ConfigureAwait(true);
                Close();
            }
            else
            {
                result.Text = $"Write failed: {sc}";
                result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
            }
        }
        catch (Exception ex)
        {
            result.Text = $"Write exception: {ex.Message}";
            result.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
        }
    }

    /// <summary>
    /// Builds the <see cref="DataValue"/> to write, applying optional
    /// StatusCode / SourceTimestamp / ServerTimestamp overrides chosen via
    /// the "Advanced" expander.  When an override checkbox is unchecked,
    /// the corresponding field retains the existing default (Good /
    /// <see cref="DateTime.MinValue"/>).
    /// </summary>
    private bool TryBuildDataValue(Variant parsed, out DataValue dataValue, [NotNullWhen(false)] out string? error)
    {
        var statusOverride = this.RequiredControl<CheckBox>("StatusOverride");
        var sourceOverride = this.RequiredControl<CheckBox>("SourceOverride");
        var serverOverride = this.RequiredControl<CheckBox>("ServerOverride");
        var statusText = this.RequiredControl<TextBox>("StatusText");
        var sourceTime = this.RequiredControl<UtcDateTimePicker>("SourceTimePicker");
        var serverTime = this.RequiredControl<UtcDateTimePicker>("ServerTimePicker");

        dataValue = new DataValue(parsed);
        error = null;

        if (statusOverride.IsChecked == true)
        {
            if (!TryParseStatusCode(statusText.Text, out StatusCode sc))
            {
                error = $"Status code '{statusText.Text}' is not a recognised numeric or symbolic StatusCode.";
                return false;
            }
            dataValue = dataValue.WithStatus(sc);
        }

        if (sourceOverride.IsChecked == true)
        {
            dataValue = dataValue.WithSourceTimestamp(ToUtc(sourceTime.Value));
        }

        if (serverOverride.IsChecked == true)
        {
            dataValue = dataValue.WithServerTimestamp(ToUtc(serverTime.Value));
        }

        return true;
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    /// <summary>
    /// Parses a user-entered StatusCode: accepts an unsigned hex literal
    /// (<c>0x80000000</c>), a decimal value, or a symbolic id
    /// (<c>BadOutOfService</c>).  Returns <c>false</c> on unrecognised
    /// input so the caller can surface a clear error rather than silently
    /// defaulting to <see cref="StatusCodes.Good"/>.
    /// </summary>
    private static bool TryParseStatusCode(string? text, out StatusCode code)
    {
        code = StatusCodes.Good;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(
                    trimmed.AsSpan(2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint hex))
            {
                code = new StatusCode(hex);
                return true;
            }
            return false;
        }
        if (uint.TryParse(
                trimmed,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint dec))
        {
            code = new StatusCode(dec);
            return true;
        }
        // Symbolic id (e.g. "BadOutOfService") — scan the interned codes
        // for an exact case-sensitive match.  StatusCode.LookupSymbolicId
        // only goes uint→name, so we walk the public interned table.
        foreach (StatusCode interned in StatusCode.InternedStatusCodes)
        {
            if (string.Equals(interned.SymbolicId, trimmed, StringComparison.Ordinal))
            {
                code = interned;
                return true;
            }
        }
        return false;
    }

    private string FormatVariant(Variant value)
    {
        return StructuredScalarValue.FormatValue(value, m_session.MessageContext);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly IStructuredValueService m_values;
    private readonly SessionStructuredValueService? m_ownedValues;
    private readonly CancellationTokenSource m_lifetime;
    private Task m_loadTask = Task.CompletedTask;
    private Task m_writeTask = Task.CompletedTask;
    private Task m_importTask = Task.CompletedTask;
    private Task? m_shutdown;
}
