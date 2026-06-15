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
internal sealed partial class MethodCallDialog : Window
{
    private readonly NodeViewModel m_method;
    private readonly ManagedSession m_session;
    private NodeId m_objectId = NodeId.Null;
    private Argument[] m_arguments = Array.Empty<Argument>();
    public ObservableCollection<MethodArgRow> Inputs { get; } = new();
    public ObservableCollection<MethodOutputRow> Outputs { get; } = new();

    public MethodCallDialog(NodeViewModel method, ManagedSession session)
    {
        m_method = method;
        m_session = session;
        InitializeComponent();

        this.RequiredControl<TextBlock>("MethodLabel").Text = $"Method  {m_method.NodeId}";
        this.RequiredControl<TextBlock>("ParentLabel").Text = "Parent  (resolving…)";
        this.RequiredControl<ItemsControl>("InputsList").ItemsSource = Inputs;
        this.RequiredControl<ItemsControl>("OutputsList").ItemsSource = Outputs;

        this.RequiredControl<Button>("OkButton").Click += async (_, _) => await OnCall().ConfigureAwait(true);
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close();

        Opened += async (_, _) => await LoadArgumentsAsync().ConfigureAwait(true);
    }

    private async Task LoadArgumentsAsync()
    {
        var parentLbl = this.RequiredControl<TextBlock>("ParentLabel");
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
                CancellationToken.None).ConfigureAwait(true);

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
                m_arguments = Array.Empty<Argument>();
                Inputs.Clear();
                return;
            }

            // Step 3: read the InputArguments property value (Argument[]).
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = inputArgsId, AttributeId = Attributes.Value }
            ];
            ReadResponse rr = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                ids, CancellationToken.None).ConfigureAwait(true);
            if (rr.Results.Count == 0 || StatusCode.IsBad(rr.Results[0].StatusCode))
            {
                return;
            }
            object? boxed = rr.Results[0].WrappedValue.AsBoxedObject();
            if (boxed is ExtensionObject[] eos)
            {
                var args = new List<Argument>(eos.Length);
                foreach (ExtensionObject eo in eos)
                {
                    if (eo.TryGetValue<Argument>(out Argument? a) && a is not null)
                    {
                        args.Add(a);
                    }
                }
                m_arguments = args.ToArray();
            }
            else if (boxed is Argument[] a2)
            {
                m_arguments = a2;
            }
            else
            {
                m_arguments = Array.Empty<Argument>();
            }

            Inputs.Clear();
            foreach (Argument a in m_arguments)
            {
                var row = new MethodArgRow(a, FormatDefault(a));
                row.ImportCommand = new AsyncRelayCommand(() => OnImportArgAsync(row));
                row.EditComplexCommand = new AsyncRelayCommand(() => OnEditComplexArgAsync(row));
                Inputs.Add(row);
            }

            // After the rows are built, probe each argument's DataType for
            // a StructureDefinition / EnumDefinition.  Rows whose type
            // resolves to a complex shape get an "Edit struct…" affordance
            // alongside their primitive TextBox; the textbox stays so
            // users can still hand-paste a JSON/XML payload.
            await ProbeComplexTypesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            this.RequiredControl<TextBlock>("ResultStatus").Text = $"Failed to load arguments: {ex.Message}";
        }
    }

    private async Task ProbeComplexTypesAsync()
    {
        for (int i = 0; i < m_arguments.Length && i < Inputs.Count; i++)
        {
            Argument a = m_arguments[i];
            if (a.ValueRank != ValueRanks.Scalar
                && a.ValueRank != ValueRanks.ScalarOrOneDimension
                && a.ValueRank != ValueRanks.Any)
            {
                continue;
            }
            DataTypeDefinition? def = await ComplexValueIO
                .GetDataTypeDefinitionAsync(a.DataType, m_session, CancellationToken.None)
                .ConfigureAwait(true);
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
                CancellationToken.None).ConfigureAwait(true);
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

    private async Task OnCall()
    {
        var statusLbl = this.RequiredControl<TextBlock>("ResultStatus");
        statusLbl.Foreground = (Application.Current?.FindResource("TextPrimary") as IBrush)
            ?? Brushes.Transparent;
        Outputs.Clear();

        if (m_objectId.IsNull)
        {
            statusLbl.Text = "Cannot call — parent ObjectId could not be resolved.";
            return;
        }

        // Parse every input argument; abort on first error.
        var parsed = new List<Variant>(m_arguments.Length);
        for (int i = 0; i < m_arguments.Length; i++)
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
            CallResponse resp = await m_session.CallAsync(null, calls, CancellationToken.None).ConfigureAwait(true);
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
            row.Argument.DataType, row.Definition, m_session, row.CachedVariant);
        Variant? edited = await dlg.ShowDialog<Variant?>(this).ConfigureAwait(true);
        if (edited.HasValue)
        {
            row.CachedVariant = edited.Value;
            row.ValueText = FormatVariant(edited.Value);
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
        var sb = new StringBuilder();
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
}

/// <summary>
/// Single row of the Method Call dialog's output table.
/// </summary>
internal sealed record MethodOutputRow(int Index, string DataType, string Value);
