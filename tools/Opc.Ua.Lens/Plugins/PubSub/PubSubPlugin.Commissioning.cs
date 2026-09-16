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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace UaLens.Plugins.PubSub;

internal sealed partial class PubSubPlugin
{
    public ObservableCollection<PubSubPreset> Presets { get; } = [];

    public ObservableCollection<PubSubTransportChoice> TransportChoices { get; } = [];

    public ObservableCollection<PubSubKeyProviderChoice> KeyProviders { get; } = [];

    public ObservableCollection<PubSubAdapterChoice> AdapterProviders { get; } = [];

    public ObservableCollection<PubSubFieldDraft> FieldDrafts { get; } = [];

    public ObservableCollection<PubSubActionInputEditor> ActionInputDrafts { get; } = [];

    public ObservableCollection<PubSubMaskOption> NetworkMaskOptions { get; } = [];

    public ObservableCollection<PubSubMaskOption> DataSetMaskOptions { get; } = [];

    public ObservableCollection<PubSubMaskOption> FieldMaskOptions { get; } = [];

    public ObservableCollection<PubSubPrerequisite> DraftValidation { get; } = [];

    public ObservableCollection<PublisherIdType> PublisherIdTypes { get; } = new(Enum.GetValues<PublisherIdType>());

    public ObservableCollection<PubSubKeySource> KeySources { get; } = new(Enum.GetValues<PubSubKeySource>());

    public bool UsesTextLocalPublisher => LocalPublisherIdType is PublisherIdType.String or PublisherIdType.Guid;

    public bool UsesTextPublisherFilter => PublisherFilterType is PublisherIdType.String or PublisherIdType.Guid;

