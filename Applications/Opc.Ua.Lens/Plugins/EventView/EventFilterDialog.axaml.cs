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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Views;

namespace UaLens.Plugins.EventView;

/// <summary>
/// Configuration produced by the <see cref="EventFilterDialog"/>: the
/// minimum severity that should pass the UI-side filter, the ordered
/// list of SelectClause field BrowseNames to include in every monitored
/// item's filter, the event-type NodeId those fields are rooted on
/// (used as <c>SimpleAttributeOperand.TypeDefinitionId</c>) and an
/// optional <see cref="Opc.Ua.ContentFilter"/> bound to the EventFilter's
/// <c>WhereClause</c>.
/// </summary>
internal sealed record EventFilterConfig(
    ushort SeverityThreshold,
    IReadOnlyList<string> Fields,
    Opc.Ua.NodeId? EventTypeNodeId = null,
    Opc.Ua.ContentFilter? WhereClause = null);

/// <summary>
/// Modal editor for the Event View's filter knobs: severity threshold
/// plus checkboxes for the chosen event type's fields.  The dialog
/// hosts its own "Pick type…" button — clicking it opens a
/// <see cref="BrowsePickerDialog"/> rooted at BaseEventType, and the
/// resulting type drives field discovery in-place (the dialog stays
/// open and rebuilds its checkbox list).
/// </summary>
internal sealed partial class EventFilterDialog : Window
{
    /// <summary>Default field list (the nine standard BaseEventType fields).</summary>
    public static readonly IReadOnlyList<string> DefaultFields =
    [
        "EventId",
        "EventType",
        "SourceName",
        "SourceNode",
        "Time",
        "ReceiveTime",
        "LocalTime",
        "Message",
        "Severity"
    ];

    /// <summary>Result populated when the user clicks Apply, else <c>null</c>.</summary>
    public EventFilterConfig? Result { get; private set; }

    private readonly ISession? m_session;
    private readonly Dictionary<string, CheckBox> m_checkboxes = new(StringComparer.Ordinal);
    private readonly TextBlock m_eventTypeLabel;
    private readonly StackPanel m_fieldsPanel;
    private readonly ContentFilterEditor m_whereClauseEditor;
    private NodeId m_eventTypeNodeId;
    private IReadOnlyList<string> m_fields;
    private HashSet<string> m_selectedSnapshot;

    public EventFilterDialog(EventFilterConfig current)
        : this(current, session: null)
    {
    }

