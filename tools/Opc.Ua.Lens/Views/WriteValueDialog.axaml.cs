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
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
    public WriteValueDialog(
        NodeViewModel node,
        ISession session,
        WriteValueOperation? operation = null,
        IStructuredValueService? values = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_operation = operation ?? new WriteValueOperation(node.NodeId, session);
        m_values = values ?? (m_ownedValues = new SessionStructuredValueService(session));
        InitializeComponent();

        this.RequiredControl<TextBlock>("NodeIdLabel").Text = node.NodeId.ToString() ?? string.Empty;

        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var import = this.RequiredControl<Button>("ImportButton");
        ok.Click += async (_, _) =>
        {
            if (!m_writeTask.IsCompleted || !m_importTask.IsCompleted || m_closing || m_operation.WasDispatched)
            {
                return;
            }
            m_writeTask = OnWriteAsync();
            await m_writeTask.ConfigureAwait(true);
        };
        cancel.Click += (_, _) => Close();
        import.Click += async (_, _) =>
        {
            if (!m_importTask.IsCompleted || m_closing || !m_loaded || m_operation.WasDispatched)
            {
                return;
            }
            m_importTask = OnImportAsync();
            SetEditingEnabled(false);
            await m_importTask.ConfigureAwait(true);
            if (!m_closing)
            {
                SetEditingEnabled(true);
            }
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
        Closing += (_, args) =>
        {
            if (!m_allowClose)
            {
                args.Cancel = true;
                if (!m_closing)
                {
                    m_closeTask = RequestCloseAsync();
                }
            }
        };
        Closed += async (_, _) => await DisposeAsync().ConfigureAwait(true);
        SetEditingEnabled(false);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(m_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        m_closing = true;
        await m_operation.CancelAndDrainAsync().ConfigureAwait(true);
        await Task.WhenAll(m_loadTask, m_importTask, m_writeTask, m_closeTask).ConfigureAwait(true);
        await this.RequiredControl<ComplexValueEditor>("ComplexEditor").StopAsync().ConfigureAwait(true);
        m_ownedValues?.Dispose();
        await m_operation.DisposeAsync().ConfigureAwait(true);
        m_allowClose = true;
    }

    private async Task RequestCloseAsync()
    {
        m_closing = true;
        bool acknowledgeOutcome = m_operation.WasDispatched && !m_writeTask.IsCompleted;
        SetEditingEnabled(false);
        this.RequiredControl<Button>("CancelButton").IsEnabled = false;
        ShowResult(acknowledgeOutcome
            ? "Stopping the local wait… The server may still apply the write; cancellation is not rollback."
            : "Closing…");
        await m_operation.CancelAndDrainAsync().ConfigureAwait(true);
        await Task.WhenAll(m_loadTask, m_importTask, m_writeTask).ConfigureAwait(true);
        await this.RequiredControl<ComplexValueEditor>("ComplexEditor").StopAsync().ConfigureAwait(true);
        m_allowClose = true;
        if (acknowledgeOutcome)
        {
            ShowOutcome();
            this.RequiredControl<Button>("CancelButton").IsEnabled = true;
            this.RequiredControl<Button>("CancelButton").Focus();
        }
        else
        {
            Close();
        }
    }

    private void SetEditingEnabled(bool enabled)
    {
        this.RequiredControl<Button>("OkButton").IsEnabled = enabled;
        this.RequiredControl<Button>("ImportButton").IsEnabled = enabled;
        this.RequiredControl<TextBox>("ValueText").IsEnabled = enabled;
        this.RequiredControl<ComplexValueEditor>("ComplexEditor").IsEnabled = enabled;
        this.RequiredControl<Expander>("AdvancedExpander").IsEnabled = enabled;
    }

    private void ShowResult(string message, bool success = false)
    {
        var result = this.RequiredControl<TextBlock>("ResultLabel");
        result.Text = message;
        AutomationProperties.SetName(result, message);
        result.Classes.Set("write-success", success);
        result.Classes.Set("write-error", !success);
    }

    private void ShowOutcome()
    {
        ShowResult(m_operation.Outcome, m_operation.State == WriteValueState.Succeeded);
        this.RequiredControl<Button>("CancelButton").Content = "Acknowledge and close";
        this.RequiredControl<Button>("CancelButton").Focus();
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
            await m_operation.LoadAsync().ConfigureAwait(true);
            if (m_closing)
            {
                return;
            }
            if (m_operation.State != WriteValueState.Editing)
            {
                currentLbl.Text = "(unavailable)";
                ShowResult(m_operation.Outcome);
                return;
            }
            m_dataType = m_operation.DataType;
            m_valueRank = m_operation.ValueRank;
            dataTypeLbl.Text = $"{m_dataType}    rank={m_valueRank}";

            DataValue current = m_operation.CurrentValue;
            string formatted = FormatVariant(current.WrappedValue);
            currentLbl.Text = formatted;
            // Pre-fill the textbox with the current value so the user has a
            // concrete starting point.
            valueText.Text = formatted;

            // If the resolved DataType is a Structure or Enum and we are
            // editing a scalar, swap the primitive TextBox for the
            // structured editor.  The TextBox stays available as a fallback
            // when the server doesn't expose DataTypeDefinition.
            if (m_valueRank == ValueRanks.Scalar ||
                m_valueRank == ValueRanks.ScalarOrOneDimension ||
                m_valueRank == ValueRanks.Any)
            {
                DataTypeDefinition? def = await m_values.ResolveAsync(m_dataType, m_operation.CancellationToken)
                    .ConfigureAwait(true);
                if (m_closing)
                {
                    return;
                }
                if (def is StructureDefinition or EnumDefinition)
                {
                    m_definition = def;
                    await complexEditor.InitializeAsync(
                        m_dataType, def, m_values, current.WrappedValue, m_operation.CancellationToken)
                        .ConfigureAwait(true);
                    if (m_closing)
                    {
                        return;
                    }
                    complexEditor.IsVisible = true;
                    valueText.IsVisible = false;
                }
            }
            m_loaded = true;
            SetEditingEnabled(true);
            valueText.Focus();
        }
        catch (Exception ex)
        {
            if (!m_closing)
            {
                currentLbl.Text = $"(read failed: {ex.Message})";
                ShowResult($"Cannot write: metadata read failed. {ex.Message}");
            }
        }
    }

    private async Task OnImportAsync()
    {
        var valueText = this.RequiredControl<TextBox>("ValueText");
        try
        {
            (byte[] bytes, UaLens.Connection.EncodingFormat fmt, string name) =
                await UaLens.Views.EncodedValueIO.LoadAsync(this).ConfigureAwait(true);
            if (bytes.Length == 0 || m_closing)
            {
                return;
            }
            DataValue dv = UaLens.Connection.DataValueCodec.DecodeDataValue(
                bytes, fmt, m_session.MessageContext);
            if (m_definition is not null)
            {
                await this.RequiredControl<ComplexValueEditor>("ComplexEditor").InitializeAsync(
                    m_dataType, m_definition, m_values, dv.WrappedValue, m_operation.CancellationToken)
                    .ConfigureAwait(true);
            }
            if (m_closing)
            {
                return;
            }
            valueText.Text = FormatVariant(dv.WrappedValue);
            ShowResult($"Loaded value from {name} ({fmt}).", success: true);
        }
        catch (OperationCanceledException)
        {
            // user cancelled
        }
        catch (Exception ex)
        {
            if (!m_closing)
            {
                ShowResult($"Import failed: {ex.Message}");
            }
        }
    }

    private async Task OnWriteAsync()
    {
        var valueText = this.RequiredControl<TextBox>("ValueText");
        var complexEditor = this.RequiredControl<ComplexValueEditor>("ComplexEditor");
        if (!m_loaded || m_dataType.IsNull)
        {
            ShowResult("Cannot write: the value and its DataType metadata have not loaded successfully.");
            return;
        }

        Variant parsed;
        if (complexEditor.IsVisible)
        {
            if (!complexEditor.TryCommit(out parsed, out string? cerr))
            {
                ShowResult($"New value: {cerr}");
                complexEditor.Focus();
                return;
            }
        }
        else if (!VariantParser.TryParse(m_dataType, m_valueRank,
            valueText.Text ?? string.Empty, out parsed, out string? perr))
        {
            ShowResult($"New value: {perr}");
            AutomationProperties.SetHelpText(valueText, perr ?? "Invalid value.");
            valueText.Focus();
            return;
        }

        if (!TryBuildDataValue(parsed, out DataValue dataValue, out string? dverr))
        {
            ShowResult(dverr);
            this.RequiredControl<Expander>("AdvancedExpander").IsExpanded = true;
            this.RequiredControl<TextBox>("StatusText").Focus();
            return;
        }

        SetEditingEnabled(false);
        ShowResult("Writing…");
        this.RequiredControl<Button>("CancelButton").Content = "Cancel wait";
        await m_operation.WriteAsync(dataValue).ConfigureAwait(true);
        if (!m_closing)
        {
            ShowOutcome();
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

    private readonly ISession m_session;
    private readonly WriteValueOperation m_operation;
    private readonly IStructuredValueService m_values;
    private readonly SessionStructuredValueService? m_ownedValues;
    private NodeId m_dataType = NodeId.Null;
    private int m_valueRank = ValueRanks.Scalar;
    private DataTypeDefinition? m_definition;
    private Task m_loadTask = Task.CompletedTask;
    private Task m_importTask = Task.CompletedTask;
    private Task m_writeTask = Task.CompletedTask;
    private Task m_closeTask = Task.CompletedTask;
    private Task? m_disposal;
    private bool m_loaded;
    private bool m_closing;
    private bool m_allowClose;
}
