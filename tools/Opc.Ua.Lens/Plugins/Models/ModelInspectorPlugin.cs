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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Schema;
using UaLens.Storage;
using UaLens.StructuredValues;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.Models;

internal sealed record ModelSchemaChoice(UaSchemaFormat Format, string Name);

/// <summary>
/// Read-only-first Models document. Only explicit commands perform server I/O;
/// restore and connection rebinding retain intent but discard live evidence and edits.
/// </summary>
internal sealed partial class ModelInspectorPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    public ModelInspectorPlugin(
        PluginHost host,
        IModelInspectorBackend? backend = null,
        IStructuredValueService? values = null,
        ISchemaProvider? schemas = null)
        : this(backend ?? new SessionModelInspectorBackend(values, schemas),
            () => host.Session, () => host.Workspace.SelectedNode,
            RequireHost(host).Telemetry)
    {
        m_browser = host.Browser;
    }

    public ModelInspectorPlugin(
        IModelInspectorBackend backend,
        Func<ISession?> primary,
        Func<NodeViewModel?> selectedNode,
        ITelemetryContext telemetry)
    {
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
        m_primary = primary ?? throw new ArgumentNullException(nameof(primary));
        m_selectedNode = selectedNode ?? throw new ArgumentNullException(nameof(selectedNode));
        ArgumentNullException.ThrowIfNull(telemetry);
        m_log = telemetry.CreateLogger<ModelInspectorPlugin>();
        m_title = string.Create(CultureInfo.InvariantCulture, $"Models {Interlocked.Increment(ref s_number)}");
        NodeViewModel? selected = m_selectedNode();
        if (selected?.NodeClass is NodeClass.Variable or NodeClass.DataType or NodeClass.Method)
        {
            try
            {
                m_target = ModelInspectorStateCodec.NormalizeTarget(
                    selected.NodeId.ToString(), m_primary()?.NamespaceUris);
                m_targetName = selected.Text;
            }
            catch (JsonException)
            {
                m_status = "Connect, then use the selected node to resolve its namespace URI.";
            }
        }
    }

    public PluginKind Kind => PluginKind.Models;
    public bool SupportsDuplicate => false;
    public bool HasCreatedView => m_view is not null;
    public Control? View => m_view ??= new ModelInspectorView { DataContext = this };
    public Control? HeaderToolbar => null;
    public bool CanConfigure => !IsBusy && m_disposal is null && !m_rebinding;
    public bool CanRead => CanConfigure && m_backend.IsBound && !string.IsNullOrWhiteSpace(Target);
    public bool CanPreviewSchema => CanConfigure && m_backend.IsBound && m_inspection?.Definition is not null;
    public bool CanExportSchema => CanConfigure && m_backend.IsBound && m_schema?.Available == true;
    public bool CanEdit => CanConfigure && m_backend.IsBound && m_inspection?.NodeClass == NodeClass.Variable &&
        (ReadState is ModelReadState.Available or ModelReadState.Unavailable) && m_valueEditable;
    public bool CanEditDraft => CanEdit && EditingEnabled;
    public bool CanWrite => CanEdit && EditingEnabled && ConfirmWrite && m_inspection?.CanWrite == true;
    public bool CanCall => CanConfigure && m_backend.IsBound && m_browser is not null &&
        m_inspection is { NodeClass: NodeClass.Method, CanCall: true };
    public bool IsStructuredValue => m_inspection is
        { NodeClass: NodeClass.Variable, Definition: StructureDefinition or EnumDefinition } &&
        (m_inspection.ValueRank == ValueRanks.Scalar || m_inspection.Value.WrappedValue.TypeInfo.IsScalar);

    public ArrayOf<ModelSchemaChoice> SchemaFormats { get; } =
    [
        new(UaSchemaFormat.JsonCompact, "JSON — compact"),
        new(UaSchemaFormat.JsonVerbose, "JSON — verbose"),
        new(UaSchemaFormat.Xsd, "XML schema (XSD)"),
        new(UaSchemaFormat.Bsd, "OPC Binary schema (BSD)")
    ];

    public ModelInspection? Inspection => m_inspection;
    public IStructuredValueService? StructuredValues => m_backend.Values;

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return
        [
            new MenuItem { Header = "Read target", Command = ReadCommand },
            new MenuItem { Header = "Refresh metadata", Command = RefreshMetadataCommand },
            new MenuItem { Header = "Preview schema", Command = PreviewSchemaCommand },
            new MenuItem { Header = "Export schema…", Command = ExportSchemaCommand },
            new MenuItem { Header = "Write prepared value", Command = WriteCommand },
            new MenuItem { Header = "Call method…", Command = CallCommand }
        ];
    }

    public void OnActivated()
    {
        NotifyAvailability();
    }

    public void OnDeactivated()
    {
    }

    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        if (m_disposal is not null)
        {
            return;
        }
        m_rebinding = true;
        NotifyAvailability();
        try
        {
            await StopAsync().ConfigureAwait(true);
            await m_backend.BindAsync(null, CancellationToken.None).ConfigureAwait(true);
            ClearEvidence();
            cancellationToken.ThrowIfCancellationRequested();
            await m_backend.BindAsync(m_primary(), cancellationToken).ConfigureAwait(true);
            IsOffline = !m_backend.IsBound;
            Status = IsOffline
                ? "Offline. Target intent retained; no read or mutation was replayed."
                : "Connected. Select Read or Refresh metadata; no operation was started automatically.";
        }
        catch (Exception ex)
        {
            IsOffline = true;
            ReadState = ModelReadState.Failed;
            Status = $"Models rebind failed: {ex.Message}";
            throw;
        }
        finally
        {
            m_rebinding = false;
            NotifyAvailability();
        }
    }

    public JsonElement CaptureState()
    {
        string target = ModelInspectorStateCodec.NormalizeTarget(
            Target, m_backend.Values?.MessageContext.NamespaceUris);
        return ModelInspectorStateCodec.Capture(new ModelInspectorState(
            ModelInspectorStateCodec.CurrentVersion, Title, target, TargetName, SelectedSchemaFormat.Format));
    }

    public async Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        ModelInspectorState restored = ModelInspectorStateCodec.Restore(state);
        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        ClearEvidence();
        Title = restored.Title;
        Target = restored.Target;
        TargetName = restored.TargetName;
        foreach (ModelSchemaChoice choice in SchemaFormats)
        {
            if (choice.Format == restored.SchemaFormat)
            {
                SelectedSchemaFormat = choice;
                break;
            }
        }
        Status = "Configuration restored read-only. Select Read; "
            + "values, armed writes and live handles were not restored.";
    }

    /// <summary>
    /// Explicit mutation entry point, also usable by a nonvisual adapter. It requires
    /// both local editing intent and confirmation plus fresh backend validation.
    /// </summary>
    public Task WritePreparedValueAsync(Variant value, CancellationToken cancellationToken = default)
    {
        if (!CanWrite || m_inspection is null)
        {
            throw new InvalidOperationException(
                "Read a writable Variable, enable editing and confirm the write first.");
        }
        ModelInspection inspection = m_inspection;
        return RunAsync("Write", async token =>
        {
            StatusCode result = await m_backend.WriteAsync(inspection, value.Copy(), token).ConfigureAwait(true);
            ConfirmWrite = false;
            EditingEnabled = false;
            m_valueEditable = false;
            Status = $"Write confirmed by server: {result}. Read again to obtain fresh value evidence.";
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(m_disposal ??= DisposeCoreAsync());
    }

    internal void SetEditorAvailability(bool editable, string detail)
    {
        m_valueEditable = editable;
        EditorStatus = detail;
        NotifyAvailability();
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private Task UseSelectedAsync(CancellationToken cancellationToken)
    {
        return RunAsync("Select target", token =>
        {
            token.ThrowIfCancellationRequested();
            NodeViewModel? node = m_selectedNode();
            if (node?.NodeClass is not (NodeClass.Variable or NodeClass.DataType or NodeClass.Method))
            {
                throw new InvalidOperationException(
                    "Select a Variable, DataType or Method in the address-space panel.");
            }
            string target = ModelInspectorStateCodec.NormalizeTarget(
                node.NodeId.ToString(), m_primary()?.NamespaceUris);
            m_applyingTarget = true;
            try
            {
                Target = target;
                TargetName = node.Text;
            }
            finally
            {
                m_applyingTarget = false;
            }
            ClearEvidence();
            Status = "Target selected. Read is explicit; no service call was sent.";
            return Task.CompletedTask;
        }, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task ReadAsync(CancellationToken cancellationToken)
    {
        return ReadTargetAsync(false, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task RefreshMetadataAsync(CancellationToken cancellationToken)
    {
        return ReadTargetAsync(true, cancellationToken);
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        Task pending = m_operation;
        try
        {
            m_operationCancellation?.Cancel();
            if (m_view is not null)
            {
                await m_view.StopAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            await pending.ConfigureAwait(true);
            EditingEnabled = false;
            ConfirmWrite = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSchema))]
    private Task PreviewSchemaAsync(CancellationToken cancellationToken)
    {
        ModelInspection inspection = m_inspection ??
            throw new InvalidOperationException("Read a target before generating a schema.");
        return RunAsync("Schema preview", async token =>
        {
            m_schema = await m_backend.CreateSchemaAsync(inspection, SelectedSchemaFormat.Format, token)
                .ConfigureAwait(true);
            SchemaPreview = m_schema.Text;
            SchemaStatus = m_schema.Available
                ? $"Schema available ({m_schema.MediaType}); dependency closure resolved."
                : m_schema.Text;
            Status = m_schema.Available
                ? "Schema preview generated."
                : "Schema unavailable; no opaque schema was exported.";
        }, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanExportSchema))]
    private Task ExportSchemaAsync(CancellationToken cancellationToken)
    {
        return RunAsync("Schema export", async token =>
        {
            ModelInspection inspection = m_inspection ?? throw new InvalidOperationException("Read a target first.");
            ModelSchemaPreview schema = m_schema ?? throw new InvalidOperationException("Preview a schema first.");
            if (!schema.Available)
            {
                throw new InvalidOperationException("An available typed schema preview is required for export.");
            }
            m_backend.EnsureCurrent(inspection);
            Window owner = RequireWindow();
            IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export OPC UA data-type schema",
                SuggestedFileName = $"opc-ua-type.{schema.Extension}",
                DefaultExtension = schema.Extension,
                FileTypeChoices =
                [
                    new FilePickerFileType(schema.MediaType) { Patterns = [$"*.{schema.Extension}"] }
                ]
            }).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            if (file is null)
            {
                throw new OperationCanceledException("Schema export canceled.");
            }
            m_backend.EnsureCurrent(inspection);
            using (file)
            {
                Stream stream = await file.OpenWriteAsync().ConfigureAwait(true);
                await using (stream.ConfigureAwait(false))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(schema.Text);
                    await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                }
            }
            Status = "Schema exported. No variable values or credentials were included.";
        }, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private Task EditValueAsync(CancellationToken cancellationToken)
    {
        return RunAsync("Edit value", async token =>
        {
            ModelInspectorView view = m_view ?? throw new InvalidOperationException("Open the Models view first.");
            await view.EditValueAsync(token).ConfigureAwait(true);
            Status = IsStructuredValue
                ? "Edit the fields in the embedded editor. No server write was sent."
                : "Local draft prepared. Only Write with confirmation sends it to the server.";
        }, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private Task ImportValueAsync(CancellationToken cancellationToken)
    {
        return RunAsync("Import value", async token =>
        {
            ModelInspectorView view = m_view ?? throw new InvalidOperationException("Open the Models view first.");
            await view.ImportAsync(token).ConfigureAwait(true);
            Status = "Value imported into the local draft. No server write was sent.";
        }, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task WriteAsync(CancellationToken cancellationToken)
    {
        Variant value = Variant.Null;
        string? error = null;
        if (m_view is null || !m_view.TryCommit(out value, out error))
        {
            Status = error ?? "The value editor is not available.";
            return Task.CompletedTask;
        }
        return WritePreparedValueAsync(value, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanCall))]
    private Task CallAsync(CancellationToken cancellationToken)
    {
        return RunAsync("Method dialog", async token =>
        {
            ModelInspection inspection = m_inspection ??
                throw new InvalidOperationException("Read method permission metadata first.");
            m_backend.EnsureCurrent(inspection);
            ISession session = m_backend.Session ?? throw new ServiceResultException(StatusCodes.BadNotConnected);
            BrowserViewModel browser = m_browser ??
                throw new InvalidOperationException("A desktop browser context is required for the method dialog.");
            var node = new NodeViewModel(browser, NodeId.Null, inspection.NodeId, inspection.Name, NodeClass.Method);
            var dialog = new MethodCallDialog(node, session, m_backend.Values, token);
            using CancellationTokenRegistration registration = token.Register(
                () => Dispatcher.UIThread.Post(dialog.Close));
            try
            {
                await dialog.ShowDialog(RequireWindow()).ConfigureAwait(true);
            }
            finally
            {
                await dialog.StopAsync().ConfigureAwait(true);
            }
            token.ThrowIfCancellationRequested();
            Status = "Method dialog closed. Its status reports any explicit Call; no call is replayed.";
        }, cancellationToken);
    }

    private Task ReadTargetAsync(bool refresh, CancellationToken cancellationToken)
    {
        return RunAsync(refresh ? "Metadata refresh" : "Read", async token =>
        {
            ClearEvidence();
            string target = ModelInspectorStateCodec.NormalizeTarget(
                Target, m_backend.Values?.MessageContext.NamespaceUris);
            ModelInspection inspection = await m_backend.ReadAsync(target, refresh, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            m_applyingTarget = true;
            try
            {
                Target = target;
                TargetName = inspection.Name;
            }
            finally
            {
                m_applyingTarget = false;
            }
            m_inspection = inspection;
            bool requiresDefinition = inspection.NodeClass != NodeClass.Method &&
                (inspection.NodeClass != NodeClass.Variable ||
                 inspection.Value.WrappedValue.TypeInfo.BuiltInType is BuiltInType.Null or BuiltInType.ExtensionObject);
            ReadState = inspection.Definition is null && requiresDefinition
                ? ModelReadState.Unavailable
                : ModelReadState.Available;
            DefinitionPreview = DescribeDefinition(inspection);
            ValuePreview = inspection.NodeClass == NodeClass.Variable
                ? StructuredScalarValue.FormatValue(inspection.Value.WrappedValue, m_backend.Values!.MessageContext)
                : inspection.NodeClass == NodeClass.Method
                    ? "Use Call… for the existing validated argument dialog."
                    : string.Empty;
            m_valueEditable = inspection.NodeClass == NodeClass.Variable &&
                (inspection.Definition is not null || inspection.Value.WrappedValue.TypeInfo.BuiltInType is
                    not (BuiltInType.Null or BuiltInType.ExtensionObject));
            EditorStatus = "Read-only. Enable editing to prepare an isolated local value.";
            if (inspection.Definition is EnumDefinition { IsOptionSet: true })
            {
                m_valueEditable = false;
                EditorStatus = "OptionSets require a dedicated bit-field editor; they are not encoded as plain enums.";
            }
            if (m_view is not null)
            {
                await m_view.LoadAsync(inspection, m_backend.Values!, token).ConfigureAwait(true);
            }
            Status = ReadState == ModelReadState.Unavailable
                ? "Read completed; the server exposes no definition. Opaque values remain read-only."
                : "Read completed. Metadata and value are evidence from this read, not an automatic live watch.";
            NotifyAvailability();
        }, cancellationToken);
    }

    private Task RunAsync(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (IsBusy || m_rebinding || m_disposal is not null)
        {
            throw new InvalidOperationException("The Models document is busy, rebinding or closed.");
        }
        IsBusy = true;
        Status = $"{operation}…";
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        m_operationCancellation = source;
        m_operation = RunCoreAsync(operation, action, source);
        return m_operation;
    }

    private async Task RunCoreAsync(
        string operation, Func<CancellationToken, Task> action, CancellationTokenSource source)
    {
        try
        {
            await action(source.Token).ConfigureAwait(true);
            source.Token.ThrowIfCancellationRequested();
            m_log.ModelInspectionCompleted(operation);
        }
        catch (OperationCanceledException)
        {
            ReadState = ModelReadState.Canceled;
            ConfirmWrite = false;
            EditingEnabled = false;
            Status = "Canceled. An already-sent mutation may have completed; inspect the server before retrying.";
            m_log.ModelInspectionCanceled(operation);
        }
        catch (Exception ex)
        {
            ReadState = ex is ServiceResultException service &&
                service.StatusCode.CodeBits == StatusCodes.BadUserAccessDenied
                ? ModelReadState.Denied
                : ModelReadState.Failed;
            ConfirmWrite = false;
            EditingEnabled = false;
            Status = $"{ReadState}: {ex.Message}";
            m_log.ModelInspectionFailed(ex, operation);
        }
        finally
        {
            m_operationCancellation = null;
            source.Dispose();
            IsBusy = false;
            NotifyAvailability();
        }
    }

    private async Task DisposeCoreAsync()
    {
        m_rebinding = true;
        try
        {
            await StopAsync().ConfigureAwait(true);
        }
        finally
        {
            m_operationCancellation?.Dispose();
            m_operationCancellation = null;
            await m_backend.DisposeAsync().ConfigureAwait(true);
            ClearEvidence();
            IsOffline = true;
            Status = "Models document closed.";
            NotifyAvailability();
        }
    }

    private void ClearEvidence()
    {
        m_inspection = null;
        m_schema = null;
        m_valueEditable = false;
        EditingEnabled = false;
        ConfirmWrite = false;
        ReadState = ModelReadState.NotRead;
        DefinitionPreview = string.Empty;
        ValuePreview = string.Empty;
        SchemaPreview = string.Empty;
        SchemaStatus = "Select Preview schema after an explicit metadata read.";
        EditorStatus = "No value read.";
        m_view?.Clear();
        NotifyAvailability();
    }

    private static string DescribeDefinition(ModelInspection inspection)
    {
        var text = new StringBuilder()
            .Append(inspection.Name).Append(" (").Append(inspection.NodeClass).AppendLine(")")
            .Append("Target: ").AppendLine(inspection.PortableTarget);
        if (inspection.NodeClass == NodeClass.Method)
        {
            return text.Append("Executable for this user: ").Append(inspection.CanCall).ToString();
        }
        text.Append("DataType: ").Append(inspection.DataTypeName).Append(" — ")
            .AppendLine(inspection.DataType.ToString());
        if (inspection.Definition is StructureDefinition structure)
        {
            text.Append("Kind: ").AppendLine(structure.StructureType.ToString());
            foreach (StructureField field in structure.Fields)
            {
                text.Append(field.Name).Append(": ").Append(field.DataType)
                    .Append(" rank=").Append(field.ValueRank)
                    .Append(field.IsOptional ? " optional/subtyped" : string.Empty).AppendLine();
            }
        }
        else if (inspection.Definition is EnumDefinition enumeration)
        {
            text.AppendLine(enumeration.IsOptionSet ? "OptionSet (read-only limitation)" : "Enumeration");
            foreach (EnumField field in enumeration.Fields)
            {
                text.Append(field.Name).Append(" = ").Append(field.Value).AppendLine();
            }
        }
        else
        {
            text.AppendLine("No DataTypeDefinition exposed. This is not a successful opaque schema.");
        }
        return text.ToString();
    }

    private Window RequireWindow()
    {
        return m_view is not null && TopLevel.GetTopLevel(m_view) is Window owner
            ? owner
            : throw new InvalidOperationException("This command requires the Models desktop view.");
    }

    private static PluginHost RequireHost(PluginHost host)
    {
        return host ?? throw new ArgumentNullException(nameof(host));
    }

    partial void OnTargetChanged(string value)
    {
        if (!m_applyingTarget)
        {
            m_operationCancellation?.Cancel();
            ClearEvidence();
        }
        NotifyAvailability();
    }

    partial void OnSelectedSchemaFormatChanged(ModelSchemaChoice value)
    {
        m_schema = null;
        SchemaPreview = string.Empty;
        SchemaStatus = "Select Preview schema for the requested format.";
        NotifyAvailability();
    }

    partial void OnEditingEnabledChanged(bool value)
    {
        ConfirmWrite = false;
        NotifyAvailability();
    }

    partial void OnConfirmWriteChanged(bool value)
    {
        NotifyAvailability();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyAvailability();
    }

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanPreviewSchema));
        OnPropertyChanged(nameof(CanExportSchema));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditDraft));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanCall));
        OnPropertyChanged(nameof(IsStructuredValue));
        ReadCommand.NotifyCanExecuteChanged();
        RefreshMetadataCommand.NotifyCanExecuteChanged();
        PreviewSchemaCommand.NotifyCanExecuteChanged();
        ExportSchemaCommand.NotifyCanExecuteChanged();
        EditValueCommand.NotifyCanExecuteChanged();
        ImportValueCommand.NotifyCanExecuteChanged();
        WriteCommand.NotifyCanExecuteChanged();
        CallCommand.NotifyCanExecuteChanged();
        UseSelectedCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private string m_title;
    [ObservableProperty]
    private bool m_isRenaming;
    [ObservableProperty]
    private string m_target = string.Empty;
    [ObservableProperty]
    private string m_targetName = string.Empty;
    [ObservableProperty]
    private string m_status = "Choose a target and select Read. Models starts read-only.";
    [ObservableProperty]
    private string m_definitionPreview = string.Empty;
    [ObservableProperty]
    private string m_valuePreview = string.Empty;
    [ObservableProperty]
    private string m_schemaPreview = string.Empty;
    [ObservableProperty]
    private string m_schemaStatus = "Select Preview schema after an explicit metadata read.";
    [ObservableProperty]
    private string m_editorStatus = "No value read.";
    [ObservableProperty]
    private ModelReadState m_readState;
    [ObservableProperty]
    private bool m_isOffline = true;
    [ObservableProperty]
    private bool m_isBusy;
    [ObservableProperty]
    private bool m_editingEnabled;
    [ObservableProperty]
    private bool m_confirmWrite;
    [ObservableProperty]
    private ModelSchemaChoice m_selectedSchemaFormat = new(UaSchemaFormat.JsonCompact, "JSON — compact");

    private readonly IModelInspectorBackend m_backend;
    private readonly Func<ISession?> m_primary;
    private readonly Func<NodeViewModel?> m_selectedNode;
    private readonly ILogger m_log;
    private readonly BrowserViewModel? m_browser;
    private ModelInspectorView? m_view;
    private ModelInspection? m_inspection;
    private ModelSchemaPreview? m_schema;
    private CancellationTokenSource? m_operationCancellation;
    private Task m_operation = Task.CompletedTask;
    private Task? m_disposal;
    private bool m_rebinding;
    private bool m_valueEditable;
    private bool m_applyingTarget;
    private static int s_number;
}

internal static partial class ModelInspectorPluginLog
{
    [LoggerMessage(EventId = UaLensEventIds.ModelsBase, Level = LogLevel.Information,
        Message = "Models operation {Operation} completed.")]
    public static partial void ModelInspectionCompleted(this ILogger logger, string operation);

    [LoggerMessage(EventId = UaLensEventIds.ModelsBase + 1, Level = LogLevel.Information,
        Message = "Models operation {Operation} canceled.")]
    public static partial void ModelInspectionCanceled(this ILogger logger, string operation);

    [LoggerMessage(EventId = UaLensEventIds.ModelsBase + 2, Level = LogLevel.Warning,
        Message = "Models operation {Operation} failed.")]
    public static partial void ModelInspectionFailed(this ILogger logger, Exception exception, string operation);
}