    /// <summary>
    /// Overload used by the EventView "Filter…" flow.  A non-null
    /// <paramref name="session"/> enables the "Pick type…" button to
    /// browse subtypes of BaseEventType and discover the fields for
    /// the chosen type live.
    /// </summary>
    public EventFilterDialog(EventFilterConfig current, ISession? session)
    {
        InitializeComponent();
        m_session = session;
        m_eventTypeNodeId = current.EventTypeNodeId ?? ObjectTypeIds.BaseEventType;
        m_fields = DefaultFields;
        m_selectedSnapshot = new HashSet<string>(current.Fields, StringComparer.Ordinal);

        Slider severity = this.RequiredControl<Slider>("SeveritySlider");
        TextBlock severityValue = this.RequiredControl<TextBlock>("SeverityValue");
        m_eventTypeLabel = this.RequiredControl<TextBlock>("EventTypeLabel");
        Button pickType = this.RequiredControl<Button>("PickTypeButton");
        m_fieldsPanel = this.RequiredControl<StackPanel>("FieldsPanel");
        m_whereClauseEditor = this.RequiredControl<ContentFilterEditor>("WhereClauseEditor");
        m_whereClauseEditor.Initialize(current.WhereClause, session);
        Button ok = this.RequiredControl<Button>("OkButton");
        Button cancel = this.RequiredControl<Button>("CancelButton");

        severity.Value = current.SeverityThreshold;
        severityValue.Text = current.SeverityThreshold.ToString(CultureInfo.InvariantCulture);
        severity.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                severityValue.Text = ((int)severity.Value).ToString(CultureInfo.InvariantCulture);
            }
        };

        RefreshEventTypeLabel();
        pickType.IsEnabled = session is not null;
        pickType.Click += async (_, _) => await PickEventTypeAsync().ConfigureAwait(true);

        RebuildFieldList(DefaultFields);

        ok.Click += (_, _) =>
        {
            CaptureSelections();
            var fields = new List<string>(m_fields.Count);
            foreach (string name in m_fields)
            {
                if (m_checkboxes.TryGetValue(name, out CheckBox? cb) && cb.IsChecked == true)
                {
                    fields.Add(name);
                }
            }
            ushort threshold = (ushort)Math.Clamp((int)severity.Value, 0, 1000);
            ContentFilter whereClause = m_whereClauseEditor.BuildResult();
            Result = new EventFilterConfig(
                threshold,
                fields,
                m_eventTypeNodeId,
                whereClause.Elements.Count > 0 ? whereClause : null);
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private async Task PickEventTypeAsync()
    {
        if (m_session is null)
        {
            return;
        }
        CaptureSelections();
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: m_session,
            Root: ObjectTypeIds.BaseEventType,
            Title: "Pick event type",
            AcceptedClasses: NodeClass.ObjectType,
            ReferenceTypeId: ReferenceTypeIds.HasSubtype,
            Header: "Browse subtypes of BaseEventType.  Selecting one updates the field list below."));
        NodeId? picked = await picker.ShowDialog<NodeId?>(this).ConfigureAwait(true);
        if (!picked.HasValue || picked.Value.IsNull)
        {
            return;
        }
        m_eventTypeNodeId = picked.Value;
        RefreshEventTypeLabel();
        // Field discovery against the new type; fall back to defaults on error.
        IReadOnlyList<string> discovered;
        try
        {
            discovered = await DiscoverEventTypeFieldsAsync(
                m_session, m_eventTypeNodeId, CancellationToken.None).ConfigureAwait(true);
            if (discovered.Count == 0)
            {
                discovered = DefaultFields;
            }
        }
        catch
        {
            discovered = DefaultFields;
        }
        RebuildFieldList(discovered);
    }

    private void RefreshEventTypeLabel()
    {
        m_eventTypeLabel.Text = m_eventTypeNodeId.ToString();
    }

    private void CaptureSelections()
    {
        // Remember which fields are currently ticked so the rebuild
        // after a type-change can preserve user choices that still apply.
        m_selectedSnapshot = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, CheckBox cb) in m_checkboxes)
        {
            if (cb.IsChecked == true)
            {
                m_selectedSnapshot.Add(name);
            }
        }
    }

    private void RebuildFieldList(IReadOnlyList<string> fields)
    {
        m_fields = fields;
        m_checkboxes.Clear();
        m_fieldsPanel.Children.Clear();
        foreach (string field in fields)
        {
            var cb = new CheckBox
            {
                Content = field,
                IsChecked = m_selectedSnapshot.Contains(field),
                Margin = new Avalonia.Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            m_checkboxes[field] = cb;
            m_fieldsPanel.Children.Add(cb);
        }
    }

    /// <summary>
    /// Browses the chain of supertypes from <paramref name="typeId"/>
    /// up to <c>BaseEventType</c> and collects unique BrowseNames of
    /// every HasProperty/HasComponent Variable child along the way.
    /// Order: supertypes first.
    /// </summary>
    private static async Task<IReadOnlyList<string>> DiscoverEventTypeFieldsAsync(
        ISession session, NodeId typeId, CancellationToken ct)
    {
        var chain = new List<NodeId>();
        NodeId current = typeId;
        for (int hop = 0; hop < 32 && !current.IsNull; hop++)
        {
            chain.Add(current);
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = current,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.ObjectType,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br = await session.BrowseAsync(null, null, 0, browse, ct).ConfigureAwait(false);
            if (br.Results.Count == 0
                || StatusCode.IsBad(br.Results[0].StatusCode)
                || br.Results[0].References.Count == 0)
            {
                break;
            }
            NodeId parent = ExpandedNodeId.ToNodeId(
                br.Results[0].References[0].NodeId, session.NamespaceUris);
            if (parent.IsNull || chain.Contains(parent))
            {
                break;
            }
            current = parent;
        }
        chain.Reverse();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<string>();
        foreach (NodeId t in chain)
        {
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = t,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasChild,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Variable,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse br = await session.BrowseAsync(null, null, 0, browse, ct).ConfigureAwait(false);
            if (br.Results.Count == 0 || StatusCode.IsBad(br.Results[0].StatusCode))
            {
                continue;
            }
            var refs = new List<ReferenceDescription>();
            foreach (ReferenceDescription r in br.Results[0].References)
            {
                refs.Add(r);
            }
            foreach (ReferenceDescription r in refs)
            {
                string? name = r.BrowseName.Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (seen.Add(name))
                {
                    fields.Add(name);
                }
            }
        }
        return fields;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

