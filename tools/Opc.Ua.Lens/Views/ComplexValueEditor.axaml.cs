/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.StructuredValues;
using Orientation = Avalonia.Layout.Orientation;

namespace UaLens.Views;

/// <summary>
/// Recursive structured-value UI over a native-safe edit transaction. Optional
/// inclusion, union selection, nested edits and typed arrays are never applied to
/// the source body. Missing metadata and failed loads cannot produce a successful commit.
/// </summary>
internal sealed partial class ComplexValueEditor : UserControl
{
    public ComplexValueEditor()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public Variant Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Task InitializeAsync(
        NodeId dataTypeId,
        DataTypeDefinition? definition,
        IStructuredValueService service,
        Variant value,
        CancellationToken cancellationToken = default)
    {
        m_arrayMode = false;
        return InitializeCoreAsync(dataTypeId, definition, service, value, cancellationToken);
    }

    public Task InitializeArrayAsync(
        NodeId elementDataTypeId,
        DataTypeDefinition? elementDefinition,
        IStructuredValueService service,
        Variant value,
        int valueRank,
        ArrayOf<uint> declaredDimensions,
        bool isStructureField = false,
        CancellationToken cancellationToken = default)
    {
        m_arrayMode = true;
        m_arrayRank = valueRank;
        m_arrayDimensions = CoreUtils.Clone(declaredDimensions);
        m_isStructureField = isStructureField;
        return InitializeCoreAsync(elementDataTypeId, elementDefinition, service, value, cancellationToken);
    }

    public Task InitializeValueAsync(
        NodeId dataTypeId,
        DataTypeDefinition? definition,
        IStructuredValueService service,
        Variant value,
        int valueRank,
        ArrayOf<uint> declaredDimensions,
        CancellationToken cancellationToken = default)
    {
        return StructuredArrayValue.RequiresEditor(valueRank, value)
            ? InitializeArrayAsync(
                dataTypeId, definition, service, value, valueRank, declaredDimensions,
                cancellationToken: cancellationToken)
            : InitializeAsync(dataTypeId, definition, service, value, cancellationToken);
    }

