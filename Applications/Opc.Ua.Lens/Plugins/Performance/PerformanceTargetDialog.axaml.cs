/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Modal dialog that lets the user finalise a <see cref="BenchmarkTarget"/>
/// based on the address-space selection in the main window:
/// <list type="bullet">
/// <item><c>Write</c> mode requires a Variable selection and resolves
///   its <see cref="Attributes.DataType"/> + <see cref="Attributes.ValueRank"/>
///   so the runner knows what synthetic values to generate.</item>
/// <item><c>Call</c> mode requires a Method selection, walks
///   <c>HasComponent</c> upward to find the parent <c>ObjectId</c>,
///   then reads <c>HasProperty(InputArguments)</c> so the runner knows
///   the argument signature and can synthesise per-op input lists.</item>
/// </list>
/// Returns the configured <see cref="BenchmarkTarget"/> via
/// <see cref="Result"/>; <see cref="Closed"/> with null = user cancel.
/// </summary>
internal sealed partial class PerformanceTargetDialog : Window
{
    private readonly MainViewModel m_main;
    private readonly ISession m_session;
    private readonly NodeViewModel? m_hint;
    private BenchmarkMode m_mode = BenchmarkMode.Write;
    private NodeId m_resolvedObjectId = NodeId.Null;
    private BuiltInType m_resolvedBuiltIn = BuiltInType.Int32;
    private int m_resolvedValueRank = ValueRanks.Scalar;
    private Argument[] m_resolvedArguments = Array.Empty<Argument>();
    private NodeViewModel? m_selected;

    /// <summary>The configured target on OK; null on cancel.</summary>
    public BenchmarkTarget? Result { get; private set; }

    public PerformanceTargetDialog(MainViewModel main, ISession session)
        : this(main, session, hint: null)
    {
    }

    /// <summary>
    /// Constructor overload used when the caller already obtained a
    /// node from <see cref="BrowsePickerDialog"/> rather than the main
    /// address-space selection.  The dialog uses <paramref name="hint"/>
    /// in lieu of <see cref="MainViewModel.SelectedNode"/>.
    /// </summary>
    public PerformanceTargetDialog(MainViewModel main, ISession session, NodeViewModel? hint)
    {
        m_main = main ?? throw new ArgumentNullException(nameof(main));
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_hint = hint;
        InitializeComponent();

        this.RequiredControl<RadioButton>("WriteRadio").IsCheckedChanged += (_, _) => SwitchMode(BenchmarkMode.Write);
        this.RequiredControl<RadioButton>("CallRadio").IsCheckedChanged += (_, _) => SwitchMode(BenchmarkMode.Call);

        this.RequiredControl<Button>("OkButton").Click += (_, _) => OnOk();
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);

        Opened += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SwitchMode(BenchmarkMode mode)
    {
        if (m_mode == mode)
        {
            return;
        }

        m_mode = mode;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        // Prefer an explicit hint passed in by the caller (e.g. from the
        // BrowsePickerDialog fallback when nothing is selected in the
        // main address-space tree).  Fall back to MainViewModel.SelectedNode.
        m_selected = m_hint ?? m_main.SelectedNode;
        var sel = this.RequiredControl<TextBlock>("SelectionLabel");
        var details = this.RequiredControl<TextBlock>("DetailsLabel");
        var status = this.RequiredControl<TextBlock>("StatusLabel");
        var panel = this.RequiredControl<StackPanel>("SignaturePanel");
        panel.Children.Clear();

        if (m_selected is null)
        {
            sel.Text = "(no node selected)";
            details.Text = "—";
            status.Text = "Select a node in the main address-space tree, then re-open this dialog.";
            status.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            return;
        }

        sel.Text = $"{m_selected.Text}  ·  {m_selected.NodeId}";
        status.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

        if (m_mode == BenchmarkMode.Write)
        {
            if (m_selected.NodeClass != NodeClass.Variable)
            {
                details.Text = "Write mode requires a Variable selection.";
                status.Text = "Pick a Variable in the address-space tree, then re-open this dialog.";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                return;
            }
            await LoadVariableAsync(m_selected, details, status).ConfigureAwait(true);
        }
        else
        {
            if (m_selected.NodeClass != NodeClass.Method)
            {
                details.Text = "Call mode requires a Method selection.";
                status.Text = "Pick a Method in the address-space tree, then re-open this dialog.";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                return;
            }
            await LoadMethodAsync(m_selected, details, status, panel).ConfigureAwait(true);
        }
    }

