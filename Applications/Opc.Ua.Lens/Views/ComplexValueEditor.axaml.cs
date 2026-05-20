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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Subscriptions;
using BindingFlags = System.Reflection.BindingFlags;
using PropertyInfo = System.Reflection.PropertyInfo;

namespace UaLens.Views;

/// <summary>
/// Recursive per-field editor for OPC UA structured values (Structure,
/// StructureWithOptionalFields, Union) and Enum types.  Operates on a
/// single <see cref="Variant"/> <see cref="Value"/> bindable property:
/// the setter parses the incoming value into a list of editable field
/// rows; <see cref="TryCommit"/> reassembles the rows back into a
/// <see cref="Variant"/> for the host dialog's Write/Call payload.
///
/// <para>
/// The editor is intentionally agnostic of <see cref="WriteValueDialog"/>
/// and <see cref="MethodCallDialog"/> — it only knows about a
/// <see cref="DataTypeDefinition"/> + a backing
/// <see cref="ManagedSession"/> (used to resolve nested field
/// definitions on demand).  Hosts attach via <see cref="Initialize"/>
/// after construction and read the edited value via
/// <see cref="TryCommit"/>.
/// </para>
///
/// <para>
/// When the supplied definition is <c>null</c> (server doesn't expose
/// the attribute), the editor renders a single "(complex type — opaque)"
/// hint and behaves as a no-op committing back the original
/// <see cref="Variant"/> unchanged — callers fall back to the existing
/// primitive / file-import path.
/// </para>
/// </summary>
internal sealed partial class ComplexValueEditor : UserControl
{
    public static readonly StyledProperty<Variant> ValueProperty =
        AvaloniaProperty.Register<ComplexValueEditor, Variant>(
            nameof(Value),
            defaultValue: Variant.Null,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// The variant value being edited.  Setting this property rebuilds
    /// the editor body from the new value's fields.
    /// </summary>
    public Variant Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private DataTypeDefinition? m_definition;
    private NodeId m_dataTypeId = NodeId.Null;
    private ManagedSession? m_session;
    private readonly List<FieldEditor> m_fields = [];

    public ComplexValueEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the editor to a specific DataType + session.  Must be
    /// called before <see cref="Value"/> is meaningful for structures.
    /// </summary>
    /// <param name="dataTypeId">DataType NodeId of the value being edited.</param>
    /// <param name="definition">Resolved definition (Structure / Enum) or null.</param>
    /// <param name="session">Active session (used to fetch nested defs).</param>
    public void Initialize(NodeId dataTypeId, DataTypeDefinition? definition, ManagedSession session)
    {
        m_dataTypeId = dataTypeId;
        m_definition = definition;
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            // Reapply incoming value when the host pushes a new variant.
            PopulateFromValue();
        }
    }

    /// <summary>
    /// Assembles the edited field state back into a <see cref="Variant"/>.
    /// Returns <c>false</c> with a description of the offending field if
    /// any inline editor failed to parse.  Hosts should not call
    /// <c>session.Write</c> until this returns true.
    /// </summary>
    public bool TryCommit(out Variant committed, out string? error)
    {
        committed = Value;
        error = null;
        if (m_definition is EnumDefinition)
        {
            if (m_fields.Count == 1 && m_fields[0].TryReadEnum(out int enumValue))
            {
                committed = Variant.From(enumValue);
                return true;
            }
            error = "Enum value could not be parsed.";
            return false;
        }
        if (m_definition is StructureDefinition sd)
        {
            return TryCommitStructure(sd, out committed, out error);
        }
        // Opaque path — value is whatever the host originally provided.
        return true;
    }

