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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using UaLens.Connection;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Read-only inspector dialog that dumps the recursive state of a single
/// address-space node — attributes, references grouped by
/// <c>ReferenceTypeId</c>, and (for Variables) the most recently read
/// <see cref="DataValue"/>.  Mirrors
/// <c>UA-.NETStandard-Samples/Samples/ClientControls.Net4/Common/ViewNodeStateDlg.cs</c>
/// adapted for the UaLens Avalonia client.
/// </summary>
/// <remarks>
/// <para>
/// The attribute super-set is read in one batched
/// <c>ISession.ReadAsync</c> call (one round-trip per node).  Attributes
/// the server reports as <see cref="StatusCodes.BadAttributeIdInvalid"/>
/// (or any other bad status) are rendered inline next to the attribute
/// name and never throw.
/// </para>
/// <para>
/// References are loaded lazily the first time the user expands the
/// "References" branch and are grouped locally by <c>ReferenceTypeId</c>
/// after a single <c>BrowseAsync</c> against
/// <see cref="ReferenceTypeIds.References"/> with <c>IncludeSubtypes=true</c>
/// (continuations are followed via <c>BrowseNextAsync</c>).
/// </para>
/// </remarks>
internal sealed partial class ViewNodeStateDialog : Window
{
    private readonly ConnectionService m_connection;
    private readonly NodeId m_nodeId;
    private readonly ObservableCollection<NodeStateItem> m_roots = new();
    private bool m_closed;

    /// <summary>
    /// Full set of attributes that <see cref="ViewNodeStateDialog"/>
    /// will attempt to read in one batched <c>ReadAsync</c>.  Anything
    /// that comes back <see cref="StatusCodes.BadAttributeIdInvalid"/>
    /// is filtered out of the display per the resolved
    /// <see cref="NodeClass"/> in <see cref="RelevantAttributes"/>.
    /// </summary>
    private static readonly (uint Id, string Name)[] s_allAttrs =
    [
        (Attributes.NodeClass, "NodeClass"),
        (Attributes.BrowseName, "BrowseName"),
        (Attributes.DisplayName, "DisplayName"),
        (Attributes.Description, "Description"),
        (Attributes.WriteMask, "WriteMask"),
        (Attributes.UserWriteMask, "UserWriteMask"),
        (Attributes.Value, "Value"),
        (Attributes.DataType, "DataType"),
        (Attributes.ValueRank, "ValueRank"),
        (Attributes.ArrayDimensions, "ArrayDimensions"),
        (Attributes.AccessLevel, "AccessLevel"),
        (Attributes.UserAccessLevel, "UserAccessLevel"),
        (Attributes.MinimumSamplingInterval, "MinimumSamplingInterval"),
        (Attributes.Historizing, "Historizing"),
        (Attributes.EventNotifier, "EventNotifier"),
        (Attributes.IsAbstract, "IsAbstract"),
        (Attributes.Symmetric, "Symmetric"),
        (Attributes.InverseName, "InverseName"),
        (Attributes.DataTypeDefinition, "DataTypeDefinition"),
    ];

    public ViewNodeStateDialog(BrowserViewModel browser, ConnectionService connection, NodeId? nodeId)
    {
        ArgumentNullException.ThrowIfNull(browser);
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        m_nodeId = nodeId ?? NodeId.Null;
        InitializeComponent();

        var tree = this.RequiredControl<TreeView>("StateTree");
        var header = this.RequiredControl<TextBlock>("HeaderBlock");
        var copy = this.RequiredControl<Button>("CopyButton");
        var close = this.RequiredControl<Button>("CloseButton");

        tree.ItemsSource = m_roots;
        header.Text = $"NodeId: {(m_nodeId.IsNull ? "(none)" : m_nodeId.ToString())}";

        close.Click += (_, _) => Close();
        copy.Click += async (_, _) =>
        {
            string text = DumpAsText(m_roots);
            IClipboard? clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null)
            {
                await clip.SetTextAsync(text).ConfigureAwait(true);
            }
        };

        Closed += (_, _) => m_closed = true;