    private async Task LoadVariableAsync(NodeViewModel selected, TextBlock details, TextBlock status)
    {
        details.Text = "(reading DataType + ValueRank…)";
        try
        {
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = selected.NodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = selected.NodeId, AttributeId = Attributes.ValueRank },
                new ReadValueId { NodeId = selected.NodeId, AttributeId = Attributes.AccessLevel }
            ];
            ReadResponse resp = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither, ids,
                CancellationToken.None).ConfigureAwait(true);
            NodeId dt = NodeId.Null;
            int rank = ValueRanks.Scalar;
            byte access = 0;
            if (resp.Results.Count >= 3)
            {
                if (!StatusCode.IsBad(resp.Results[0].StatusCode))
                {
                    resp.Results[0].WrappedValue.TryGetValue(out dt);
                }
                if (!StatusCode.IsBad(resp.Results[1].StatusCode))
                {
                    resp.Results[1].WrappedValue.TryGetValue(out rank);
                }
                if (!StatusCode.IsBad(resp.Results[2].StatusCode))
                {
                    resp.Results[2].WrappedValue.TryGetValue(out access);
                }
            }
            m_resolvedBuiltIn = BuiltInForDataType(dt);
            m_resolvedValueRank = rank;
            bool writable = (access & AccessLevels.CurrentWrite) != 0;
            details.Text = $"DataType: {dt}  ({m_resolvedBuiltIn})\nValueRank: {rank}\nAccessLevel: 0x{access:X2}";
            if (!writable)
            {
                status.Text = "Variable is not writable (AccessLevel does not have CurrentWrite). The benchmark will report write errors.";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            }
            else
            {
                status.Text = "Ready. Click OK to use this variable as the Write target.";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            }
        }
        catch (Exception ex)
        {
            details.Text = "(read failed)";
            status.Text = $"Read failed: {ex.Message}";
            status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private async Task LoadMethodAsync(NodeViewModel selected, TextBlock details, TextBlock status, StackPanel panel)
    {
        details.Text = "(resolving parent + InputArguments…)";
        try
        {
            // 1. parent ObjectId — prefer cached, else inverse HasComponent browse.
            m_resolvedObjectId = !selected.ParentNodeId.IsNull
                ? selected.ParentNodeId
                : await ResolveParentAsync(selected).ConfigureAwait(true);

            // 2. browse for HasProperty(InputArguments).
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = selected.NodeId,
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

            m_resolvedArguments = Array.Empty<Argument>();
            if (!inputArgsId.IsNull)
            {
                ArrayOf<ReadValueId> ids =
                [
                    new ReadValueId { NodeId = inputArgsId, AttributeId = Attributes.Value }
                ];
                ReadResponse rr = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither, ids,
                    CancellationToken.None).ConfigureAwait(true);
                if (rr.Results.Count > 0 && !StatusCode.IsBad(rr.Results[0].StatusCode))
                {
                    object? boxed = rr.Results[0].WrappedValue.AsBoxedObject();
                    if (boxed is ExtensionObject[] eos)
                    {
                        var list = new List<Argument>(eos.Length);
                        foreach (ExtensionObject eo in eos)
                        {
                            if (eo.TryGetValue<Argument>(out Argument? a) && a is not null)
                            {
                                list.Add(a);
                            }
                        }
                        m_resolvedArguments = list.ToArray();
                    }
                    else if (boxed is Argument[] arr)
                    {
                        m_resolvedArguments = arr;
                    }
                }
            }

            details.Text = m_resolvedObjectId.IsNull
                ? "Parent ObjectId could not be resolved — cannot Call this method."
                : $"Parent ObjectId: {m_resolvedObjectId}";

            if (m_resolvedArguments.Length == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "(method has no input arguments)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                    FontSize = 12
                });
            }
            else
            {
                var header = new TextBlock
                {
                    Text = "InputArguments (synthesised per op):",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                    FontSize = 12,
                    Margin = new Avalonia.Thickness(0, 0, 0, 6)
                };
                panel.Children.Add(header);
                int i = 0;
                foreach (Argument a in m_resolvedArguments)
                {
                    BuiltInType bi = ValueFactory.BuiltInForArgument(a);
                    string text = string.Format(CultureInfo.InvariantCulture,
                        "  [{0}] {1}  : {2}  (rank={3}) — synth as {4}",
                        i++, a.Name ?? "(unnamed)", a.DataType, a.ValueRank, bi);
                    panel.Children.Add(new TextBlock
                    {
                        Text = text,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                        FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Left
                    });
                }
            }

            if (m_resolvedObjectId.IsNull)
            {
                status.Text = "Cannot resolve parent ObjectId — pick a method whose parent has been expanded in the tree.";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            }
            else
            {
                status.Text = "Ready. Click OK to use this method as the Call target.";
                status.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            }
        }
        catch (Exception ex)
        {
            details.Text = "(resolve failed)";
            status.Text = $"Resolve failed: {ex.Message}";
            status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private async Task<NodeId> ResolveParentAsync(NodeViewModel selected)
    {
        try
        {
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = selected.NodeId,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br = await m_session.BrowseAsync(null, null, 0, browse,
                CancellationToken.None).ConfigureAwait(true);
            if (br.Results.Count > 0
                && !StatusCode.IsBad(br.Results[0].StatusCode)
                && br.Results[0].References.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(br.Results[0].References[0].NodeId, m_session.NamespaceUris);
            }
        }
        catch
        {
            // fall through — return Null below
        }
        return NodeId.Null;
    }

    private void OnOk()
    {
        if (m_selected is null)
        { Close(null); return; }
        if (m_mode == BenchmarkMode.Write)
        {
            if (m_selected.NodeClass != NodeClass.Variable)
            { Close(null); return; }
            Result = new BenchmarkTarget(
                BenchmarkMode.Write,
                m_selected.NodeId,
                ObjectId: null,
                m_resolvedBuiltIn,
                m_resolvedValueRank,
                InputArguments: null,
                DisplayName: BuildDisplayName());
        }
        else
        {
            if (m_selected.NodeClass != NodeClass.Method || m_resolvedObjectId.IsNull)
            {
                Close(null);
                return;
            }
            Result = new BenchmarkTarget(
                BenchmarkMode.Call,
                m_selected.NodeId,
                m_resolvedObjectId,
                BuiltInType.Variant,
                ValueRanks.Scalar,
                m_resolvedArguments,
                DisplayName: BuildDisplayName());
        }
        Close(Result);
    }

    private string BuildDisplayName()
    {
        if (m_selected is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append(m_mode == BenchmarkMode.Write ? "write " : "call ");
        sb.Append(m_selected.NodeId.ToString());
        return sb.ToString();
    }

    private static BuiltInType BuiltInForDataType(NodeId dataType)
    {
        if (dataType.IsNull)
        {
            return BuiltInType.Int32;
        }

        if (dataType.IdType != IdType.Numeric)
        {
            return BuiltInType.Int32;
        }

        if (dataType.NamespaceIndex != 0)
        {
            return BuiltInType.Int32;
        }

        uint id = (uint)dataType.Identifier;
        BuiltInType bi = (BuiltInType)id;
        return Enum.IsDefined(bi) ? bi : BuiltInType.Int32;
    }
}
