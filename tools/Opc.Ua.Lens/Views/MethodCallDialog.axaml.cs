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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.StructuredValues;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Method invocation dialog: browses the method's
/// <c>HasProperty(InputArguments)</c> property to discover the
/// argument list, renders one <see cref="TextBox"/> per argument,
/// then on OK parses each via <see cref="VariantParser"/>, builds a
/// <see cref="CallMethodRequest"/>, and surfaces the
/// <c>StatusCode</c> + <c>OutputArguments</c> in-dialog.
/// </summary>
internal sealed partial class MethodCallDialog : Window, IAsyncDisposable
{
    private readonly NodeViewModel m_method;
    private readonly ISession m_session;
    private NodeId m_objectId = NodeId.Null;
    private ArrayOf<Argument> m_arguments;
    private bool m_argumentsLoaded;
    public ObservableCollection<MethodArgRow> Inputs { get; } = new();
    public ObservableCollection<MethodOutputRow> Outputs { get; } = new();

    public MethodCallDialog(
        NodeViewModel method,
        ISession session,
        IStructuredValueService? values = null,
        CancellationToken cancellationToken = default)
    {
        m_method = method;
        m_session = session;
        m_values = values ?? (m_ownedValues = new SessionStructuredValueService(session));
        m_lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        InitializeComponent();

        this.RequiredControl<TextBlock>("MethodLabel").Text = $"Method  {m_method.NodeId}";
        this.RequiredControl<TextBlock>("ParentLabel").Text = "Parent  (resolving…)";
        this.RequiredControl<ItemsControl>("InputsList").ItemsSource = Inputs;
        this.RequiredControl<ItemsControl>("OutputsList").ItemsSource = Outputs;

        this.RequiredControl<Button>("OkButton").Click += async (_, _) =>
        {
            if (!m_callTask.IsCompleted)
            {
                return;
            }
            this.RequiredControl<Button>("OkButton").IsEnabled = false;
            m_callTask = OnCallAsync();
            try
            {
                await m_callTask.ConfigureAwait(true);
            }
            finally
            {
                this.RequiredControl<Button>("OkButton").IsEnabled = !m_lifetime.IsCancellationRequested;
            }
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close();

        Opened += async (_, _) =>
        {
            m_loadTask = LoadArgumentsAsync();
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
        m_nestedDialog?.Close();
        try
        {
            await Task.WhenAll(m_callTask, m_loadTask, m_auxiliaryTask).ConfigureAwait(true);
        }
        finally
        {
            m_ownedValues?.Dispose();
            m_lifetime.Dispose();
        }
    }

    private async Task LoadArgumentsAsync()
    {
        var parentLbl = this.RequiredControl<TextBlock>("ParentLabel");
        m_argumentsLoaded = false;
        try
        {
            // Step 1: parent ObjectId — prefer the cached ParentNodeId (set
            // when the user expanded the parent); fall back to an Inverse
            // HasComponent browse.
            if (!m_method.ParentNodeId.IsNull)
            {
                m_objectId = m_method.ParentNodeId;
            }
            else
            {
                m_objectId = await ResolveParentObjectAsync().ConfigureAwait(true);
            }
            parentLbl.Text = m_objectId.IsNull
                ? "Parent  (could not resolve)"
                : $"Parent  {m_objectId}";

            // Step 2: browse the method for HasProperty.InputArguments.
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = m_method.NodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.Variable,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br = await m_session.BrowseAsync(null, null, 0, browse,
                m_lifetime.Token).ConfigureAwait(true);
            if (!StatusCode.IsGood(br.ResponseHeader.ServiceResult) || br.Results.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The InputArguments browse returned an invalid response.");
            }
            if (!StatusCode.IsGood(br.Results[0].StatusCode))
            {
                throw new ServiceResultException(br.Results[0].StatusCode);
            }

            NodeId inputArgsId = NodeId.Null;
            if (br.Results.Count > 0 && !StatusCode.IsBad(br.Results[0].StatusCode))
            {
                foreach (ReferenceDescription r in br.Results[0].References)
                {
                    if (!r.BrowseName.IsNull
                        && string.Equals(r.BrowseName.Name, BrowseNames.InputArguments, StringComparison.Ordinal))
                    {
                        inputArgsId = ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris);
                        break;
                    }
                }
            }

            if (inputArgsId.IsNull)
            {
                // Method takes no inputs.
                m_arguments = [];
                Inputs.Clear();
                m_argumentsLoaded = true;
                return;
            }

            // Step 3: read the InputArguments property value (Argument[]).
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = inputArgsId, AttributeId = Attributes.Value }
            ];
            ReadResponse rr = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                ids, m_lifetime.Token).ConfigureAwait(true);
            if (!StatusCode.IsGood(rr.ResponseHeader.ServiceResult) || rr.Results.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The InputArguments read returned an invalid response.");
            }
            if (!StatusCode.IsGood(rr.Results[0].StatusCode))
            {
                throw new ServiceResultException(rr.Results[0].StatusCode);
            }
            if (!rr.Results[0].WrappedValue.TryGetValue(out ArrayOf<Argument> arguments, m_session.MessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "InputArguments is not an array of decoded Argument values.");
            }
            m_arguments = arguments;