    private void Rebuild()
    {
        StackPanel body = this.RequiredControl<StackPanel>("BodyPanel");
        TextBlock header = this.RequiredControl<TextBlock>("HeaderLabel");
        TextBlock hint = this.RequiredControl<TextBlock>("HintLabel");
        body.Children.Clear();
        m_fields.Clear();

        switch (m_definition)
        {
            case null:
                header.Text = m_dataTypeId.IsNull ? "(no DataType)" : $"DataType: {m_dataTypeId}";
                hint.Text = "(complex type — opaque)  No DataTypeDefinition exposed by the server; "
                            + "use the primitive editor or a file import instead.";
                break;
            case EnumDefinition ed:
                header.Text = $"Enum  {m_dataTypeId}";
                hint.Text = ed.IsOptionSet
                    ? "Bitset / OptionSet enum — pick a base value; combine flags using the Advanced editor."
                    : "Enumeration — pick a named value.";
                BuildEnumBody(body, ed);
                break;
            case StructureDefinition sd:
                header.Text = StructureHeader(sd);
                hint.Text = StructureHint(sd);
                BuildStructureBody(body, sd);
                break;
            default:
                header.Text = $"DataType: {m_dataTypeId}";
                hint.Text = $"(unsupported definition kind {m_definition.GetType().Name})";
                break;
        }
        PopulateFromValue();
    }

    private static string StructureHeader(StructureDefinition sd)
    {
        return sd.StructureType switch
        {
            StructureType.Union => "Union",
            StructureType.UnionWithSubtypedValues => "Union (subtyped)",
            StructureType.StructureWithOptionalFields => "Structure (with optional fields)",
            StructureType.StructureWithSubtypedValues => "Structure (subtyped)",
            _ => "Structure"
        };
    }

    private static string StructureHint(StructureDefinition sd)
    {
        int count = sd.Fields.Count;
        return sd.StructureType switch
        {
            StructureType.Union or StructureType.UnionWithSubtypedValues
                => $"Select exactly one of {count} fields.",
            StructureType.StructureWithOptionalFields
                => $"{count} field(s).  Tick the box to include an optional field; "
                   + "untick to omit it from the encoded value.",
            _ => $"{count} field(s)."
        };
    }

    private void BuildEnumBody(StackPanel body, EnumDefinition ed)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (EnumField f in ed.Fields)
        {
            combo.Items.Add(new EnumChoice(f));
        }
        if (ed.Fields.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
        body.Children.Add(combo);
        m_fields.Add(new FieldEditor.EnumRow(combo));
    }

    private void BuildStructureBody(StackPanel body, StructureDefinition sd)
    {
        if (sd.Fields.IsEmpty)
        {
            return;
        }
        bool isUnion = sd.StructureType is StructureType.Union
            or StructureType.UnionWithSubtypedValues;
        bool hasOptional = sd.StructureType is StructureType.StructureWithOptionalFields;

        // For Unions we need to enforce single-selection across rows; the
        // editor surfaces this as a "Selected" radio per field.  For
        // optional structs, an "Include" checkbox per optional field.
        ComboBox? unionSelector = null;
        if (isUnion)
        {
            unionSelector = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };
            foreach (StructureField f in sd.Fields)
            {
                unionSelector.Items.Add(f.Name ?? "(unnamed)");
            }
            if (sd.Fields.Count > 0)
            {
                unionSelector.SelectedIndex = 0;
            }
            body.Children.Add(new TextBlock
            {
                Text = "Selected field",
                Foreground = (Application.Current?.FindResource("TextSecondary") as IBrush)
                    ?? Brushes.Transparent
            });
            body.Children.Add(unionSelector);
        }

