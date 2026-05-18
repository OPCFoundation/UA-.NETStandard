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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Distinguishes the four concrete <c>FilterOperand</c> subclasses that
/// the <see cref="ContentFilterEditor"/> can author.  Matches the OPC UA
/// 1.05 Part 4 spec list.
/// </summary>
internal enum FilterOperandKind
{
    Literal,
    Element,
    Attribute,
    SimpleAttribute,
}

/// <summary>
/// Composite modal editor for a single OPC UA <see cref="FilterOperand"/>.
/// The user picks an operand kind via the top combo box and the matching
/// sub-panel is shown; on OK the dialog returns the constructed operand
/// POCO (<see cref="LiteralOperand"/>, <see cref="ElementOperand"/>,
/// <see cref="AttributeOperand"/> or <see cref="SimpleAttributeOperand"/>).
///
/// <para>Edit-time validation is deliberately minimal — the only checks
/// performed are those needed to keep the operand round-trippable on the
/// wire.  Semantic errors (e.g. unknown TypeDefinition, out-of-range
/// element index, malformed RelativePath) are deferred to the server's
/// response when the resulting <c>EventFilter</c> is sent to
/// <c>CreateMonitoredItems</c>.</para>
/// </summary>
internal sealed partial class FilterOperandEditDialog : Window
{
    /// <summary>Listed in the literal-operand DataType combo.  Covers
    /// the common scalar built-ins; anything more exotic should be edited
    /// directly via a Variant editor outside this dialog.</summary>
    private static readonly (string Label, BuiltInType Type, NodeId DataTypeId)[] s_literalTypes =
    [
        ("Boolean",  BuiltInType.Boolean,    DataTypeIds.Boolean),
        ("SByte",    BuiltInType.SByte,      DataTypeIds.SByte),
        ("Byte",     BuiltInType.Byte,       DataTypeIds.Byte),
        ("Int16",    BuiltInType.Int16,      DataTypeIds.Int16),
        ("UInt16",   BuiltInType.UInt16,     DataTypeIds.UInt16),
        ("Int32",    BuiltInType.Int32,      DataTypeIds.Int32),
        ("UInt32",   BuiltInType.UInt32,     DataTypeIds.UInt32),
        ("Int64",    BuiltInType.Int64,      DataTypeIds.Int64),
        ("UInt64",   BuiltInType.UInt64,     DataTypeIds.UInt64),
        ("Float",    BuiltInType.Float,      DataTypeIds.Float),
        ("Double",   BuiltInType.Double,     DataTypeIds.Double),
        ("String",   BuiltInType.String,     DataTypeIds.String),
        ("DateTime", BuiltInType.DateTime,   DataTypeIds.DateTime),
        ("Guid",     BuiltInType.Guid,       DataTypeIds.Guid),
        ("NodeId",   BuiltInType.NodeId,     DataTypeIds.NodeId),
    ];

    /// <summary>Attribute id options shared by Attribute / SimpleAttribute panels.</summary>
    private static readonly (string Label, uint Id)[] s_attributeIds =
    [
        ("Value",          Attributes.Value),
        ("NodeId",         Attributes.NodeId),
        ("NodeClass",      Attributes.NodeClass),
        ("BrowseName",     Attributes.BrowseName),
        ("DisplayName",    Attributes.DisplayName),
        ("Description",    Attributes.Description),
        ("DataType",       Attributes.DataType),
        ("ValueRank",      Attributes.ValueRank),
        ("ArrayDimensions", Attributes.ArrayDimensions),
        ("AccessLevel",    Attributes.AccessLevel),
        ("EventNotifier",  Attributes.EventNotifier),
    ];

    public FilterOperand? Result { get; private set; }

    private readonly ISession? m_session;
    private readonly ComboBox m_kindCombo;
    private readonly Grid m_literalPanel;
    private readonly Grid m_elementPanel;
    private readonly Grid m_attributePanel;
    private readonly Grid m_simplePanel;
    private readonly TextBlock m_status;

    private readonly ComboBox m_literalType;
    private readonly TextBox m_literalValue;
    private readonly CheckBox m_literalIsArray;

    private readonly NumericUpDown m_elementIndex;

    private readonly TextBox m_attrNodeId;
    private readonly TextBox m_attrAlias;
    private readonly TextBox m_attrPath;
    private readonly ComboBox m_attrAttributeId;
    private readonly TextBox m_attrIndexRange;
    private readonly Button m_attrPickNode;

    private readonly TextBox m_simpleTypeId;
    private readonly TextBox m_simplePath;
    private readonly ComboBox m_simpleAttributeId;
    private readonly TextBox m_simpleIndexRange;
    private readonly Button m_simplePickType;