            Inputs.Clear();
            foreach (Argument a in m_arguments)
            {
                var row = new MethodArgRow(a, FormatDefault(a));
                row.ImportCommand = new AsyncRelayCommand(() => StartAuxiliaryAsync(() => OnImportArgAsync(row)));
                row.EditComplexCommand = new AsyncRelayCommand(
                    () => StartAuxiliaryAsync(() => OnEditComplexArgAsync(row)));
                Inputs.Add(row);
            }

            // After the rows are built, probe each argument's DataType for
            // a StructureDefinition / EnumDefinition.  Rows whose type
            // resolves to a complex shape get an "Edit struct…" affordance
            // alongside their primitive TextBox; the textbox stays so
            // users can still hand-paste a JSON/XML payload.
            await ProbeComplexTypesAsync().ConfigureAwait(true);
            m_argumentsLoaded = true;
        }
        catch (Exception ex)
        {
            this.RequiredControl<TextBlock>("ResultStatus").Text = $"Failed to load arguments: {ex.Message}";
        }
    }

    private async Task ProbeComplexTypesAsync()
    {
        for (int i = 0; i < m_arguments.Count && i < Inputs.Count; i++)
        {
            Argument a = m_arguments[i];
            if (a.ValueRank != ValueRanks.Scalar
                && a.ValueRank != ValueRanks.ScalarOrOneDimension
                && a.ValueRank != ValueRanks.Any)
            {
                continue;
            }
            DataTypeDefinition? def = await m_values.ResolveAsync(a.DataType, m_lifetime.Token).ConfigureAwait(true);
            if (def is StructureDefinition or EnumDefinition)
            {
                Inputs[i].Definition = def;
                Inputs[i].IsComplex = true;
            }
        }
    }

    private async Task<NodeId> ResolveParentObjectAsync()
    {
        try
        {
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = m_method.NodeId,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br = await m_session.BrowseAsync(null, null, 0, browse,
                m_lifetime.Token).ConfigureAwait(true);
            if (br.Results.Count > 0 && !StatusCode.IsBad(br.Results[0].StatusCode)
                && br.Results[0].References.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(br.Results[0].References[0].NodeId, m_session.NamespaceUris);
            }
        }
        catch
        {
            // ignore — m_objectId stays null and the user gets a clear error.
        }
        return NodeId.Null;
    }

    private async Task OnCallAsync()
    {
        var statusLbl = this.RequiredControl<TextBlock>("ResultStatus");
        statusLbl.Foreground = (Application.Current?.FindResource("TextPrimary") as IBrush)
            ?? Brushes.Transparent;
        Outputs.Clear();

        if (!m_argumentsLoaded || m_objectId.IsNull)
        {
            statusLbl.Text = "Cannot call — the parent and argument metadata have not loaded successfully.";
            return;
        }

        // Parse every input argument; abort on first error.
        var parsed = new List<Variant>(m_arguments.Count);
        for (int i = 0; i < m_arguments.Count; i++)
        {
            Argument a = m_arguments[i];
            MethodArgRow? row = i < Inputs.Count ? Inputs[i] : null;
            if (row?.CachedVariant is { IsNull: false } cached)
            {
                // Complex-edit / import path bypassed the text parser.
                parsed.Add(cached);
                continue;
            }
            string txt = row?.ValueText ?? string.Empty;
            if (!VariantParser.TryParse(a.DataType, a.ValueRank, txt,
                out Variant v, out string? perr))
            {
                statusLbl.Text = $"Argument '{a.Name}' parse error: {perr}";
                statusLbl.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
                return;
            }
            parsed.Add(v);
        }

        try
        {
            ArrayOf<CallMethodRequest> calls =
            [
                new CallMethodRequest
                {
                    ObjectId = m_objectId,
                    MethodId = m_method.NodeId,
                    InputArguments = new ArrayOf<Variant>(parsed.ToArray())
                }
            ];
            m_lifetime.Token.ThrowIfCancellationRequested();
            CallResponse resp = await m_session.CallAsync(null, calls, m_lifetime.Token).ConfigureAwait(true);
            if (resp.Results.Count == 0)
            {
                statusLbl.Text = "(no result)";
                return;
            }
            CallMethodResult cmr = resp.Results[0];
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"StatusCode: {cmr.StatusCode}");
            if (cmr.InputArgumentResults.Count > 0)
            {
                sb.Append("    InputArgumentResults: ");
                for (int i = 0; i < cmr.InputArgumentResults.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(cmr.InputArgumentResults[i]);
                }
            }
            statusLbl.Text = sb.ToString();
            statusLbl.Foreground = StatusCode.IsGood(cmr.StatusCode)
                ? (Application.Current?.FindResource("AccentGreen") as IBrush)
                    ?? Brushes.Transparent
                : (Application.Current?.FindResource("AccentRedLight") as IBrush)
                    ?? Brushes.Transparent;
            // Per-output rows so the user sees them in a table.
            for (int i = 0; i < cmr.OutputArguments.Count; i++)
            {
                Variant v = cmr.OutputArguments[i];
                string dt = v.TypeInfo.BuiltInType.ToString();
                if (v.TypeInfo.ValueRank != ValueRanks.Scalar)
                {
                    dt += "[]";
                }
                Outputs.Add(new MethodOutputRow(i, dt, FormatVariant(v)));
            }
        }
        catch (Exception ex)
        {
            statusLbl.Text = $"Call exception: {ex.Message}";
            statusLbl.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
        }
    }

    private async Task OnImportArgAsync(MethodArgRow row)
    {
        var statusLbl = this.RequiredControl<TextBlock>("ResultStatus");
        try
        {
            (byte[] bytes, UaLens.Connection.EncodingFormat fmt, string name) =
                await EncodedValueIO.LoadAsync(this).ConfigureAwait(true);
            m_lifetime.Token.ThrowIfCancellationRequested();
            if (bytes.Length == 0)
            {
                return;
            }
            Variant v = UaLens.Connection.DataValueCodec.DecodeVariant(
                bytes, fmt, m_session.MessageContext);
            row.ValueText = FormatVariant(v);
            row.CachedVariant = v;
            statusLbl.Text = $"Loaded {row.Header} from {name} ({fmt}).";
            statusLbl.Foreground = (Application.Current?.FindResource("AccentGreen") as IBrush)
                ?? Brushes.Transparent;
        }
        catch (Exception ex)
        {
            statusLbl.Text = $"Import failed: {ex.Message}";
            statusLbl.Foreground = (Application.Current?.FindResource("AccentRedLight") as IBrush)
                ?? Brushes.Transparent;
        }
    }

    private async Task OnEditComplexArgAsync(MethodArgRow row)
    {
        var statusLbl = this.RequiredControl<TextBlock>("ResultStatus");
        if (row.Definition is null)
        {
            return;
        }
        var dlg = new ComplexValueElementDialog(
            row.Argument.DataType, row.Definition, m_values, row.CachedVariant);
        m_nestedDialog = dlg;
        try
        {
            await dlg.ShowDialog(this).ConfigureAwait(true);
        }
        finally
        {
            await dlg.StopAsync().ConfigureAwait(true);
            m_nestedDialog = null;
        }
        if (dlg.WasCommitted)
        {
            row.ValueText = FormatVariant(dlg.Result);
            row.CachedVariant = dlg.Result;
            statusLbl.Text = $"Edited {row.Header} (complex value).";
            statusLbl.Foreground = (Application.Current?.FindResource("TextPrimary") as IBrush)
                ?? Brushes.Transparent;
        }
    }

    private static string FormatDefault(Argument a)
    {
        // Argument doesn't expose a DefaultValue for input; leave empty.
        // Caller customises via the textbox.
        return string.Empty;
    }

    private string FormatVariant(Variant value)
    {
        return StructuredScalarValue.FormatValue(value, m_session.MessageContext);
    }

    private Task StartAuxiliaryAsync(Func<Task> operation)
    {
        if (!m_auxiliaryTask.IsCompleted || m_lifetime.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }
        m_auxiliaryTask = operation();
        return m_auxiliaryTask;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly IStructuredValueService m_values;
    private readonly SessionStructuredValueService? m_ownedValues;
    private readonly CancellationTokenSource m_lifetime;
    private Task m_loadTask = Task.CompletedTask;
    private Task m_callTask = Task.CompletedTask;
    private Task m_auxiliaryTask = Task.CompletedTask;
    private Task? m_shutdown;
    private Window? m_nestedDialog;
}