    public bool TryCommit(out Variant committed, out string? error)
    {
        committed = Variant.Null;
        error = m_error;
        if (!m_editTask.IsCompleted)
        {
            error = "Finish or cancel the nested edit before committing.";
            return false;
        }
        if (m_arrayDraft is not null && m_arrayValue is not null)
        {
            if (m_arrayError is not null)
            {
                error = m_arrayError;
                return false;
            }
            if (m_dimensionsText is not null &&
                !string.Equals(m_dimensionsText.Text, m_appliedDimensionsText, StringComparison.Ordinal))
            {
                error = "Apply or undo the pending matrix dimensions before committing.";
                return false;
            }
            return m_arrayDraft.TryCommit(m_arrayValue, out committed, out error);
        }
        if (m_draft is null)
        {
            error ??= "The structured value is not loaded.";
            return false;
        }
        if (m_draft.OptionSet is not null)
        {
            var bits = new StructuredOptionBitEdit[m_optionRows.Count];
            for (int i = 0; i < bits.Length; i++)
            {
                OptionBitRow row = m_optionRows[i];
                bits[i] = new StructuredOptionBitEdit(
                    row.BitIndex, row.Value.IsChecked == true,
                    row.Validity is null ? null : row.Validity.IsChecked == true);
            }
            return m_draft.TryCommitOptionSet(bits, out committed, out error);
        }
        if (m_definition is EnumDefinition)
        {
            if (m_enumSelector?.SelectedItem is not EnumChoice choice)
            {
                error = "Select an enumeration value.";
                return false;
            }
            return m_draft.TryCommitEnum(choice.Value, out committed, out error);
        }

        var edits = new StructuredFieldEdit[m_rows.Count];
        for (int i = 0; i < edits.Length; i++)
        {
            FieldRow row = m_rows[i];
            bool included = m_unionSelector is not null
                ? m_unionSelector.SelectedIndex == i + 1
                : row.OptionalToggle?.IsChecked ?? true;
            Variant value = row.CurrentValue;
            if (included && row.Text is not null &&
                !StructuredScalarValue.TryParse(row.Field.TypeInfo.BuiltInType,
                    row.Text.Text ?? string.Empty, value, out value, out string? parseError))
            {
                error = $"Field '{row.Field.Definition.Name}': {parseError}";
                return false;
            }
            edits[i] = new StructuredFieldEdit(row.Field.Definition.Name!, value, included);
        }
        return m_draft.TryCommit(edits, out committed, out error);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? loading = m_loading;
        CancelEditing();
        try
        {
            await Task.WhenAll(m_pendingReloads.ToArray()).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // ReloadAsync already presents the current load's failure. Cleanup
            // must still await nested editors before a session service is released.
        }
        m_pendingReloads.RemoveAll(static task => task.IsCompleted);
        await m_editTask.ConfigureAwait(true);
        if (ReferenceEquals(m_loading, loading))
        {
            m_loading = null;
        }
        loading?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && !m_initializing && m_service is not null)
        {
            _ = ReloadFromBindingAsync();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelEditing();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task InitializeCoreAsync(
        NodeId dataTypeId,
        DataTypeDefinition? definition,
        IStructuredValueService service,
        Variant value,
        CancellationToken cancellationToken)
    {
        m_dataTypeId = dataTypeId;
        m_definition = definition;
        m_service = service ?? throw new ArgumentNullException(nameof(service));
        m_initializing = true;
        try
        {
            Value = value;
        }
        finally
        {
            m_initializing = false;
        }
        await StartReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    private void CancelEditing()
    {
        m_loading?.Cancel();
        m_editCancellation?.Cancel();
        m_nestedWindow?.Close();
        ShowError("Editing was canceled.");
    }

    private async Task ReloadFromBindingAsync()
    {
        try
        {
            await StartReloadAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // ReloadAsync reports failures only for the current load. A canceled
            // older binding update must not clear a newer successful draft.
            _ = ex;
        }
    }

    private Task StartReloadAsync(CancellationToken cancellationToken)
    {
        Task task = ReloadAsync(cancellationToken);
        m_pendingReloads.RemoveAll(static pending => pending.IsCompleted);
        m_pendingReloads.Add(task);
        return task;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        m_loading?.Cancel();
        m_loading?.Dispose();
        m_editCancellation?.Cancel();
        m_nestedWindow?.Close();
        var loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = loading.Token;
        m_loading = loading;
        m_draft = null;
        m_arrayDraft = null;
        m_arrayValue = null;
        m_arrayError = null;
        m_dimensionsText = null;
        m_error = "Loading structured value…";
        m_rows.Clear();
        m_optionRows.Clear();
        m_fieldDefinitions.Clear();
        this.RequiredControl<StackPanel>("BodyPanel").Children.Clear();
        this.RequiredControl<TextBlock>("HeaderLabel").Text = $"DataType: {m_dataTypeId}";
        this.RequiredControl<TextBlock>("HintLabel").Text = m_error;
        try
        {
            IStructuredValueService? service = m_service;
            DataTypeDefinition? definition = m_definition;
            if (service is null || (!m_arrayMode && definition is null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "No DataTypeDefinition is available. Use an explicit file import.");
            }
            if (m_arrayMode)
            {
                StructuredArrayDraft array = await service.OpenArrayAsync(
                    m_dataTypeId, m_arrayRank, m_arrayDimensions, Value, m_isStructureField, token)
                    .ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                m_arrayDraft = array;
                m_arrayValue = array.InitialValue;
                m_error = null;
                BuildArrayBody(array);
                return;
            }
            StructuredValueDraft draft = await service.OpenAsync(
                m_dataTypeId, definition!, Value, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < draft.Fields.Count; i++)
            {
                StructuredValueField field = draft.Fields[i];
                if (field.TypeInfo.IsScalar &&
                    field.TypeInfo.BuiltInType is not (BuiltInType.Null or BuiltInType.ExtensionObject) &&
                    (field.TypeInfo.BuiltInType == BuiltInType.Enumeration ||
                        TypeInfo.GetBuiltInType(field.Definition.DataType) == BuiltInType.Null))
                {
                    DataTypeDefinition? fieldDefinition = await service.ResolveAsync(
                        field.Definition.DataType, token).ConfigureAwait(true);
                    token.ThrowIfCancellationRequested();
                    if (fieldDefinition is EnumDefinition)
                    {
                        m_fieldDefinitions[field.Definition.DataType] = fieldDefinition;
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            m_draft = draft;
            m_error = null;
            BuildBody(draft);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(m_loading, loading))
            {
                ShowError(ex is OperationCanceledException ? "Loading was canceled." : ex.Message);
            }
            throw;
        }
    }

    private void BuildBody(StructuredValueDraft draft)
    {
        StackPanel body = this.RequiredControl<StackPanel>("BodyPanel");
        body.Children.Clear();
        m_rows.Clear();
        m_optionRows.Clear();
        m_unionSelector = null;
        m_enumSelector = null;
        if (draft.OptionSet is { } optionSet)
        {
            this.RequiredControl<TextBlock>("HintLabel").Text = optionSet.HasValidity
                ? $"OptionSet: {optionSet.ByteLength} bytes. Value and validity are independent; unnamed bits are kept."
                : $"OptionSet: {optionSet.StorageType}. Toggle named bits; unnamed bits and unsigned width are kept.";
            foreach (StructuredOptionBit bit in optionSet.Bits)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                var toggle = new CheckBox { Content = $"{bit.Name} (bit {bit.BitIndex})", IsChecked = bit.IsSet };
                row.Children.Add(toggle);
                CheckBox? valid = null;
                if (optionSet.HasValidity)
                {
                    valid = new CheckBox { Content = "Valid", IsChecked = bit.IsValid };
                    row.Children.Add(valid);
                }
                m_optionRows.Add(new OptionBitRow(bit.BitIndex, toggle, valid));
                body.Children.Add(row);
            }
            if (optionSet.Bits.IsEmpty)
            {
                body.Children.Add(new TextBlock { Text = "No named bits. The original bit fields will be retained." });
            }
            return;
        }
        if (draft.Definition is EnumDefinition enumeration)
        {
            this.RequiredControl<TextBlock>("HintLabel").Text = "Select a named enumeration value.";
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            bool hasCurrent = draft.InitialValue.TryGetValue(out int current);
            foreach (EnumField field in enumeration.Fields)
            {
                if (field.Value is < int.MinValue or > int.MaxValue)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadOutOfRange, "The enumeration contains a value outside the Int32 range.");
                }
                var choice = new EnumChoice(
                    field.Name ?? field.DisplayName.Text ?? "(unnamed)", (int)field.Value);
                combo.Items.Add(choice);
                if (hasCurrent && field.Value == current)
                {
                    combo.SelectedItem = choice;
                }
            }
            if (hasCurrent && combo.SelectedItem is null)
            {
                var retained = new EnumChoice("Unlisted value (retained)", current);
                combo.Items.Add(retained);
                combo.SelectedItem = retained;
            }
            if (!hasCurrent && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
            m_enumSelector = combo;
            body.Children.Add(combo);
            return;
        }

        var definition = (StructureDefinition)draft.Definition;
        bool union = definition.StructureType is StructureType.Union or StructureType.UnionWithSubtypedValues;
        bool optional = definition.StructureType == StructureType.StructureWithOptionalFields;
        this.RequiredControl<TextBlock>("HintLabel").Text = union
            ? "Select one field, or (none) for an empty union."
            : "Edit fields. Unchecked optional fields are omitted from the encoded value.";
        if (union)
        {
            m_unionSelector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            m_unionSelector.Items.Add("(none)");
            m_unionSelector.SelectedIndex = 0;
            for (int i = 0; i < draft.Fields.Count; i++)
            {
                m_unionSelector.Items.Add(draft.Fields[i].Definition.Name);
                if (draft.Fields[i].IsIncluded)
                {
                    m_unionSelector.SelectedIndex = i + 1;
                }
            }
            m_unionSelector.SelectionChanged += (_, _) => UpdateEnabledFields();
            body.Children.Add(m_unionSelector);
        }
        foreach (StructuredValueField field in draft.Fields)
        {
            var row = new FieldRow(field);
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("200,*,Auto"),
                Margin = new Thickness(0, 2, 0, 2)
            };
            var label = new TextBlock
            {
                Text = $"{field.Definition.Name} : {field.TypeInfo}",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            grid.Children.Add(label);
            Control control;
            bool array = field.TypeInfo.ValueRank >= ValueRanks.OneOrMoreDimensions ||
                (!row.CurrentValue.IsNull && !row.CurrentValue.TypeInfo.IsScalar);
            bool namedScalar = m_fieldDefinitions.ContainsKey(field.Definition.DataType);
            if (array || namedScalar || field.TypeInfo.BuiltInType is BuiltInType.Null or BuiltInType.ExtensionObject)
            {
                var button = new Button
                {
                    Content = array ? "Edit array / matrix…" : namedScalar ? "Edit named value…" : "Edit struct…",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                button.Click += async (_, _) =>
                {
                    if (!m_editTask.IsCompleted)
                    {
                        return;
                    }
                    m_editTask = EditFieldAsync(row, array);
                    await m_editTask.ConfigureAwait(true);
                };
                control = button;
            }
            else
            {
                row.Text = new TextBox
                {
                    Text = StructuredScalarValue.Format(row.CurrentValue),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace")
                };
                control = row.Text;
            }
            row.Control = control;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            if (optional && field.Definition.IsOptional)
            {
                row.OptionalToggle = new CheckBox
                {
                    Content = "Include",
                    IsChecked = field.IsIncluded,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                row.OptionalToggle.IsCheckedChanged += (_, _) => UpdateEnabledFields();
                Grid.SetColumn(row.OptionalToggle, 2);
                grid.Children.Add(row.OptionalToggle);
            }
            m_rows.Add(row);
            body.Children.Add(grid);
        }
        UpdateEnabledFields();
    }

    private void UpdateEnabledFields()
    {
        for (int i = 0; i < m_rows.Count; i++)
        {
            FieldRow row = m_rows[i];
            if (row.Control is not null)
            {
                row.Control.IsEnabled = m_unionSelector is not null
                    ? m_unionSelector.SelectedIndex == i + 1
                    : row.OptionalToggle?.IsChecked ?? true;
            }
        }
    }

    private void BuildArrayBody(StructuredArrayDraft draft)
    {
        StackPanel body = this.RequiredControl<StackPanel>("BodyPanel");
        body.Children.Clear();
        this.RequiredControl<TextBlock>("HintLabel").Text =
            $"Flat row-major order. Capacity: {draft.ElementCapacity} elements. " +
            "Reshape keeps all elements unless resizing is explicitly enabled.";
        m_arraySummary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Add(m_arraySummary);
        var resize = new CheckBox { Content = "Allow resizing / discarding trailing elements" };
        body.Children.Add(resize);
        if (draft.ValueRank is not (ValueRanks.OneDimension or ValueRanks.ScalarOrOneDimension))
        {
            body.Children.Add(new TextBlock { Text = "Matrix dimensions (comma-separated, for example 2, 3):" });
            var dimensions = new TextBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            m_dimensionsText = dimensions;
            body.Children.Add(dimensions);
            var reshape = new Button { Content = "Apply shape / create matrix" };
            reshape.Click += (_, _) => ApplyArrayChange(() =>
                draft.Reshape(RequireArrayValue(),
                    StructuredArrayDraft.ParseDimensions(dimensions.Text ?? string.Empty), resize.IsChecked == true));
            body.Children.Add(reshape);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var edit = new Button { Content = "Edit elements…" };
        edit.Click += async (_, _) =>
        {
            if (!m_editTask.IsCompleted)
            {
                return;
            }
            m_editTask = EditArrayElementsAsync();
            await m_editTask.ConfigureAwait(true);
        };
        actions.Children.Add(edit);
        var setNull = new Button { Content = "Set null" };
        setNull.Click += (_, _) => ApplyArrayChange(() =>
            draft.AsNull(RequireArrayValue(), resize.IsChecked == true));
        actions.Children.Add(setNull);
        var setEmpty = new Button { Content = "Set empty" };
        setEmpty.Click += (_, _) => ApplyArrayChange(() =>
        {
            StructuredArrayValue value = RequireArrayValue();
            if (!value.Elements.IsEmpty && resize.IsChecked != true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Explicitly allow discarding elements before setting an empty value.");
            }
            StructuredArrayValue empty = value.Dimensions.IsEmpty
                ? draft.WithElements(value, [])
                : draft.Reshape(value, new ArrayOf<int>(new int[value.Dimensions.Count]), allowResize: true);
            return empty;
        });
        actions.Children.Add(setEmpty);
        body.Children.Add(actions);
        UpdateArraySummary();
    }

    private StructuredArrayValue RequireArrayValue()
    {
        return m_arrayValue ?? throw new InvalidOperationException("The array draft is no longer loaded.");
    }

    private void ApplyArrayChange(Func<StructuredArrayValue> change)
    {
        try
        {
            m_arrayValue = change();
            m_arrayError = null;
            UpdateArraySummary();
        }
        catch (Exception ex) when (ex is ServiceResultException or ArgumentException or
            InvalidOperationException or FormatException or OverflowException or OperationCanceledException)
        {
            m_arrayError = ex.Message;
            this.RequiredControl<TextBlock>("HintLabel").Text = $"Edit failed: {ex.Message}";
        }
    }

    private void UpdateArraySummary()
    {
        StructuredArrayValue value = RequireArrayValue();
        string shape = value.Dimensions.IsEmpty
            ? "array"
            : $"matrix [{string.Join(", ", value.Dimensions.ToArray() ?? [])}]";
        if (m_arraySummary is not null)
        {
            m_arraySummary.Text = $"{value.ElementType} {shape}: " +
                (value.IsNull ? "null" : $"{value.Elements.Count} elements") +
                $". Editor capacity: {m_arrayDraft!.ElementCapacity}.";
        }
        m_appliedDimensionsText = value.Dimensions.IsEmpty
            ? string.Empty
            : string.Join(", ", value.Dimensions.ToArray() ?? []);
        if (m_dimensionsText is not null)
        {
            m_dimensionsText.Text = m_appliedDimensionsText;
        }
        this.RequiredControl<TextBlock>("HintLabel").Text = m_isStructureField
            ? "Null and empty matrices retain their raw structure-field encoding."
            : "Standalone matrix Variants require non-empty dimensions. Null and empty 1-D arrays remain distinct.";
    }

    private async Task EditArrayElementsAsync()
    {
        IStructuredValueService? service = m_service;
        StructuredArrayDraft? draft = m_arrayDraft;
        StructuredArrayValue? snapshot = m_arrayValue;
        if (service is null || draft is null || snapshot is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            this.RequiredControl<TextBlock>("HintLabel").Text = "The array editor is not attached to an active window.";
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_loading?.Token ?? default);
        m_editCancellation = cancellation;
        try
        {
            DataTypeDefinition? definition = m_definition ??
                await service.ResolveAsync(m_dataTypeId, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            var dialog = new EditArrayDialog(
                "Typed array elements", m_dataTypeId, definition, snapshot.Elements, service, snapshot.ElementType);
            m_nestedWindow = dialog;
            using CancellationTokenRegistration registration = cancellation.Token.Register(
                () => Dispatcher.UIThread.Post(dialog.Close));
            try
            {
                await dialog.ShowDialog(owner).ConfigureAwait(true);
            }
            finally
            {
                await dialog.StopAsync().ConfigureAwait(true);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (dialog.WasCommitted && ReferenceEquals(m_arrayDraft, draft) && ReferenceEquals(m_arrayValue, snapshot))
            {
                ApplyArrayChange(() => draft.WithElements(snapshot, dialog.Result));
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(m_arrayDraft, draft))
            {
                this.RequiredControl<TextBlock>("HintLabel").Text = $"Edit failed: {ex.Message}";
            }
        }
        finally
        {
            m_editCancellation = null;
            m_nestedWindow = null;
        }
    }

    private async Task EditFieldAsync(FieldRow row, bool array)
    {
        IStructuredValueService? service = m_service;
        StructuredValueDraft? draft = m_draft;
        if (service is null || draft is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            this.RequiredControl<TextBlock>("HintLabel").Text = "The field editor is not attached to an active window.";
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_loading?.Token ?? default);
        m_editCancellation = cancellation;
        try
        {
            DataTypeDefinition? definition = await service.ResolveAsync(
                row.Field.Definition.DataType, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (array)
            {
                (bool accepted, Variant edited) = await EditArrayFieldAsync(
                    row, definition, service, owner, cancellation.Token).ConfigureAwait(true);
                cancellation.Token.ThrowIfCancellationRequested();
                if (accepted && ReferenceEquals(m_draft, draft))
                {
                    row.CurrentValue = edited;
                }
            }
            else
            {
                var dialog = new ComplexValueElementDialog(
                    row.Field.Definition.DataType, definition, service, row.CurrentValue);
                m_nestedWindow = dialog;
                using CancellationTokenRegistration registration = cancellation.Token.Register(
                    () => Dispatcher.UIThread.Post(dialog.Close));
                try
                {
                    await dialog.ShowDialog(owner).ConfigureAwait(true);
                }
                finally
                {
                    await dialog.StopAsync().ConfigureAwait(true);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if (dialog.WasCommitted && ReferenceEquals(m_draft, draft))
                {
                    row.CurrentValue = dialog.Result;
                }
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(m_draft, draft))
            {
                this.RequiredControl<TextBlock>("HintLabel").Text = $"Edit failed: {ex.Message}";
            }
        }
        finally
        {
            m_editCancellation = null;
            m_nestedWindow = null;
        }
    }

    private async Task<(bool Accepted, Variant Value)> EditArrayFieldAsync(
        FieldRow row,
        DataTypeDefinition? definition,
        IStructuredValueService service,
        Window owner,
        CancellationToken cancellationToken)
    {
        var editor = new ComplexValueEditor();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel }
        };
        var content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto,Auto")
        };
        content.Children.Add(editor);
        Grid.SetRow(status, 1);
        content.Children.Add(status);
        Grid.SetRow(buttons, 2);
        content.Children.Add(buttons);
        var dialog = new Window
        {
            Title = $"Array / matrix editor - {row.Field.Definition.Name}",
            Width = 740,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
        bool accepted = false;
        Variant result = Variant.Null;
        ok.Click += (_, _) =>
        {
            if (editor.TryCommit(out Variant candidate, out string? error))
            {
                result = candidate;
                accepted = true;
                dialog.Close();
            }
            else
            {
                status.Text = error;
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        m_nestedWindow = dialog;
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(dialog.Close));
        try
        {
            await editor.InitializeArrayAsync(
                row.Field.Definition.DataType, definition, service, row.CurrentValue,
                row.Field.Definition.ValueRank, row.Field.Definition.ArrayDimensions,
                isStructureField: true, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            await dialog.ShowDialog(owner).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return (accepted, result);
        }
        finally
        {
            await editor.StopAsync().ConfigureAwait(true);
        }
    }

    private void ShowError(string message)
    {
        m_error = message;
        m_draft = null;
        m_arrayDraft = null;
        m_arrayValue = null;
        this.RequiredControl<TextBlock>("HintLabel").Text = message;
    }

    private sealed class FieldRow
    {
        public FieldRow(StructuredValueField field)
        {
            Field = field;
            CurrentValue = field.Value.Copy();
            if (CurrentValue.IsNull && field.TypeInfo.IsScalar &&
                field.TypeInfo.BuiltInType != BuiltInType.Null)
            {
                CurrentValue = Variant.CreateDefault(field.TypeInfo);
            }
        }

        public StructuredValueField Field { get; }
        public Variant CurrentValue { get; set; }
        public TextBox? Text { get; set; }
        public Control? Control { get; set; }
        public CheckBox? OptionalToggle { get; set; }
    }

    private sealed record EnumChoice(string Name, int Value)
    {
        public override string ToString()
        {
            return $"{Name} ({Value})";
        }
    }

    private sealed record OptionBitRow(int BitIndex, CheckBox Value, CheckBox? Validity);

    public static readonly StyledProperty<Variant> ValueProperty =
        AvaloniaProperty.Register<ComplexValueEditor, Variant>(
            nameof(Value), defaultValue: Variant.Null, defaultBindingMode: BindingMode.TwoWay);

    private readonly List<FieldRow> m_rows = [];
    private readonly List<OptionBitRow> m_optionRows = [];
    private readonly Dictionary<NodeId, DataTypeDefinition> m_fieldDefinitions = [];
    private NodeId m_dataTypeId;
    private DataTypeDefinition? m_definition;
    private IStructuredValueService? m_service;
    private StructuredValueDraft? m_draft;
    private StructuredArrayDraft? m_arrayDraft;
    private StructuredArrayValue? m_arrayValue;
    private TextBox? m_dimensionsText;
    private TextBlock? m_arraySummary;
    private string? m_arrayError;
    private string? m_appliedDimensionsText;
    private bool m_arrayMode;
    private bool m_isStructureField;
    private int m_arrayRank;
    private ArrayOf<uint> m_arrayDimensions;
    private ComboBox? m_enumSelector;
    private ComboBox? m_unionSelector;
    private CancellationTokenSource? m_loading;
    private string? m_error;
    private bool m_initializing;
    private readonly List<Task> m_pendingReloads = [];
    private Task m_editTask = Task.CompletedTask;
    private CancellationTokenSource? m_editCancellation;
    private Window? m_nestedWindow;
}