    public FilterOperandEditDialog(FilterOperand? initial, ISession? session)
    {
        InitializeComponent();
        m_session = session;

        m_kindCombo = this.RequiredControl<ComboBox>("KindCombo");
        m_literalPanel = this.RequiredControl<Grid>("LiteralPanel");
        m_elementPanel = this.RequiredControl<Grid>("ElementPanel");
        m_attributePanel = this.RequiredControl<Grid>("AttributePanel");
        m_simplePanel = this.RequiredControl<Grid>("SimplePanel");
        m_status = this.RequiredControl<TextBlock>("StatusLabel");

        m_literalType = this.RequiredControl<ComboBox>("LiteralDataType");
        m_literalValue = this.RequiredControl<TextBox>("LiteralValue");
        m_literalIsArray = this.RequiredControl<CheckBox>("LiteralIsArray");

        m_elementIndex = this.RequiredControl<NumericUpDown>("ElementIndex");

        m_attrNodeId = this.RequiredControl<TextBox>("AttrNodeId");
        m_attrAlias = this.RequiredControl<TextBox>("AttrAlias");
        m_attrPath = this.RequiredControl<TextBox>("AttrPath");
        m_attrAttributeId = this.RequiredControl<ComboBox>("AttrAttributeId");
        m_attrIndexRange = this.RequiredControl<TextBox>("AttrIndexRange");
        m_attrPickNode = this.RequiredControl<Button>("AttrPickNode");

        m_simpleTypeId = this.RequiredControl<TextBox>("SimpleTypeId");
        m_simplePath = this.RequiredControl<TextBox>("SimplePath");
        m_simpleAttributeId = this.RequiredControl<ComboBox>("SimpleAttributeId");
        m_simpleIndexRange = this.RequiredControl<TextBox>("SimpleIndexRange");
        m_simplePickType = this.RequiredControl<Button>("SimplePickType");

        m_kindCombo.ItemsSource = Enum.GetValues<FilterOperandKind>();
        m_literalType.ItemsSource = s_literalTypes.Select(t => t.Label).ToArray();
        m_attrAttributeId.ItemsSource = s_attributeIds.Select(a => a.Label).ToArray();
        m_simpleAttributeId.ItemsSource = s_attributeIds.Select(a => a.Label).ToArray();

        m_attrPickNode.IsEnabled = session is not null;
        m_simplePickType.IsEnabled = session is not null;
        m_attrPickNode.Click += async (_, _) => await PickAttributeNodeAsync().ConfigureAwait(true);
        m_simplePickType.Click += async (_, _) => await PickSimpleTypeAsync().ConfigureAwait(true);

        // Default-select first item in each combo before applying initial,
        // so the panels render even when 'initial' is null.
        m_kindCombo.SelectedIndex = 0;
        m_literalType.SelectedIndex = 11; // String
        m_attrAttributeId.SelectedIndex = 0;
        m_simpleAttributeId.SelectedIndex = 0;
        m_kindCombo.SelectionChanged += (_, _) => UpdatePanelVisibility();
        UpdatePanelVisibility();

        ApplyInitial(initial);

        this.RequiredControl<Button>("OkButton").Click += (_, _) => OnOk();
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    private void ApplyInitial(FilterOperand? initial)
    {
        switch (initial)
        {
            case LiteralOperand lit:
                m_kindCombo.SelectedItem = FilterOperandKind.Literal;
                ApplyLiteral(lit);
                break;
            case ElementOperand el:
                m_kindCombo.SelectedItem = FilterOperandKind.Element;
                m_elementIndex.Value = el.Index;
                break;
            case AttributeOperand ao:
                m_kindCombo.SelectedItem = FilterOperandKind.Attribute;
                m_attrNodeId.Text = ao.NodeId.IsNull ? string.Empty : ao.NodeId.ToString();
                m_attrAlias.Text = ao.Alias ?? string.Empty;
                m_attrPath.Text = ao.BrowsePath?.ToString() ?? string.Empty;
                SelectAttributeId(m_attrAttributeId, ao.AttributeId);
                m_attrIndexRange.Text = ao.IndexRange ?? string.Empty;
                break;
            case SimpleAttributeOperand sao:
                m_kindCombo.SelectedItem = FilterOperandKind.SimpleAttribute;
                m_simpleTypeId.Text = sao.TypeDefinitionId.IsNull ? string.Empty : sao.TypeDefinitionId.ToString();
                m_simplePath.Text = FormatBrowsePath(sao.BrowsePath);
                SelectAttributeId(m_simpleAttributeId, sao.AttributeId);
                m_simpleIndexRange.Text = sao.IndexRange ?? string.Empty;
                break;
            default:
                m_kindCombo.SelectedItem = FilterOperandKind.SimpleAttribute;
                break;
        }
        UpdatePanelVisibility();
    }

    private void ApplyLiteral(LiteralOperand lit)
    {
        Variant v = lit.Value;
        BuiltInType bi = v.TypeInfo.BuiltInType;
        if (bi == BuiltInType.Null)
        {
            bi = BuiltInType.String;
        }
        int idx = Array.FindIndex(s_literalTypes, t => t.Type == bi);
        if (idx < 0)
        {
            idx = 11; // String fallback
        }
        m_literalType.SelectedIndex = idx;
        m_literalIsArray.IsChecked = v.TypeInfo.ValueRank > ValueRanks.Scalar;
        m_literalValue.Text = FormatVariant(v);
    }

    private static void SelectAttributeId(ComboBox combo, uint attributeId)
    {
        int idx = Array.FindIndex(s_attributeIds, a => a.Id == attributeId);
        combo.SelectedIndex = idx < 0 ? 0 : idx;
    }

    private void UpdatePanelVisibility()
    {
        var kind = m_kindCombo.SelectedItem is FilterOperandKind k ? k : FilterOperandKind.SimpleAttribute;
        m_literalPanel.IsVisible = kind == FilterOperandKind.Literal;
        m_elementPanel.IsVisible = kind == FilterOperandKind.Element;
        m_attributePanel.IsVisible = kind == FilterOperandKind.Attribute;
        m_simplePanel.IsVisible = kind == FilterOperandKind.SimpleAttribute;
    }

    private async Task PickAttributeNodeAsync()
    {
        if (m_session is null)
        {
            return;
        }
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: m_session,
            Root: ObjectIds.RootFolder,
            Title: "Pick node",
            AcceptedClasses: NodeClass.Object | NodeClass.Variable | NodeClass.ObjectType
                | NodeClass.VariableType | NodeClass.View | NodeClass.Method
                | NodeClass.ReferenceType | NodeClass.DataType,
            Header: "Pick the node whose attribute this operand refers to."));
        NodeId? picked = await picker.ShowDialog<NodeId?>(this).ConfigureAwait(true);
        if (picked.HasValue && !picked.Value.IsNull)
        {
            m_attrNodeId.Text = picked.Value.ToString();
        }
    }

    private async Task PickSimpleTypeAsync()
    {
        if (m_session is null)
        {
            return;
        }
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: m_session,
            Root: ObjectTypeIds.BaseEventType,
            Title: "Pick event type",
            AcceptedClasses: NodeClass.ObjectType,
            ReferenceTypeId: ReferenceTypeIds.HasSubtype,
            Header: "Pick the event type that defines the field referenced by this SimpleAttributeOperand."));
        NodeId? picked = await picker.ShowDialog<NodeId?>(this).ConfigureAwait(true);
        if (picked.HasValue && !picked.Value.IsNull)
        {
            m_simpleTypeId.Text = picked.Value.ToString();
        }
    }

    private void OnOk()
    {
        var kind = m_kindCombo.SelectedItem is FilterOperandKind k ? k : FilterOperandKind.SimpleAttribute;
        try
        {
            FilterOperand built = kind switch
            {
                FilterOperandKind.Literal => BuildLiteral(),
                FilterOperandKind.Element => BuildElement(),
                FilterOperandKind.Attribute => BuildAttribute(),
                FilterOperandKind.SimpleAttribute => BuildSimpleAttribute(),
                _ => throw new InvalidOperationException("Unknown operand kind.")
            };
            Result = built;
            Close(built);
        }
        catch (Exception ex)
        {
            m_status.Text = $"Error: {ex.Message}";
            m_status.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private LiteralOperand BuildLiteral()
    {
        int idx = m_literalType.SelectedIndex;
        if (idx < 0)
        {
            idx = 11; // default String
        }
        (string _, BuiltInType _, NodeId dt) = s_literalTypes[idx];
        bool isArray = m_literalIsArray.IsChecked == true;
        string text = m_literalValue.Text ?? string.Empty;
        if (!VariantParser.TryParse(
            dt,
            isArray ? ValueRanks.OneDimension : ValueRanks.Scalar,
            text,
            out Variant value,
            out string? err))
        {
            throw new ArgumentException(err ?? "Could not parse the literal value.");
        }
        return new LiteralOperand(value);
    }

    private ElementOperand BuildElement()
    {
        decimal? val = m_elementIndex.Value;
        if (val is null || val.Value < 0)
        {
            throw new ArgumentException("Element index must be a non-negative integer.");
        }
        return new ElementOperand((uint)val.Value);
    }

    private AttributeOperand BuildAttribute()
    {
        var op = new AttributeOperand
        {
            NodeId = ParseNodeId(m_attrNodeId.Text),
            Alias = m_attrAlias.Text ?? string.Empty,
            BrowsePath = ParseRelativePath(m_attrPath.Text),
            AttributeId = SelectedAttributeId(m_attrAttributeId),
            IndexRange = m_attrIndexRange.Text ?? string.Empty,
        };
        return op;
    }

    private SimpleAttributeOperand BuildSimpleAttribute()
    {
        var op = new SimpleAttributeOperand
        {
            TypeDefinitionId = ParseNodeId(m_simpleTypeId.Text),
            BrowsePath = ParseSimpleBrowsePath(m_simplePath.Text),
            AttributeId = SelectedAttributeId(m_simpleAttributeId),
            IndexRange = m_simpleIndexRange.Text ?? string.Empty,
        };
        return op;
    }

    private static NodeId ParseNodeId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return NodeId.Null;
        }
        try
        {
            return NodeId.Parse(text.Trim());
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid NodeId: {ex.Message}", ex);
        }
    }

    private RelativePath ParseRelativePath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RelativePath();
        }
        if (m_session is not null)
        {
            try
            {
                return RelativePath.Parse(text.Trim(), m_session.TypeTree);
            }
            catch
            {
                // Fall through to the formatter-only path so the user can
                // still commit an operand the server will later validate.
            }
        }
        return ParseRelativePathLite(text);
    }

    private static RelativePath ParseRelativePathLite(string text)
    {
        // Last-resort offline parser used when no session is available
        // (and therefore no ITypeTable to resolve named reference types).
        // Treats every "/" segment as a forward HierarchicalReferences
        // element and every "." segment as Aggregates.  Anything else is
        // left for the server to reject.
        var rp = new RelativePath();
        var elements = new List<RelativePathElement>();
        try
        {
            RelativePathFormatter formatter = RelativePathFormatter.Parse(text.Trim());
            foreach (RelativePathFormatter.Element el in formatter.Elements)
            {
                var parsed = new RelativePathElement
                {
                    IsInverse = el.ElementType == RelativePathFormatter.ElementType.InverseReference,
                    IncludeSubtypes = el.IncludeSubtypes,
                    TargetName = el.TargetName,
                    ReferenceTypeId = el.ElementType switch
                    {
                        RelativePathFormatter.ElementType.AnyComponent => ReferenceTypeIds.Aggregates,
                        _ => ReferenceTypeIds.HierarchicalReferences,
                    }
                };
                elements.Add(parsed);
            }
        }
        catch
        {
            // Empty path is the OPC UA "no traversal" representation —
            // the server will reject if it actually needs one.
        }
        rp.Elements = elements;
        return rp;
    }

    private static ArrayOf<QualifiedName> ParseSimpleBrowsePath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<QualifiedName>();
        }
        // Try the slash-delimited form first (matches SimpleAttributeOperand.Format).
        string trimmed = text.Trim();
        if (trimmed.StartsWith('/'))
        {
            try
            {
                return SimpleAttributeOperand.Parse(trimmed);
            }
            catch
            {
                // Fall through to the line-based parser below.
            }
        }
        var qns = new List<QualifiedName>();
        foreach (string line in trimmed.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string token = line.Trim();
            if (token.Length == 0)
            {
                continue;
            }
            qns.Add(QualifiedName.Parse(token));
        }
        return qns;
    }

    private static uint SelectedAttributeId(ComboBox combo)
    {
        int idx = combo.SelectedIndex;
        if (idx < 0 || idx >= s_attributeIds.Length)
        {
            return Attributes.Value;
        }
        return s_attributeIds[idx].Id;
    }

    private static string FormatBrowsePath(ArrayOf<QualifiedName> path)
    {
        if (path.IsEmpty)
        {
            return string.Empty;
        }
        var buffer = new StringBuilder();
        for (int i = 0; i < path.Count; i++)
        {
            QualifiedName qn = path[i];
            if (qn.IsNull)
            {
                continue;
            }
            if (buffer.Length > 0)
            {
                buffer.Append('\n');
            }
            if (qn.NamespaceIndex != 0)
            {
                buffer.Append(qn.NamespaceIndex.ToString(CultureInfo.InvariantCulture)).Append(':');
            }
            buffer.Append(qn.Name);
        }
        return buffer.ToString();
    }

    private static string FormatVariant(Variant v)
    {
        if (v.IsNull)
        {
            return string.Empty;
        }
        object? raw = v.AsBoxedObject();
        return raw switch
        {
            null => string.Empty,
            string s => s,
            System.Collections.IEnumerable enumerable and not string => FormatArray(enumerable),
            System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString() ?? string.Empty
        };
    }

    private static string FormatArray(System.Collections.IEnumerable enumerable)
    {
        var buffer = new StringBuilder();
        bool first = true;
        foreach (object? item in enumerable)
        {
            if (!first)
            {
                buffer.Append(", ");
            }
            first = false;
            buffer.Append(item is System.IFormattable f
                ? f.ToString(null, CultureInfo.InvariantCulture)
                : item?.ToString() ?? string.Empty);
        }
        return buffer.ToString();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