internal sealed partial class MethodArgRow : ObservableObject
{
    public string Header { get; }

    public Argument Argument { get; }

    [ObservableProperty]
    private string m_valueText;

    [ObservableProperty]
    private bool m_isComplex;

    public DataTypeDefinition? Definition { get; set; }

    /// <summary>
    /// Cached structured-value / imported variant.  When non-null and
    /// non-<see cref="Variant.Null"/> the dialog uses this directly
    /// instead of re-parsing the text — preserves complex bodies that
    /// can't be round-tripped through <see cref="VariantParser"/>.
    /// </summary>
    public Variant CachedVariant { get; set; } = Variant.Null;

    /// <summary>
    /// Per-row Import command, wired after construction by the dialog
    /// code-behind so the DataTemplate can bind a small file-icon
    /// button to it.  Made settable so the row stays plain-data here.
    /// </summary>
    public System.Windows.Input.ICommand? ImportCommand { get; set; }

    /// <summary>
    /// Per-row Edit-complex command, wired after construction by the
    /// dialog code-behind.  Bound to the puzzle-piece button; visible
    /// only when <see cref="IsComplex"/> is true.
    /// </summary>
    public System.Windows.Input.ICommand? EditComplexCommand { get; set; }

    public MethodArgRow(Argument a, string defaultValue)
    {
        Argument = a;
        Header = $"{a.Name} : {a.DataType} (rank={a.ValueRank})";
        m_valueText = defaultValue;
    }

    partial void OnValueTextChanged(string value)
    {
        CachedVariant = Variant.Null;
    }
}

/// <summary>
/// Single row of the Method Call dialog's output table.
/// </summary>
internal sealed record MethodOutputRow(int Index, string DataType, string Value);