        // Kick off the attribute read as soon as the dialog opens.  No
        // need to await — the dialog model is observable and renders
        // incrementally.  CancellationToken.None mirrors
        // FindNodeDialog's pattern; the m_closed flag guards against
        // updating UI state after the dialog has been closed.
        _ = LoadOnOpenAsync();
    }

    /// <summary>
    /// Single batched <c>ReadAsync</c> for every attribute in
    /// <see cref="s_allAttrs"/>.  Builds the root <see cref="NodeStateItem"/>
    /// with the "Attributes", "References" (lazy), and (for Variables) the
    /// "Value" sub-branches.  Never throws — read failures are rendered
    /// inline against the offending attribute.
    /// </summary>
    private async Task LoadOnOpenAsync()
    {
        if (m_connection.Session is not { } session || m_nodeId.IsNull)
        {
            m_roots.Add(new NodeStateItem("(disconnected or null node)"));
            return;
        }

        DataValue[] results = Array.Empty<DataValue>();
        try
        {
            var ids = new ArrayOf<ReadValueId>(s_allAttrs
                .Select(a => new ReadValueId { NodeId = m_nodeId, AttributeId = a.Id })
                .ToArray());
            ReadResponse resp = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, ids, CancellationToken.None)
                .ConfigureAwait(true);
            results = resp.Results.ToArray() ?? Array.Empty<DataValue>();
        }
        catch (Exception ex)
        {
            if (!m_closed)
            {
                m_roots.Add(new NodeStateItem($"(read failed: {ex.Message})"));
            }
            return;
        }
        if (m_closed)
        {
            return;
        }

        NodeClass nc = NodeClass.Unspecified;
        int ncIdx = IndexOf(Attributes.NodeClass);
        if (ncIdx >= 0
            && results.Length > ncIdx
            && !StatusCode.IsBad(results[ncIdx].StatusCode)
            && results[ncIdx].WrappedValue.TryGetValue(out int ncv))
        {
            nc = (NodeClass)ncv;
        }

        string displayName = "";
        int dnIdx = IndexOf(Attributes.DisplayName);
        if (dnIdx >= 0
            && results.Length > dnIdx
            && !StatusCode.IsBad(results[dnIdx].StatusCode)
            && results[dnIdx].WrappedValue.TryGetValue(out LocalizedText dn)
            && !dn.IsNull)
        {
            displayName = dn.Text ?? "";
        }

        string rootHeader = displayName.Length > 0
            ? $"{Glyph(nc)} {displayName}  [{m_nodeId}]  ({nc})"
            : $"{Glyph(nc)} {m_nodeId}  ({nc})";
        var root = new NodeStateItem(rootHeader);

        // ── Attributes ──────────────────────────────────────────────
        var attrs = new NodeStateItem("Attributes");
        List<(uint Id, string Name)> relevant = RelevantAttributes(nc);
        foreach ((uint Id, string Name) e in relevant)
        {
            int idx = IndexOf(e.Id);
            if (idx < 0 || idx >= results.Length)
            {
                continue;
            }
            DataValue dv = results[idx];
            if (StatusCode.IsBad(dv.StatusCode))
            {
                attrs.Children.Add(new NodeStateItem($"{e.Name}  ({StatusCodeName(dv.StatusCode)})"));
            }
            else
            {
                attrs.Children.Add(new NodeStateItem($"{e.Name} = {FormatAttribute(e.Id, dv.WrappedValue)}"));
            }
        }
        root.Children.Add(attrs);

        // ── References (lazy) ───────────────────────────────────────
        var references = new NodeStateItem("References", LoadReferencesAsync);
        root.Children.Add(references);

        // ── Value (Variables / VariableTypes only) ──────────────────
        if (nc == NodeClass.Variable || nc == NodeClass.VariableType)
        {
            int vIdx = IndexOf(Attributes.Value);
            if (vIdx >= 0 && vIdx < results.Length)
            {
                var valueItem = new NodeStateItem("Value");
                DataValue valDv = results[vIdx];
                if (StatusCode.IsBad(valDv.StatusCode))
                {
                    valueItem.Children.Add(new NodeStateItem($"(read failed: {StatusCodeName(valDv.StatusCode)})"));
                }
                else
                {
                    valueItem.Children.Add(new NodeStateItem($"WrappedValue = {valDv.WrappedValue.ToString()}"));
                    if (valDv.SourceTimestamp != default)
                    {
                        valueItem.Children.Add(new NodeStateItem(
                            $"SourceTimestamp = {valDv.SourceTimestamp.ToString("o", CultureInfo.InvariantCulture)}"));
                    }
                    if (valDv.ServerTimestamp != default)
                    {
                        valueItem.Children.Add(new NodeStateItem(
                            $"ServerTimestamp = {valDv.ServerTimestamp.ToString("o", CultureInfo.InvariantCulture)}"));
                    }
                    valueItem.Children.Add(new NodeStateItem($"StatusCode = {StatusCodeName(valDv.StatusCode)}"));
                }
                root.Children.Add(valueItem);
            }
        }

        m_roots.Add(root);
        root.IsExpanded = true;
        attrs.IsExpanded = true;
    }

    /// <summary>
    /// Lazy loader wired to the "References" branch.  Issues a single
    /// <c>BrowseAsync</c> against <see cref="ReferenceTypeIds.References"/>
    /// with <c>IncludeSubtypes=true</c>, follows any continuation points,
    /// then groups the result locally by <see cref="ReferenceDescription.ReferenceTypeId"/>.
    /// Each group's children carry the form
    /// <c>{ReferenceType} → {TargetNodeId} ({DisplayName})</c>.
    /// </summary>
    private async Task LoadReferencesAsync(NodeStateItem item, CancellationToken ct)
    {
        item.Children.Clear();

        if (m_connection.Session is not { } session)
        {
            item.Children.Add(new NodeStateItem("(disconnected)"));
            return;
        }

        var refs = new List<ReferenceDescription>();
        try
        {
            ArrayOf<BrowseDescription> descriptions = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = m_nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.References,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse resp = await session
                .BrowseAsync(null, null, 0, descriptions, ct)
                .ConfigureAwait(true);
            ByteString cp = ByteString.Empty;
            if (resp.Results.Count > 0 && !StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                refs.AddRange(resp.Results[0].References);
                cp = resp.Results[0].ContinuationPoint;
            }
            while (cp.Length > 0)
            {
                ArrayOf<ByteString> cps = new ByteString[] { cp };
                BrowseNextResponse next = await session
                    .BrowseNextAsync(null, false, cps, ct)
                    .ConfigureAwait(true);
                cp = ByteString.Empty;
                if (next.Results.Count > 0 && !StatusCode.IsBad(next.Results[0].StatusCode))
                {
                    refs.AddRange(next.Results[0].References);
                    cp = next.Results[0].ContinuationPoint;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            item.Children.Add(new NodeStateItem($"(browse failed: {ex.Message})"));
            return;
        }

        if (refs.Count == 0)
        {
            item.Children.Add(new NodeStateItem("(no references)"));
            return;
        }

        // Resolve reference-type names via a single batched ReadAsync so
        // we don't pay a per-reference round-trip.
        var refTypeIds = new List<NodeId>();
        var seenIds = new HashSet<NodeId>();
        foreach (ReferenceDescription r in refs)
        {
            if (!r.ReferenceTypeId.IsNull && seenIds.Add(r.ReferenceTypeId))
            {
                refTypeIds.Add(r.ReferenceTypeId);
            }
        }
        var refTypeNames = new Dictionary<NodeId, string>();
        if (refTypeIds.Count > 0)
        {
            try
            {
                var nameIds = new ArrayOf<ReadValueId>(refTypeIds
                    .Select(rt => new ReadValueId { NodeId = rt, AttributeId = Attributes.BrowseName })
                    .ToArray());
                ReadResponse nameResp = await session
                    .ReadAsync(null, 0, TimestampsToReturn.Neither, nameIds, ct)
                    .ConfigureAwait(true);
                for (int i = 0; i < refTypeIds.Count; i++)
                {
                    if (i < nameResp.Results.Count
                        && !StatusCode.IsBad(nameResp.Results[i].StatusCode)
                        && nameResp.Results[i].WrappedValue.TryGetValue(out QualifiedName qn))
                    {
                        refTypeNames[refTypeIds[i]] = qn.Name ?? refTypeIds[i].ToString() ?? string.Empty;
                    }
                    else
                    {
                        refTypeNames[refTypeIds[i]] = refTypeIds[i].ToString() ?? string.Empty;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Fall back to raw NodeIds — name resolution is cosmetic.
            }
        }

        // Group locally by ReferenceTypeId and render one branch per
        // reference type, ordered alphabetically for stable output.
        var groups = refs
            .GroupBy(r => r.ReferenceTypeId)
            .OrderBy(g => refTypeNames.TryGetValue(g.Key, out string? n) ? n : g.Key.ToString(), StringComparer.Ordinal);
        foreach (IGrouping<NodeId, ReferenceDescription> group in groups)
        {
            string rtName = refTypeNames.TryGetValue(group.Key, out string? name)
                ? name
                : group.Key.ToString() ?? string.Empty;
            var branch = new NodeStateItem($"{rtName}  ({group.Count()})");
            foreach (ReferenceDescription r in group)
            {
                string targetDisplay = !r.DisplayName.IsNull
                    ? r.DisplayName.Text ?? string.Empty
                    : (!r.BrowseName.IsNull ? r.BrowseName.Name ?? string.Empty : string.Empty);
                string target = r.NodeId.ToString() ?? string.Empty;
                branch.Children.Add(new NodeStateItem(
                    $"{rtName} → {target}  ({targetDisplay})"));
            }
            item.Children.Add(branch);
        }
    }

    /// <summary>
    /// Returns the index in <see cref="s_allAttrs"/> of the given
    /// attribute id, or <c>-1</c> when the attribute isn't in the
    /// super-set (should never happen).
    /// </summary>
    private static int IndexOf(uint attributeId)
    {
        for (int i = 0; i < s_allAttrs.Length; i++)
        {
            if (s_allAttrs[i].Id == attributeId)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Returns the (common + class-specific) attributes that should be
    /// listed under "Attributes" for a node of the given class.  The
    /// list mirrors the task spec: common ones for every class, plus
    /// the class-specific subset for Variable / Object / View /
    /// ReferenceType / DataType.
    /// </summary>
    private static List<(uint Id, string Name)> RelevantAttributes(NodeClass nc)
    {
        var list = new List<(uint, string)>
        {
            (Attributes.NodeClass, "NodeClass"),
            (Attributes.BrowseName, "BrowseName"),
            (Attributes.DisplayName, "DisplayName"),
            (Attributes.Description, "Description"),
            (Attributes.WriteMask, "WriteMask"),
            (Attributes.UserWriteMask, "UserWriteMask"),
        };
        switch (nc)
        {
            case NodeClass.Variable:
            case NodeClass.VariableType:
                list.Add((Attributes.Value, "Value"));
                list.Add((Attributes.DataType, "DataType"));
                list.Add((Attributes.ValueRank, "ValueRank"));
                list.Add((Attributes.ArrayDimensions, "ArrayDimensions"));
                list.Add((Attributes.AccessLevel, "AccessLevel"));
                list.Add((Attributes.UserAccessLevel, "UserAccessLevel"));
                list.Add((Attributes.MinimumSamplingInterval, "MinimumSamplingInterval"));
                list.Add((Attributes.Historizing, "Historizing"));
                break;
            case NodeClass.Object:
            case NodeClass.View:
                list.Add((Attributes.EventNotifier, "EventNotifier"));
                break;
            case NodeClass.ReferenceType:
                list.Add((Attributes.IsAbstract, "IsAbstract"));
                list.Add((Attributes.Symmetric, "Symmetric"));
                list.Add((Attributes.InverseName, "InverseName"));
                break;
            case NodeClass.DataType:
                list.Add((Attributes.IsAbstract, "IsAbstract"));
                list.Add((Attributes.DataTypeDefinition, "DataTypeDefinition"));
                break;
            case NodeClass.ObjectType:
                list.Add((Attributes.IsAbstract, "IsAbstract"));
                break;
            default:
                // Unspecified: show every attribute that did read OK.
                foreach ((uint Id, string Name) entry in s_allAttrs)
                {
                    if (!list.Exists(x => x.Item1 == entry.Id))
                    {
                        list.Add((entry.Id, entry.Name));
                    }
                }
                break;
        }
        return list;
    }

    /// <summary>
    /// Pretty-print an attribute's <see cref="Variant"/> value.  Falls
    /// back to <c>Variant.ToString()</c> for unknown attribute ids — good
    /// enough for the dump dialog, no need to mimic the richer
    /// per-attribute formatting on the main attributes panel.
    /// </summary>
    private static string FormatAttribute(uint attributeId, Variant v)
    {
        if (v.IsNull)
        {
            return "(null)";
        }
        if (attributeId == Attributes.NodeClass && v.TryGetValue(out int ncv))
        {
            return ((NodeClass)ncv).ToString();
        }
        object? boxed = v.AsBoxedObject();
        return boxed switch
        {
            null => "(null)",
            string s => s,
            LocalizedText l => l.Text ?? string.Empty,
            QualifiedName q => q.ToString() ?? string.Empty,
            Array a => FormatArray(a),
            _ => boxed.ToString() ?? string.Empty
        };
    }

    private static string FormatArray(Array a)
    {
        const int kMax = 16;
        var sb = new StringBuilder();
        sb.Append('[');
        int n = Math.Min(a.Length, kMax);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(a.GetValue(i)?.ToString() ?? "null");
        }
        if (a.Length > kMax)
        {
            sb.Append(", …");
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Best-effort mapping of a numeric <see cref="StatusCode"/> to its
    /// symbolic name, falling back to the raw hex when the code isn't
    /// registered.
    /// </summary>
    private static string StatusCodeName(StatusCode sc)
    {
        string? sym = StatusCode.LookupSymbolicId(sc.Code);
        return string.IsNullOrEmpty(sym)
            ? sc.ToString()
            : sym;
    }

    private static string Glyph(NodeClass cls) => cls switch
    {
        NodeClass.Object => "\u25C9",
        NodeClass.ObjectType => "\u25C7",
        NodeClass.Variable => "\u25CB",
        NodeClass.VariableType => "\u25CE",
        NodeClass.Method => "\u25B6",
        NodeClass.ReferenceType => "\u25E6",
        NodeClass.DataType => "\u25A1",
        NodeClass.View => "\u25A3",
        _ => "?"
    };

    /// <summary>
    /// Renders the currently-loaded tree (root → branch → leaf) as an
    /// indent-formatted plain-text dump suitable for the system
    /// clipboard.  Branches that haven't been lazy-loaded yet are
    /// represented by their header alone — the user can expand them
    /// before clicking "Copy as text" to capture them.
    /// </summary>
    private static string DumpAsText(IEnumerable<NodeStateItem> roots)
    {
        var sb = new StringBuilder();
        foreach (NodeStateItem r in roots)
        {
            Walk(r, 0, sb);
        }
        return sb.ToString();
    }

    private static void Walk(NodeStateItem item, int depth, StringBuilder sb)
    {
        if (depth > 0)
        {
            sb.Append(' ', depth * 2);
        }
        sb.AppendLine(item.Header);
        foreach (NodeStateItem c in item.Children)
        {
            Walk(c, depth + 1, sb);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// One row in the recursive-state TreeView.  Holds the rendered header
/// text, an observable <see cref="IsExpanded"/> flag (TwoWay-bound to
/// the <c>TreeViewItem</c>), and an optional lazy loader fired the
/// first time the user expands the item.  Lazy items seed a
/// "(loading…)" placeholder child so Avalonia's TreeView surfaces the
/// expand chevron before the real children arrive.
/// </summary>
internal sealed partial class NodeStateItem : ObservableObject
{
    private readonly Func<NodeStateItem, CancellationToken, Task>? m_loader;
    private bool m_loaded;

    public string Header { get; }
    public ObservableCollection<NodeStateItem> Children { get; } = new();

    [ObservableProperty]
    private bool m_isExpanded;

    public NodeStateItem(string header)
    {
        Header = header;
    }

    public NodeStateItem(string header, Func<NodeStateItem, CancellationToken, Task> loader)
    {
        Header = header;
        m_loader = loader;
        // Placeholder so the TreeView shows an expand chevron until the
        // real children are loaded on first expand.
        Children.Add(new NodeStateItem("(expand to load…)"));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || m_loaded || m_loader is null)
        {
            return;
        }
        m_loaded = true;
        Func<NodeStateItem, CancellationToken, Task> loader = m_loader;
        _ = LoadAsync(loader);
    }

    private async Task LoadAsync(Func<NodeStateItem, CancellationToken, Task> loader)
    {
        try
        {
            await loader.Invoke(this, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Children.Clear();
            Children.Add(new NodeStateItem($"(load failed: {ex.Message})"));
        }
    }
}