    public async Task ImportConfigurationAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!CanConfigure)
        {
            throw new InvalidOperationException("Stop the workload and finish the active operation before importing.");
        }
        int revision = Volatile.Read(ref m_draftVersion);
        PubSubConfiguration original = m_workspace.Configuration;
        PubSubConfiguration configuration = await PubSubConfigurationFiles.ReadAsync(stream, cancellationToken)
            .ConfigureAwait(true);
        if (!CanConfigure || revision != Volatile.Read(ref m_draftVersion) ||
            !ReferenceEquals(original, m_workspace.Configuration))
        {
            throw new InvalidOperationException("The document changed while the configuration was being read.");
        }
        await m_workspace.ConfigureAsync(configuration, cancellationToken).ConfigureAwait(true);
        ClearAuthorizations();
        ClearActionInputs();
        LoadConfiguration(configuration);
        RefreshPresentation();
    }

    public Task ExportConfigurationAsync(string localPath, CancellationToken cancellationToken = default)
    {
        return PubSubConfigurationFiles.WriteAsync(localPath, m_workspace.Configuration, cancellationToken);
    }

    private void InitializeCommissioning(IPubSubRuntimeFactory factory)
    {
        PubSubProviderCatalog catalog = factory is IPubSubCommissioningCatalog commissioning
            ? commissioning.Catalog
            : PubSubProviderCatalog.Empty;
        SetItems(TransportChoices, catalog.Transports);
        SetItems(KeyProviders, catalog.Keys);
        SetItems(AdapterProviders, catalog.Adapters);
        SetItems(Presets, PubSubPresets.Create(catalog));
        SetItems(Profiles, [.. catalog.Transports.ToList().Select(choice => choice.Profile).Distinct()]);
        PropertyChanged += OnDocumentDraftChanged;
        FieldDrafts.CollectionChanged += OnFieldCollectionChanged;
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private Task ApplyPresetAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            PubSubPreset preset = SelectedPreset ?? throw new InvalidOperationException("Select a registered preset.");
            if (!Presets.Contains(preset))
            {
                throw new InvalidOperationException("The selected preset is not registered.");
            }
            await m_workspace.ConfigureAsync(preset.Configuration, cancellationToken).ConfigureAwait(true);
            ClearAuthorizations();
            ClearActionInputs();
            LoadConfiguration(preset.Configuration);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private Task ApplyMetadataAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            PubSubMetadataSchema schema = SelectedMetadata?.Schema ??
                throw new InvalidOperationException("Select retained metadata with a commissioning schema.");
            PubSubConfiguration configuration = schema.Apply(CreateDraft());
            await m_workspace.ConfigureAsync(configuration, cancellationToken).ConfigureAwait(true);
            ClearAuthorizations();
            ClearActionInputs();
            LoadConfiguration(configuration);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ValidateConfiguration()
    {
        OperationError = string.Empty;
        try
        {
            SetItems(DraftValidation, m_workspace.InspectConfiguration(CreateDraft()));
        }
        catch (ArgumentException)
        {
            SetItems(DraftValidation,
            [
                new PubSubPrerequisite("Draft", PubSubReadiness.RequiresConfiguration,
                    "Enter canonical dataset/field UUIDs and bounded values before validating the configuration.")
            ]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void AddField()
    {
        if (FieldDrafts.Count >= PubSubConfigurationValidation.MaxFields)
        {
            OperationError = "At most 32 scalar dataset fields are supported.";
            return;
        }
        var field = new PubSubFieldDraft(new PubSubFieldConfiguration
        {
            Name = NextName("Field", FieldDrafts.Select(value => value.Name))
        });
        field.PropertyChanged += OnEditorChanged;
        FieldDrafts.Add(field);
        SelectedField = field;
        ClearAuthorizations();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void RemoveField()
    {
        if (SelectedField is null || !FieldDrafts.Contains(SelectedField))
        {
            OperationError = "Select a dataset field to remove.";
            return;
        }
        if (FieldDrafts.Count == 1)
        {
            OperationError = "At least one dataset field is required.";
            return;
        }
        SelectedField.PropertyChanged -= OnEditorChanged;
        FieldDrafts.Remove(SelectedField);
        SelectedField = FieldDrafts[0];
        ClearAuthorizations();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void MoveFieldUp()
    {
        MoveField(-1);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void MoveFieldDown()
    {
        MoveField(1);
    }

    [RelayCommand]
    private void AddActionInput()
    {
        if (IsBusy || ActionInputDrafts.Count >= PubSubActionInputs.MaxInputs)
        {
            OperationError = "Finish the current operation; at most 16 scalar Action inputs are supported.";
            return;
        }
        var input = new PubSubActionInputEditor(NextName("Input", ActionInputDrafts.Select(value => value.Name)));
        input.PropertyChanged += OnActionInputChanged;
        ActionInputDrafts.Add(input);
        SelectedActionInput = input;
        AllowAction = false;
    }

    [RelayCommand]
    private void RemoveActionInput()
    {
        if (IsBusy || SelectedActionInput is null || !ActionInputDrafts.Contains(SelectedActionInput))
        {
            OperationError = "Finish the current operation and select an Action input to remove.";
            return;
        }
        SelectedActionInput.PropertyChanged -= OnActionInputChanged;
        ActionInputDrafts.Remove(SelectedActionInput);
        SelectedActionInput = ActionInputDrafts.FirstOrDefault();
        AllowAction = false;
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private async Task ImportFileAsync(CancellationToken cancellationToken)
    {
        OperationError = string.Empty;
        try
        {
            IStorageProvider storage = TopLevel.GetTopLevel(m_view)?.StorageProvider ??
                throw new InvalidOperationException("A desktop file picker is required.");
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import PubSub configuration (offline)",
                AllowMultiple = false,
                FileTypeFilter = [s_configurationFileType]
            }).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count == 0)
            {
                return;
            }
            using IStorageFile file = files[0];
            Stream stream = await file.OpenReadAsync().ConfigureAwait(true);
            await using (stream.ConfigureAwait(true))
            {
                await ImportConfigurationAsync(stream, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            OperationError = "Configuration import was canceled; no runtime was started.";
        }
        catch (Exception exception) when (PubSubFailure.IsExpected(exception))
        {
            OperationError = PubSubFailure.Describe(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private Task ExportFileAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            IStorageProvider storage = TopLevel.GetTopLevel(m_view)?.StorageProvider ??
                throw new InvalidOperationException("A desktop file picker is required.");
            using IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export applied PubSub configuration",
                SuggestedFileName = "pubsub-configuration.json",
                DefaultExtension = "json",
                FileTypeChoices = [s_configurationFileType]
            }).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (file is null)
            {
                return;
            }
            string path = file.TryGetLocalPath() ??
                throw new NotSupportedException("Atomic configuration export requires a local filesystem destination.");
            await ExportConfigurationAsync(path, cancellationToken).ConfigureAwait(true);
        });
    }

    private void MoveField(int offset)
    {
        int index = SelectedField is null ? -1 : FieldDrafts.IndexOf(SelectedField);
        if (index < 0 || index + offset < 0 || index + offset >= FieldDrafts.Count)
        {
            OperationError = "Select a dataset field that can move in this direction.";
            return;
        }
        FieldDrafts.Move(index, index + offset);
        ClearAuthorizations();
    }

    private void LoadCommissioning(PubSubConfiguration configuration)
    {
        m_loadingDraft = true;
        try
        {
            SelectedTransport = TransportChoices.FirstOrDefault(choice =>
                choice.Profile == configuration.Profile &&
                choice.ProviderId == (configuration.TransportProviderId.Length == 0
                    ? "builtin"
                    : configuration.TransportProviderId));
            SelectedKeyProvider = KeyProviders.FirstOrDefault(provider =>
                provider.Id == configuration.SecurityProviderId);
            SelectedAdapterProvider = AdapterProviders.FirstOrDefault(provider =>
                provider.Id == configuration.AdapterProviderId);
            LocalPublisherIdType = configuration.LocalPublisherIdType;
            PublisherFilterType = configuration.PublisherFilterType;
            LocalPublisherName = configuration.LocalPublisherName;
            PublisherFilterName = configuration.PublisherFilterName;
            KeySource = configuration.KeySource;
            DataSetClassId = configuration.DataSetClassId == Guid.Empty
                ? string.Empty : configuration.DataSetClassId.ToString("D");
            MetadataMajorVersion = configuration.MetadataMajorVersion;
            MetadataMinorVersion = configuration.MetadataMinorVersion;
            RetainedMessages = configuration.RetainedMessages;
            MaxNetworkMessageBytes = configuration.MaxNetworkMessageBytes;
            MulticastLoopback = configuration.MulticastLoopback;
            RawDataEncoding = configuration.RawDataEncoding;
            AdapterProviderId = configuration.AdapterProviderId;
            WriteBackEnabled = configuration.WriteBackEnabled;
            ActionResponderEnabled = configuration.ActionResponderEnabled;
            ActionWriterId = configuration.ActionWriterId;
            ActionTargetId = configuration.ActionTargetId;
            ActionName = configuration.ActionName;
            ActionObjectNodeId = configuration.ActionObjectNodeId;
            ActionMethodNodeId = configuration.ActionMethodNodeId;
            ActionResponseTopic = configuration.ActionResponseTopic;
            foreach (PubSubFieldDraft field in FieldDrafts)
            {
                field.PropertyChanged -= OnEditorChanged;
            }
            FieldDrafts.Clear();
            foreach (PubSubFieldConfiguration field in configuration.Fields)
            {
                var draft = new PubSubFieldDraft(field);
                draft.PropertyChanged += OnEditorChanged;
                FieldDrafts.Add(draft);
            }
            SelectedField = FieldDrafts.FirstOrDefault();
            LoadMaskOptions(configuration);
        }
        finally
        {
            m_loadingDraft = false;
        }
    }

    private PubSubConfiguration ExtendDraft(PubSubConfiguration configuration)
    {
        if (DataSetClassId.Length > 0 && !Guid.TryParseExact(DataSetClassId, "D", out _))
        {
            throw new ArgumentException("Use a canonical dataset class UUID or leave it empty.");
        }
        ArrayOf<PubSubFieldConfiguration> fields = [.. FieldDrafts.Select(field => field.ToConfiguration())];
        if (fields.Span.SequenceEqual(configuration.Fields.Span))
        {
            fields = configuration.Fields;
        }
        return configuration with
        {
            LocalPublisherIdType = LocalPublisherIdType,
            PublisherFilterType = PublisherFilterType,
            LocalPublisherName = LocalPublisherName,
            PublisherFilterName = PublisherFilterName,
            KeySource = KeySource,
            DataSetClassId = DataSetClassId.Length == 0 ? Guid.Empty : Guid.ParseExact(DataSetClassId, "D"),
            MetadataMajorVersion = MetadataMajorVersion,
            MetadataMinorVersion = MetadataMinorVersion,
            RetainedMessages = RetainedMessages,
            MaxNetworkMessageBytes = MaxNetworkMessageBytes,
            MulticastLoopback = MulticastLoopback,
            RawDataEncoding = RawDataEncoding,
            UadpNetworkMask = configuration.IsJson
                ? configuration.UadpNetworkMask : (UadpNetworkMessageContentMask)SelectedMask(NetworkMaskOptions),
            UadpDataSetMask = configuration.IsJson
                ? configuration.UadpDataSetMask : (UadpDataSetMessageContentMask)SelectedMask(DataSetMaskOptions),
            JsonNetworkMask = configuration.IsJson
                ? (JsonNetworkMessageContentMask)SelectedMask(NetworkMaskOptions) : configuration.JsonNetworkMask,
            JsonDataSetMask = configuration.IsJson
                ? (JsonDataSetMessageContentMask)SelectedMask(DataSetMaskOptions) : configuration.JsonDataSetMask,
            FieldContentMask = (DataSetFieldContentMask)SelectedMask(FieldMaskOptions),
            AdapterProviderId = AdapterProviderId,
            WriteBackEnabled = WriteBackEnabled,
            ActionResponderEnabled = ActionResponderEnabled,
            ActionWriterId = ActionWriterId,
            ActionTargetId = ActionTargetId,
            ActionName = ActionName,
            ActionObjectNodeId = ActionObjectNodeId,
            ActionMethodNodeId = ActionMethodNodeId,
            ActionResponseTopic = ActionResponseTopic,
            Fields = fields
        };
    }

    private void LoadMaskOptions(PubSubConfiguration configuration)
    {
        ReplaceMaskOptions(NetworkMaskOptions, configuration.IsJson
            ? CreateOptions(Enum.GetValues<JsonNetworkMessageContentMask>()
                .Select(flag => (flag.ToString(), (uint)flag)), (uint)configuration.JsonNetworkMask,
                (uint)PubSubContentMasks.AllowedJsonNetwork)
            : CreateUadpNetworkOptions(configuration));
        ReplaceMaskOptions(DataSetMaskOptions, configuration.IsJson
            ? CreateOptions(Enum.GetValues<JsonDataSetMessageContentMask>()
                .Select(flag => (flag.ToString(), (uint)flag)), (uint)configuration.JsonDataSetMask,
                (uint)PubSubContentMasks.DefaultJsonDataSet)
            : CreateOptions(Enum.GetValues<UadpDataSetMessageContentMask>()
                .Select(flag => (flag.ToString(), (uint)flag)), (uint)configuration.UadpDataSetMask,
                (uint)PubSubContentMasks.AllowedUadpDataSet));
        ReplaceMaskOptions(FieldMaskOptions, CreateOptions(Enum.GetValues<DataSetFieldContentMask>()
            .Select(flag => (flag.ToString(), (uint)flag)), (uint)configuration.FieldContentMask,
            (uint)PubSubContentMasks.AllowedField));
    }

    private void ReplaceMaskOptions(
        ObservableCollection<PubSubMaskOption> options, ArrayOf<PubSubMaskOption> replacement)
    {
        foreach (PubSubMaskOption option in options)
        {
            option.PropertyChanged -= OnEditorChanged;
        }
        SetItems(options, replacement);
        foreach (PubSubMaskOption option in options)
        {
            option.PropertyChanged += OnEditorChanged;
        }
    }

    private void ClearActionInputs()
    {
        ActionInputs = "[]";
        UseAdvancedActionInputs = false;
        foreach (PubSubActionInputEditor input in ActionInputDrafts)
        {
            input.PropertyChanged -= OnActionInputChanged;
        }
        ActionInputDrafts.Clear();
        SelectedActionInput = null;
    }

    private void OnDocumentDraftChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!m_loadingDraft && args.PropertyName is not null &&
            s_configurationProperties.Contains(args.PropertyName))
        {
            Interlocked.Increment(ref m_draftVersion);
            ClearAuthorizations();
            DraftValidation.Clear();
        }
        if (args.PropertyName is nameof(ActionInputs) or nameof(UseAdvancedActionInputs))
        {
            AllowAction = false;
        }
        if (args.PropertyName == nameof(IsRunning))
        {
            Interlocked.Increment(ref m_draftVersion);
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!m_loadingDraft)
        {
            Interlocked.Increment(ref m_draftVersion);
            ClearAuthorizations();
            DraftValidation.Clear();
        }
    }

    private void OnFieldCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!m_loadingDraft)
        {
            Interlocked.Increment(ref m_draftVersion);
            ClearAuthorizations();
            DraftValidation.Clear();
        }
    }

    private void OnActionInputChanged(object? sender, PropertyChangedEventArgs args)
    {
        AllowAction = false;
    }

    partial void OnSelectedTransportChanged(PubSubTransportChoice? value)
    {
        if (!m_loadingDraft && value is not null && TransportChoices.Contains(value))
        {
            Profile = value.Profile;
            TransportProviderId = value.ProviderId;
        }
    }

    partial void OnSelectedKeyProviderChanged(PubSubKeyProviderChoice? value)
    {
        if (!m_loadingDraft && value is not null && KeyProviders.Contains(value))
        {
            SecurityProviderId = value.Id;
            KeySource = value.Source;
            SecurityKeyServiceEndpoint = value.Endpoint;
        }
    }

    partial void OnSelectedAdapterProviderChanged(PubSubAdapterChoice? value)
    {
        if (!m_loadingDraft && value is not null && AdapterProviders.Contains(value))
        {
            AdapterProviderId = value.Id;
        }
    }

    partial void OnProfileChanged(PubSubProfile value)
    {
        if (!m_loadingDraft && NetworkMaskOptions.Count > 0)
        {
            LoadMaskOptions(m_workspace.Configuration with { Profile = value });
        }
    }

    private static ArrayOf<PubSubMaskOption> CreateUadpNetworkOptions(PubSubConfiguration configuration)
    {
        return CreateOptions(Enum.GetValues<UadpNetworkMessageContentMask>()
            .Select(flag => (flag.ToString(), (uint)flag)), (uint)configuration.UadpNetworkMask,
            (uint)PubSubContentMasks.AllowedUadpNetwork);
    }

    private static ArrayOf<PubSubMaskOption> CreateOptions(
        IEnumerable<(string Name, uint Flag)> flags, uint selected, uint allowed)
    {
        return [.. flags.Where(flag => flag.Flag != 0 && (flag.Flag & (flag.Flag - 1)) == 0 &&
            (flag.Flag & allowed) == flag.Flag).Select(flag => new PubSubMaskOption(flag.Name, flag.Flag, selected))];
    }

    private static uint SelectedMask(ObservableCollection<PubSubMaskOption> options)
    {
        uint mask = 0;
        foreach (PubSubMaskOption option in options)
        {
            if (option.IsSelected)
            {
                mask |= option.Flag;
            }
        }
        return mask;
    }

    private static string NextName(string prefix, IEnumerable<string> existing)
    {
        var names = new HashSet<string>(existing, StringComparer.Ordinal);
        for (int i = 1; ; i++)
        {
            string candidate = prefix + i.ToString(CultureInfo.InvariantCulture);
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private bool m_loadingDraft;
    private int m_draftVersion;

    [ObservableProperty]
    private PubSubPreset? m_selectedPreset;

    [ObservableProperty]
    private PubSubTransportChoice? m_selectedTransport;

    [ObservableProperty]
    private PubSubKeyProviderChoice? m_selectedKeyProvider;

    [ObservableProperty]
    private PubSubAdapterChoice? m_selectedAdapterProvider;

    [ObservableProperty]
    private PubSubMetadataRow? m_selectedMetadata;

    [ObservableProperty]
    private PubSubFieldDraft? m_selectedField;

    [ObservableProperty]
    private PubSubActionInputEditor? m_selectedActionInput;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesTextLocalPublisher))]
    private PublisherIdType m_localPublisherIdType = PublisherIdType.UInt16;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesTextPublisherFilter))]
    private PublisherIdType m_publisherFilterType = PublisherIdType.UInt16;

    [ObservableProperty]
    private string m_localPublisherName = string.Empty;

    [ObservableProperty]
    private string m_publisherFilterName = string.Empty;

    [ObservableProperty]
    private PubSubKeySource m_keySource;

    [ObservableProperty]
    private string m_dataSetClassId = string.Empty;

    [ObservableProperty]
    private uint m_metadataMajorVersion = 1;

    [ObservableProperty]
    private uint m_metadataMinorVersion;

    [ObservableProperty]
    private int m_retainedMessages = 128;

    [ObservableProperty]
    private int m_maxNetworkMessageBytes = 1500;

    [ObservableProperty]
    private bool m_multicastLoopback;

    [ObservableProperty]
    private bool m_rawDataEncoding;

    [ObservableProperty]
    private string m_adapterProviderId = string.Empty;

    [ObservableProperty]
    private bool m_writeBackEnabled;

    [ObservableProperty]
    private bool m_actionResponderEnabled;

    [ObservableProperty]
    private ushort m_actionWriterId = 2;

    [ObservableProperty]
    private ushort m_actionTargetId = 1;

    [ObservableProperty]
    private string m_actionName = "SampleAction";

    [ObservableProperty]
    private string m_actionObjectNodeId = string.Empty;

    [ObservableProperty]
    private string m_actionMethodNodeId = string.Empty;

    [ObservableProperty]
    private string m_actionResponseTopic = string.Empty;

    [ObservableProperty]
    private bool m_useAdvancedActionInputs;

    private static readonly FilePickerFileType s_configurationFileType = new("PubSub configuration JSON")
    {
        Patterns = ["*.json"]
    };

    private static readonly HashSet<string> s_configurationProperties =
    [
        nameof(Profile), nameof(Endpoint), nameof(NetworkInterface), nameof(Topic), nameof(TransportProviderId),
        nameof(BrokerAuthentication), nameof(CredentialReference), nameof(SecurityMode), nameof(SecurityProviderId),
        nameof(SecurityGroupId), nameof(SecurityKeyServiceEndpoint), nameof(KeySource), nameof(ReceiveEnabled),
        nameof(Publication), nameof(PublishingIntervalMs), nameof(DurationSeconds), nameof(MaxPublishedMessages),
        nameof(LocalPublisherId), nameof(PublisherFilter), nameof(LocalPublisherIdType), nameof(PublisherFilterType),
        nameof(LocalPublisherName), nameof(PublisherFilterName), nameof(WriterGroupId), nameof(DataSetWriterId),
        nameof(DataSetClassId), nameof(MetadataMajorVersion), nameof(MetadataMinorVersion), nameof(RetainedMessages),
        nameof(MaxNetworkMessageBytes), nameof(MulticastLoopback), nameof(RawDataEncoding), nameof(AdapterProviderId),
        nameof(WriteBackEnabled), nameof(ActionResponderEnabled), nameof(ActionWriterId), nameof(ActionTargetId),
        nameof(ActionName), nameof(ActionObjectNodeId), nameof(ActionMethodNodeId), nameof(ActionResponseTopic),
        nameof(AdvancedConfiguration)
    ];
}
