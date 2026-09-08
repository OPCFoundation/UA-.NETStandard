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

    public async Task InitializeAsync(
        NodeId dataTypeId,
        DataTypeDefinition? definition,
        IStructuredValueService service,
        Variant value,
        CancellationToken cancellationToken = default)
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

    public bool TryCommit(out Variant committed, out string? error)
    {
        committed = Variant.Null;
        error = m_error;
        if (m_draft is null)
        {
            error ??= "The structured value is not loaded.";
            return false;
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
        m_loading?.Cancel();
        m_editCancellation?.Cancel();
        m_nestedWindow?.Close();
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
        m_loading?.Cancel();
        m_editCancellation?.Cancel();
        m_nestedWindow?.Close();
        base.OnDetachedFromVisualTree(e);
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
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        m_loading = loading;
        m_draft = null;
        m_error = "Loading structured value…";
        m_rows.Clear();
        this.RequiredControl<StackPanel>("BodyPanel").Children.Clear();
        this.RequiredControl<TextBlock>("HeaderLabel").Text = $"DataType: {m_dataTypeId}";
        this.RequiredControl<TextBlock>("HintLabel").Text = m_error;
        try
        {
            if (m_definition is null || m_service is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "No DataTypeDefinition is available. Use an explicit file import.");
            }
            StructuredValueDraft draft = await m_service.OpenAsync(
                m_dataTypeId, m_definition, Value, loading.Token).ConfigureAwait(true);
            loading.Token.ThrowIfCancellationRequested();
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
        finally
        {
            if (ReferenceEquals(m_loading, loading))
            {
                m_loading = null;
            }
        }
    }

    private void BuildBody(StructuredValueDraft draft)
    {
        StackPanel body = this.RequiredControl<StackPanel>("BodyPanel");
        body.Children.Clear();
        m_rows.Clear();
        m_unionSelector = null;
        m_enumSelector = null;
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
            if (array || field.TypeInfo.BuiltInType is BuiltInType.Null or BuiltInType.ExtensionObject)
            {
                var button = new Button
                {
                    Content = array ? "Edit array…" : "Edit struct…",
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

    private async Task EditFieldAsync(FieldRow row, bool array)
    {
        if (m_service is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        using var cancellation = new CancellationTokenSource();
        m_editCancellation = cancellation;
        try
        {
            DataTypeDefinition? definition = await m_service.ResolveAsync(
                row.Field.Definition.DataType, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (array)
            {
                if (row.CurrentValue.IsNull && row.Field.Definition.ValueRank > ValueRanks.OneDimension)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "Import a typed matrix to establish its dimensions first.");
                }
                StructuredArrayValue snapshot = row.CurrentValue.IsNull
                    ? new StructuredArrayValue(row.Field.TypeInfo.BuiltInType, [])
                    : StructuredArrayValue.Read(row.CurrentValue, m_service.MessageContext);
                var dialog = new EditArrayDialog(
                    $"Array editor — {row.Field.Definition.Name}", row.Field.Definition.DataType,
                    definition, snapshot.Elements, m_service, snapshot.ElementType);
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
                if (dialog.WasCommitted)
                {
                    row.CurrentValue = snapshot.WithElements(dialog.Result, m_service.MessageContext);
                }
            }
            else
            {
                var dialog = new ComplexValueElementDialog(
                    row.Field.Definition.DataType, definition, m_service, row.CurrentValue);
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
                if (dialog.WasCommitted)
                {
                    row.CurrentValue = dialog.Result;
                }
            }
        }
        catch (Exception ex)
        {
            this.RequiredControl<TextBlock>("HintLabel").Text = $"Edit failed: {ex.Message}";
        }
        finally
        {
            m_editCancellation = null;
            m_nestedWindow = null;
        }
    }

    private void ShowError(string message)
    {
        m_error = message;
        m_draft = null;
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

    public static readonly StyledProperty<Variant> ValueProperty =
        AvaloniaProperty.Register<ComplexValueEditor, Variant>(
            nameof(Value), defaultValue: Variant.Null, defaultBindingMode: BindingMode.TwoWay);

    private readonly List<FieldRow> m_rows = [];
    private NodeId m_dataTypeId;
    private DataTypeDefinition? m_definition;
    private IStructuredValueService? m_service;
    private StructuredValueDraft? m_draft;
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