        for (int i = 0; i < sd.Fields.Count; i++)
        {
            StructureField f = sd.Fields[i];
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("200,*,Auto,Auto"),
                Margin = new Thickness(0, 2, 0, 2)
            };
            var label = new TextBlock
            {
                Text = $"{f.Name} : {DescribeField(f)}",
                Foreground = (Application.Current?.FindResource("TextSecondary") as IBrush)
                    ?? Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace")
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            FieldEditor editor = BuildFieldEditor(f, grid, isUnion, hasOptional, unionSelector, i);
            m_fields.Add(editor);
            body.Children.Add(grid);
        }
    }

    private FieldEditor BuildFieldEditor(
        StructureField f,
        Grid host,
        bool isUnion,
        bool hasOptional,
        ComboBox? unionSelector,
        int unionIndex)
    {
        bool isArray = f.ValueRank == ValueRanks.OneDimension
            || f.ValueRank == ValueRanks.OneOrMoreDimensions
            || (f.ValueRank > 0);
        BuiltInType bi = TypeInfo.GetBuiltInType(f.DataType);

        Control valueControl;
        Button? arrayButton = null;
        Button? structButton = null;

        if (isArray)
        {
            arrayButton = new Button
            {
                Content = "Edit array…",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            valueControl = arrayButton;
        }
        else if (bi == BuiltInType.ExtensionObject || bi == BuiltInType.Null)
        {
            // Nested structure (or unresolved sub-type).
            structButton = new Button
            {
                Content = "Edit struct…",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            valueControl = structButton;
        }
        else
        {
            valueControl = new TextBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace")
            };
        }
        Grid.SetColumn(valueControl, 1);
        host.Children.Add(valueControl);

        CheckBox? optionalToggle = null;
        if (hasOptional && f.IsOptional)
        {
            optionalToggle = new CheckBox
            {
                Content = "Include",
                IsChecked = false,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(optionalToggle, 2);
            host.Children.Add(optionalToggle);
        }

        FieldEditor editor;
        if (arrayButton is not null)
        {
            var row = new FieldEditor.ArrayRow(
                f, bi, isUnion ? unionSelector : null, unionIndex, optionalToggle);
            arrayButton.Click += async (_, _) => await OnEditArrayAsync(row).ConfigureAwait(true);
            editor = row;
        }
        else if (structButton is not null)
        {
            var row = new FieldEditor.StructRow(
                f, isUnion ? unionSelector : null, unionIndex, optionalToggle);
            structButton.Click += async (_, _) => await OnEditStructAsync(row).ConfigureAwait(true);
            editor = row;
        }
        else
        {
            editor = new FieldEditor.PrimitiveRow(
                f, bi, (TextBox)valueControl,
                isUnion ? unionSelector : null, unionIndex, optionalToggle);
        }
        return editor;
    }

    private static string DescribeField(StructureField f)
    {
        BuiltInType bi = TypeInfo.GetBuiltInType(f.DataType);
        string baseName = bi == BuiltInType.Null || bi == BuiltInType.ExtensionObject
            ? f.DataType.ToString() ?? "Structure"
            : bi.ToString();
        string suffix = f.ValueRank switch
        {
            ValueRanks.OneDimension => "[]",
            ValueRanks.OneOrMoreDimensions => "[...]",
            int r when r > 1 => $"[{r}D]",
            _ => string.Empty
        };
        return baseName + suffix + (f.IsOptional ? "  (optional)" : string.Empty);
    }

    private void PopulateFromValue()
    {
        Variant v = Value;
        if (m_definition is EnumDefinition && m_fields.Count == 1)
        {
            if (v.TryGetValue(out int iv))
            {
                m_fields[0].WriteEnum(iv);
            }
            return;
        }
        if (m_definition is StructureDefinition sd)
        {
            ApplyStructureValue(sd, v);
        }
    }

    private void ApplyStructureValue(StructureDefinition sd, Variant v)
    {
        if (sd.Fields.IsEmpty)
        {
            return;
        }
        if (v.IsNull || v.AsBoxedObject() is not ExtensionObject eo || eo.Body is null)
        {
            // Nothing to import — leave defaults from BuildStructureBody.
            return;
        }
        // Walk the body object reflectively, matching by field name.
        // This works whether or not the body is a generated complex
        // type — any class that exposes public properties matching the
        // structure field names will roundtrip its values into the UI.
        object body = eo.Body;
        Type t = body.GetType();
        for (int i = 0; i < sd.Fields.Count && i < m_fields.Count; i++)
        {
            string? name = sd.Fields[i].Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            PropertyInfo? p = t.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p is null)
            {
                continue;
            }
            object? raw = p.GetValue(body);
            if (raw is null)
            {
                continue;
            }
            m_fields[i].WriteFromObject(raw);
        }
    }

    private bool TryCommitStructure(StructureDefinition sd, out Variant committed, out string? error)
    {
        committed = Value;
        error = null;
        if (sd.Fields.IsEmpty)
        {
            return true;
        }

        // If the existing Value already contains a typed body, mutate it
        // in place — this is the only path that survives a Write back to
        // the server (the body knows how to encode itself).  For brand
        // new values (or opaque bodies) we keep the original Variant —
        // the UI worked, but the server-side roundtrip needs the complex
        // type system loaded.
        object? body = null;
        if (Value.AsBoxedObject() is ExtensionObject typedEo)
        {
            body = typedEo.Body;
        }
        if (body is null)
        {
            // No typed body — surface as a soft warning but allow OK.
            // The caller's existing JSON / XML import path remains the
            // working route for newly constructed structures.
            error = "Edits are kept in-memory but cannot be re-encoded "
                + "without an existing typed body — use Import to seed a "
                + "value first, then edit.";
            return false;
        }

        Type t = body.GetType();
        for (int i = 0; i < sd.Fields.Count && i < m_fields.Count; i++)
        {
            string? name = sd.Fields[i].Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            PropertyInfo? p = t.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p is null || !p.CanWrite)
            {
                continue;
            }
            if (!m_fields[i].TryReadObject(p.PropertyType, out object? boxed, out string? rerr))
            {
                error = $"Field '{name}': {rerr}";
                return false;
            }
            try
            {
                p.SetValue(body, boxed);
            }
            catch (Exception ex)
            {
                error = $"Field '{name}': could not set value ({ex.Message})";
                return false;
            }
        }
        // committed remains the same Variant — the underlying body was
        // mutated in place; the Variant value is still a reference to it.
        return true;
    }

    private async Task OnEditArrayAsync(FieldEditor.ArrayRow row)
    {
        if (m_session is null)
        {
            return;
        }
        DataTypeDefinition? elementDef = await ComplexValueIO
            .GetDataTypeDefinitionAsync(row.Field.DataType, m_session, CancellationToken.None)
            .ConfigureAwait(true);
        Window? owner = TopLevelWindow();
        if (owner is null)
        {
            return;
        }
        var dlg = new EditArrayDialog(
            $"Array editor — {row.Field.Name}",
            row.Field.DataType,
            elementDef,
            row.CurrentElements,
            m_session);
        await dlg.ShowDialog(owner).ConfigureAwait(true);
        if (dlg.WasCommitted && dlg.Result is not null)
        {
            row.SetElements(dlg.Result);
        }
    }

    private async Task OnEditStructAsync(FieldEditor.StructRow row)
    {
        if (m_session is null)
        {
            return;
        }
        DataTypeDefinition? nested = await ComplexValueIO
            .GetDataTypeDefinitionAsync(row.Field.DataType, m_session, CancellationToken.None)
            .ConfigureAwait(true);
        Window? owner = TopLevelWindow();
        if (owner is null)
        {
            return;
        }
        var dlg = new ComplexValueElementDialog(
            row.Field.DataType, nested, m_session, row.CurrentValue);
        Variant? edited = await dlg.ShowDialog<Variant?>(owner).ConfigureAwait(true);
        if (edited.HasValue)
        {
            row.CurrentValue = edited.Value;
        }
    }

    private Window? TopLevelWindow()
    {
        return TopLevel.GetTopLevel(this) as Window;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// Wraps an <see cref="EnumField"/> for binding into the editor's enum
/// <see cref="ComboBox"/>.  The <see cref="ToString"/> override drives
/// the default item template.
/// </summary>
internal sealed class EnumChoice
{
    public EnumField Field { get; }

    public EnumChoice(EnumField field)
    {
        Field = field;
    }

    public override string ToString()
    {
        string name = Field.Name ?? Field.DisplayName.Text ?? "(unnamed)";
        return $"{name} ({Field.Value})";
    }
}

/// <summary>
/// Abstract per-field row backing the structure editor.  Concrete
/// subclasses know how to parse / format a specific UI control kind
/// (primitive TextBox, nested struct button, array button, enum
/// ComboBox).  Kept as a closed hierarchy in this file because each
/// row is tightly coupled to the editor's commit/reload pipeline.
/// </summary>
internal abstract class FieldEditor
{
    public StructureField Field { get; }
    public CheckBox? OptionalToggle { get; }
    public ComboBox? UnionSelector { get; }
    public int UnionIndex { get; }

    protected FieldEditor(StructureField field,
        ComboBox? unionSelector, int unionIndex, CheckBox? optionalToggle)
    {
        Field = field;
        UnionSelector = unionSelector;
        UnionIndex = unionIndex;
        OptionalToggle = optionalToggle;
    }

    /// <summary>
    /// True when this field is currently part of the encoded value:
    /// always true for required fields, dependent on the toggle for
    /// optional ones, dependent on the union selector for unions.
    /// </summary>
    public bool IsIncluded
    {
        get
        {
            if (UnionSelector is not null)
            {
                return UnionSelector.SelectedIndex == UnionIndex;
            }
            if (OptionalToggle is not null)
            {
                return OptionalToggle.IsChecked == true;
            }
            return true;
        }
    }

    public virtual bool TryReadEnum(out int value)
    {
        value = 0;
        return false;
    }

    public virtual void WriteEnum(int value)
    {
        // overridden in EnumRow
    }

    public virtual void WriteFromObject(object value)
    {
        // overridden in subclasses where applicable
    }

    public abstract bool TryReadObject(Type targetType, out object? boxed, out string? error);

    public sealed class PrimitiveRow : FieldEditor
    {
        private readonly TextBox m_text;
        private readonly BuiltInType m_builtIn;

        public PrimitiveRow(StructureField f, BuiltInType bi, TextBox box,
            ComboBox? unionSelector, int unionIndex, CheckBox? optional)
            : base(f, unionSelector, unionIndex, optional)
        {
            m_builtIn = bi;
            m_text = box;
        }

        public override void WriteFromObject(object value)
        {
            m_text.Text = value switch
            {
                null => string.Empty,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                string s => s,
                _ => value.ToString() ?? string.Empty
            };
            if (OptionalToggle is not null)
            {
                OptionalToggle.IsChecked = true;
            }
        }

        public override bool TryReadObject(Type targetType, out object? boxed, out string? error)
        {
            boxed = null;
            error = null;
            if (OptionalToggle is not null && OptionalToggle.IsChecked != true)
            {
                // Field omitted by user toggle — leave the underlying
                // property at its existing value (e.g. null).
                return true;
            }
            string s = m_text.Text?.Trim() ?? string.Empty;
            try
            {
                if (!VariantParser.TryParse(Field.DataType, ValueRanks.Scalar, s,
                    out Variant parsed, out string? perr))
                {
                    error = perr;
                    return false;
                }
                boxed = CoerceToTargetType(parsed, targetType);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static object? CoerceToTargetType(Variant v, Type targetType)
        {
            object? raw = v.AsBoxedObject();
            if (raw is null)
            {
                return null;
            }
            // Common: target == typeof(int) and parsed == int.  Otherwise
            // delegate to Convert for narrow numeric coercion.
            if (targetType.IsInstanceOfType(raw))
            {
                return raw;
            }
            try
            {
                return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                // Returning the original boxed value forces a TypeMismatch
                // on the property setter — surfaced to the user via
                // TryReadObject's catch block.
                return raw;
            }
        }
    }

    public sealed class ArrayRow : FieldEditor
    {
        private List<Variant> m_elements = [];

        public ArrayRow(StructureField f, BuiltInType bi,
            ComboBox? unionSelector, int unionIndex, CheckBox? optional)
            : base(f, unionSelector, unionIndex, optional)
        {
            // bi is currently unused but kept on the row so future
            // commits can build correctly typed arrays without a re-lookup.
            _ = bi;
        }

        public IReadOnlyList<Variant> CurrentElements => m_elements;

        public void SetElements(IReadOnlyList<Variant> elements)
        {
            m_elements = new List<Variant>(elements);
            if (OptionalToggle is not null)
            {
                OptionalToggle.IsChecked = true;
            }
        }

        public override void WriteFromObject(object value)
        {
            if (value is System.Collections.IEnumerable enumerable and not string)
            {
                var list = new List<Variant>();
                foreach (object? el in enumerable)
                {
                    list.Add(el is null ? Variant.Null : Variant.From(el.ToString() ?? string.Empty));
                }
                m_elements = list;
                if (OptionalToggle is not null)
                {
                    OptionalToggle.IsChecked = true;
                }
            }
        }

        public override bool TryReadObject(Type targetType, out object? boxed, out string? error)
        {
            boxed = null;
            error = null;
            if (OptionalToggle is not null && OptionalToggle.IsChecked != true)
            {
                return true;
            }
            // Array commit is intentionally not implemented for the typed
            // body path: callers retrieve the live element list separately
            // and the existing value is left untouched.  This keeps the
            // round-trip safe even when the property type is something
            // like a generated <c>ArrayOf&lt;T&gt;</c> the editor can't
            // construct.
            return true;
        }
    }

    public sealed class StructRow : FieldEditor
    {
        public Variant CurrentValue { get; set; } = Variant.Null;

        public StructRow(StructureField f,
            ComboBox? unionSelector, int unionIndex, CheckBox? optional)
            : base(f, unionSelector, unionIndex, optional)
        {
        }

        public override void WriteFromObject(object value)
        {
            // Wrap as ExtensionObject so the nested editor can introspect.
            if (value is ExtensionObject eo)
            {
                CurrentValue = Variant.From(eo);
            }
            else if (value is IEncodeable)
            {
#pragma warning disable CS0618 // Use TryGetAsXXX API for type safe access to body.
                CurrentValue = Variant.From(new ExtensionObject(ExpandedNodeId.Null, value));
#pragma warning restore CS0618
            }
            if (OptionalToggle is not null)
            {
                OptionalToggle.IsChecked = true;
            }
        }

        public override bool TryReadObject(Type targetType, out object? boxed, out string? error)
        {
            boxed = null;
            error = null;
            if (OptionalToggle is not null && OptionalToggle.IsChecked != true)
            {
                return true;
            }
            object? raw = CurrentValue.AsBoxedObject();
            if (raw is ExtensionObject eo2 && eo2.Body is not null)
            {
                boxed = targetType.IsInstanceOfType(eo2.Body) ? eo2.Body : eo2;
            }
            else
            {
                boxed = raw;
            }
            return true;
        }
    }

    public sealed class EnumRow : FieldEditor
    {
        private readonly ComboBox m_combo;

        public EnumRow(ComboBox combo)
            : base(new StructureField { Name = "value" }, null, 0, null)
        {
            m_combo = combo;
        }

        public override bool TryReadEnum(out int value)
        {
            if (m_combo.SelectedItem is EnumChoice c)
            {
                value = (int)c.Field.Value;
                return true;
            }
            value = 0;
            return false;
        }

        public override void WriteEnum(int value)
        {
            for (int i = 0; i < m_combo.Items.Count; i++)
            {
                if (m_combo.Items[i] is EnumChoice c && c.Field.Value == value)
                {
                    m_combo.SelectedIndex = i;
                    return;
                }
            }
        }

        public override bool TryReadObject(Type targetType, out object? boxed, out string? error)
        {
            if (TryReadEnum(out int v))
            {
                boxed = v;
                error = null;
                return true;
            }
            boxed = null;
            error = "no enum selection";
            return false;
        }
    }
}
