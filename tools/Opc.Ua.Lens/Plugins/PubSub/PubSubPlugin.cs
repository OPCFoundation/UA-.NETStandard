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
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding.Uadp;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Independent-network workspace document with explicit, non-persisted workload consent.
/// </summary>
internal sealed partial class PubSubPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    public PubSubPlugin(PluginHost host)
        : this(host, new PubSubRuntimeFactory(
            (host ?? throw new ArgumentNullException(nameof(host))).Telemetry))
    {
    }

    public PubSubPlugin(PluginHost host, IPubSubRuntimeFactory runtimeFactory)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_workspace = new PubSubWorkspace(runtimeFactory, host.Telemetry);
        m_title = string.Create(CultureInfo.InvariantCulture, $"PubSub {Interlocked.Increment(ref s_number)}");
        m_refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        m_refreshTimer.Tick += OnRefresh;
        InitializeCommissioning(runtimeFactory);
        LoadConfiguration(m_workspace.Configuration);
        RefreshPresentation();
    }

    public PluginKind Kind => PluginKind.PubSub;

    public bool SupportsDuplicate => false;

    public bool IsOffline => !IsRunning;

    public bool CanConfigure => !IsBusy && !IsRunning;

    public ObservableCollection<PubSubProfile> Profiles { get; } = [];

    public ObservableCollection<PubSubPublication> PublicationModes { get; } =
        new(Enum.GetValues<PubSubPublication>());

    public ObservableCollection<MessageSecurityMode> SecurityModes { get; } =
        [MessageSecurityMode.SignAndEncrypt, MessageSecurityMode.Sign, MessageSecurityMode.None];

    public ObservableCollection<PubSubBrokerAuthentication> AuthenticationModes { get; } =
        new(Enum.GetValues<PubSubBrokerAuthentication>());

    public ObservableCollection<UadpDiscoveryType> DiscoveryTypes { get; } =
    [
        UadpDiscoveryType.DataSetMetaData,
        UadpDiscoveryType.DataSetWriterConfiguration,
        UadpDiscoveryType.PublisherEndpoints
    ];

    public ObservableCollection<PubSubPrerequisite> PrerequisiteRows { get; } = [];

    public ObservableCollection<PubSubFieldValue> Values { get; } = [];

    public ObservableCollection<PubSubMessageRow> Messages { get; } = [];

    public ObservableCollection<PubSubMetadataRow> Metadata { get; } = [];

    public ObservableCollection<PubSubEvidence> Evidence { get; } = [];

    public ObservableCollection<PubSubCounterRow> Counters { get; } = [];

    public ObservableCollection<PubSubComponentRow> Components { get; } = [];

    public ObservableCollection<PubSubDiscoveryRow> Discovery { get; } = [];

    public ObservableCollection<PubSubFieldValue> ActionOutputs { get; } = [];

    Control? IPlugin.View => m_view ??= new PubSubView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public void OnActivated()
    {
        RefreshPresentation();
        m_refreshTimer.Start();
    }

    public void OnDeactivated()
    {
        m_refreshTimer.Stop();
    }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return [];
    }

    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        await m_workspace.BindPrimarySessionAsync(m_host.Session, cancellationToken).ConfigureAwait(true);
        RefreshPresentation();
    }

    public JsonElement CaptureState()
    {
        return PubSubStateCodec.Capture(m_workspace.Configuration);
    }

    public async Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        PubSubConfiguration restored = PubSubStateCodec.Restore(state);
        await m_workspace.ConfigureAsync(restored, cancellationToken).ConfigureAwait(true);
        ClearAuthorizations();
        ClearActionInputs();
        LoadConfiguration(restored);
        RefreshPresentation();
        Status = "Configuration restored offline. No traffic, listener, publication, Action or UA write was resumed.";
    }

    public async ValueTask DisposeAsync()
    {
        m_refreshTimer.Stop();
        m_refreshTimer.Tick -= OnRefresh;
        PropertyChanged -= OnDocumentDraftChanged;
        FieldDrafts.CollectionChanged -= OnFieldCollectionChanged;
        foreach (PubSubFieldDraft field in FieldDrafts)
        {
            field.PropertyChanged -= OnEditorChanged;
        }
        foreach (PubSubMaskOption option in NetworkMaskOptions.Concat(DataSetMaskOptions).Concat(FieldMaskOptions))
        {
            option.PropertyChanged -= OnEditorChanged;
        }
        ClearActionInputs();
        await m_workspace.DisposeAsync().ConfigureAwait(false);
    }

    private bool CanStart()
    {
        return !IsBusy && !IsRunning;
    }

    private bool CanOperate()
    {
        return !IsBusy && IsRunning;
    }

    private bool CanStop()
    {
        return IsBusy || IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private Task ApplyConfigurationAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            ClearAuthorizations();
            PubSubConfiguration draft = CreateDraft();
            await m_workspace.ConfigureAsync(draft, cancellationToken).ConfigureAwait(true);
            ClearAuthorizations();
            LoadConfiguration(draft);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private Task ApplyAdvancedConfigurationAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            ClearAuthorizations();
            PubSubConfiguration draft = PubSubStateCodec.Parse(AdvancedConfiguration);
            await m_workspace.ConfigureAsync(draft, cancellationToken).ConfigureAwait(true);
            ClearAuthorizations();
            LoadConfiguration(draft);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
    private Task StartAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            var authorization = new PubSubStartAuthorization(
                AllowUnsecured, AllowPublication, AllowWriteBack, AllowResponder, AllowAnonymousBroker);
            ClearAuthorizations();
            if (CreateDraft() != m_workspace.Configuration)
            {
                throw new InvalidOperationException(
                    "Apply and review the changed configuration before authorizing Start.");
            }
            await m_workspace.StartAsync(authorization, cancellationToken).ConfigureAwait(true);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStop), FlowExceptionsToTaskScheduler = true)]
    private async Task StopAsync()
    {
        try
        {
            await m_workspace.StopAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (PubSubFailure.IsExpected(exception))
        {
            OperationError = PubSubFailure.Describe(exception);
        }
        finally
        {
            ClearAuthorizations();
            RefreshPresentation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperate), FlowExceptionsToTaskScheduler = true, IncludeCancelCommand = true)]
    private Task DiscoverAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            await m_workspace.DiscoverAsync(DiscoveryType, TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(true);
        });
    }

    [RelayCommand(CanExecute = nameof(CanOperate), FlowExceptionsToTaskScheduler = true)]
    private Task InspectConfigurationAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            await m_workspace.InspectRuntimeConfigurationAsync(cancellationToken).ConfigureAwait(true);
        });
    }

    [RelayCommand(CanExecute = nameof(CanOperate), FlowExceptionsToTaskScheduler = true, IncludeCancelCommand = true)]
    private Task InvokeActionAsync(CancellationToken cancellationToken)
    {
        return PresentAsync(async () =>
        {
            bool authorized = AllowAction;
            AllowAction = false;
            PubSubConfiguration configuration = m_workspace.Configuration;
            await m_workspace.InvokeActionAsync(new PubSubActionRequest
            {
                Target = new PubSubActionTarget
                {
                    DataSetWriterId = configuration.ActionWriterId,
                    ActionTargetId = configuration.ActionTargetId,
                    ActionName = configuration.ActionName
                },
                InputFields = UseAdvancedActionInputs
                    ? PubSubActionInputs.Parse(ActionInputs)
                    : PubSubActionInputs.Create([.. ActionInputDrafts.Select(input => input.ToDraft())]),
                ResponseAddress = configuration.ActionResponseTopic
            }, authorized, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(true);
        });
    }

    private async Task PresentAsync(Func<Task> operation)
    {
        IsBusy = true;
        OperationError = string.Empty;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            OperationError = "The operation was canceled; document cleanup is awaited.";
        }
        catch (Exception exception) when (PubSubFailure.IsExpected(exception))
        {
            OperationError = PubSubFailure.Describe(exception);
        }
        finally
        {
            IsBusy = false;
            RefreshPresentation();
        }
    }

    private PubSubConfiguration CreateDraft()
    {
        return ExtendDraft(m_workspace.Configuration with
        {
            Profile = Profile,
            Endpoint = Endpoint,
            NetworkInterface = NetworkInterface,
            Topic = Topic,
            SecurityMode = SecurityMode,
            SecurityProviderId = SecurityProviderId,
            SecurityGroupId = SecurityGroupId,
            SecurityKeyServiceEndpoint = SecurityKeyServiceEndpoint,
            TransportProviderId = TransportProviderId,
            BrokerAuthentication = BrokerAuthentication,
            CredentialReference = CredentialReference,
            ReceiveEnabled = ReceiveEnabled,
            Publication = Publication,
            PublishingIntervalMs = PublishingIntervalMs,
            DurationSeconds = DurationSeconds,
            MaxPublishedMessages = MaxPublishedMessages,
            LocalPublisherId = LocalPublisherId,
            PublisherFilter = PublisherFilter,
            WriterGroupId = WriterGroupId,
            DataSetWriterId = DataSetWriterId
        });
    }

    private void LoadConfiguration(PubSubConfiguration configuration)
    {
        Profile = configuration.Profile;
        Endpoint = configuration.Endpoint;
        NetworkInterface = configuration.NetworkInterface;
        Topic = configuration.Topic;
        TransportProviderId = configuration.TransportProviderId;
        BrokerAuthentication = configuration.BrokerAuthentication;
        CredentialReference = configuration.CredentialReference;
        SecurityMode = configuration.SecurityMode;
        SecurityProviderId = configuration.SecurityProviderId;
        SecurityGroupId = configuration.SecurityGroupId;
        SecurityKeyServiceEndpoint = configuration.SecurityKeyServiceEndpoint;
        ReceiveEnabled = configuration.ReceiveEnabled;
        Publication = configuration.Publication;
        PublishingIntervalMs = configuration.PublishingIntervalMs;
        DurationSeconds = configuration.DurationSeconds;
        MaxPublishedMessages = configuration.MaxPublishedMessages;
        LocalPublisherId = configuration.LocalPublisherId;
        PublisherFilter = configuration.PublisherFilter;
        WriterGroupId = configuration.WriterGroupId;
        DataSetWriterId = configuration.DataSetWriterId;
        LoadCommissioning(configuration);
        AdvancedConfiguration = PubSubStateCodec.Format(configuration);
        ConfigurationSummary = string.Create(CultureInfo.InvariantCulture,
            $"{configuration.Publication} → {configuration.Endpoint}; {configuration.SecurityMode}; " +
            $"{configuration.PublishingIntervalMs} ms, {configuration.DurationSeconds} s, " +
            $"≤{configuration.MaxPublishedMessages} source samples/UA operations. " +
            $"Local sink: {!configuration.WriteBackEnabled}. Responder: {configuration.ActionResponderEnabled}. " +
            $"Local publisher {PubSubIdentity.Describe(configuration, true)}; " +
            $"received publisher {PubSubIdentity.Describe(configuration, false)}; " +
            $"fields {configuration.Fields.Count}.");
        ActionTarget = string.Create(CultureInfo.InvariantCulture,
            $"Action {configuration.ActionName}; " +
            $"writer {configuration.ActionWriterId}, target {configuration.ActionTargetId}.");
    }

    private void ClearAuthorizations()
    {
        AllowUnsecured = false;
        AllowPublication = false;
        AllowWriteBack = false;
        AllowResponder = false;
        AllowAnonymousBroker = false;
        AllowAction = false;
    }

    private void OnRefresh(object? sender, EventArgs args)
    {
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        PubSubWorkspaceSnapshot snapshot = m_workspace.Snapshot();
        Status = snapshot.Status;
        Phase = snapshot.Phase.ToString();
        IsRunning = snapshot.Phase is PubSubDocumentPhase.Running or
            PubSubDocumentPhase.Starting or PubSubDocumentPhase.Stopping;
        SetItems(PrerequisiteRows, m_workspace.Prerequisites);
        SetItems(Values, snapshot.Observations.Values);
        SetItems(Messages, snapshot.Observations.Messages);
        SetItems(Metadata, snapshot.Observations.Metadata);
        SetItems(Evidence, snapshot.Observations.Evidence);
        SetItems(Counters, snapshot.Counters);
        SetItems(Components, snapshot.Components);
        SetItems(Discovery, snapshot.Discovery);
        SetItems(ActionOutputs, snapshot.Action is { } actionResult ? actionResult.Outputs : []);
        ActionResult = snapshot.Action is { } action
            ? string.Create(CultureInfo.InvariantCulture,
                $"Request {action.RequestId}: {action.Status}, {action.State}; correlation {action.Correlation}")
            : "No Action result. A timeout or missing response is not success.";
        DataSummary = string.Create(CultureInfo.InvariantCulture,
            $"Accepted datasets: {snapshot.Observations.AcceptedDataSets}; " +
            $"rejected locally: {snapshot.Observations.RejectedDataSets}; " +
            $"evicted display messages: {snapshot.Observations.EvictedMessages}; metadata updates/evictions: " +
            $"{snapshot.Observations.MetadataUpdates}/{snapshot.Observations.MetadataEvictions}; " +
            $"truncated/unsupported values: {snapshot.Observations.TruncatedValues}; " +
            $"discovery budget drops: {snapshot.Observations.DiscoveryDrops}; " +
            $"source samples: {snapshot.SourceSamples}.");
        ObserveCommandFailures();
    }

    private void ObserveCommandFailures()
    {
        ArrayOf<IAsyncRelayCommand> commands =
        [
            StartCommand, StopCommand, DiscoverCommand, InspectConfigurationCommand,
            InvokeActionCommand, ApplyConfigurationCommand, ApplyAdvancedConfigurationCommand,
            ApplyPresetCommand, ApplyMetadataCommand, ImportFileCommand, ExportFileCommand
        ];
        foreach (IAsyncRelayCommand command in commands)
        {
            if (command.ExecutionTask is { IsFaulted: true } task &&
                (!m_observedFailures.TryGetValue(command, out Task? previous) || !ReferenceEquals(previous, task)))
            {
                _ = task.Exception;
                m_observedFailures[command] = task;
                OperationError =
                    "The operation failed unexpectedly. Resources remain document-owned; no retry is automatic.";
            }
        }
    }

    private static void SetItems<T>(ObservableCollection<T> target, ArrayOf<T> source)
    {
        bool unchanged = target.Count == source.Count;
        for (int i = 0; unchanged && i < source.Count; i++)
        {
            unchanged = EqualityComparer<T>.Default.Equals(target[i], source[i]);
        }
        if (unchanged)
        {
            return;
        }
        target.Clear();
        foreach (T item in source)
        {
            target.Add(item);
        }
    }

    private readonly PluginHost m_host;
    private readonly PubSubWorkspace m_workspace;
    private readonly DispatcherTimer m_refreshTimer;
    private readonly Dictionary<IAsyncRelayCommand, Task> m_observedFailures = [];
    private PubSubView? m_view;
    private static int s_number;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(ApplyConfigurationCommand),
        nameof(ApplyAdvancedConfigurationCommand), nameof(StopCommand), nameof(DiscoverCommand),
        nameof(InspectConfigurationCommand), nameof(InvokeActionCommand), nameof(ApplyPresetCommand),
        nameof(ApplyMetadataCommand), nameof(AddFieldCommand), nameof(RemoveFieldCommand),
        nameof(MoveFieldUpCommand), nameof(MoveFieldDownCommand), nameof(ValidateConfigurationCommand),
        nameof(ImportFileCommand), nameof(ExportFileCommand))]
    private bool m_isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffline), nameof(CanConfigure))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(ApplyConfigurationCommand),
        nameof(ApplyAdvancedConfigurationCommand), nameof(StopCommand), nameof(DiscoverCommand),
        nameof(InspectConfigurationCommand), nameof(InvokeActionCommand), nameof(ApplyPresetCommand),
        nameof(ApplyMetadataCommand), nameof(AddFieldCommand), nameof(RemoveFieldCommand),
        nameof(MoveFieldUpCommand), nameof(MoveFieldDownCommand), nameof(ValidateConfigurationCommand),
        nameof(ImportFileCommand), nameof(ExportFileCommand))]
    private bool m_isRunning;

    [ObservableProperty]
    private PubSubProfile m_profile;

    [ObservableProperty]
    private string m_endpoint = string.Empty;

    [ObservableProperty]
    private string m_networkInterface = string.Empty;

    [ObservableProperty]
    private string m_topic = string.Empty;

    [ObservableProperty]
    private string m_transportProviderId = string.Empty;

    [ObservableProperty]
    private PubSubBrokerAuthentication m_brokerAuthentication;

    [ObservableProperty]
    private string m_credentialReference = string.Empty;

    [ObservableProperty]
    private MessageSecurityMode m_securityMode = MessageSecurityMode.SignAndEncrypt;

    [ObservableProperty]
    private string m_securityProviderId = string.Empty;

    [ObservableProperty]
    private string m_securityGroupId = string.Empty;

    [ObservableProperty]
    private string m_securityKeyServiceEndpoint = string.Empty;

    [ObservableProperty]
    private bool m_receiveEnabled = true;

    [ObservableProperty]
    private PubSubPublication m_publication;

    [ObservableProperty]
    private int m_publishingIntervalMs = 1000;

    [ObservableProperty]
    private int m_durationSeconds = 30;

    [ObservableProperty]
    private int m_maxPublishedMessages = 100;

    [ObservableProperty]
    private ulong m_localPublisherId = 2;

    [ObservableProperty]
    private ulong m_publisherFilter = 1;

    [ObservableProperty]
    private ushort m_writerGroupId = 100;

    [ObservableProperty]
    private ushort m_dataSetWriterId = 1;

    [ObservableProperty]
    private bool m_allowUnsecured;

    [ObservableProperty]
    private bool m_allowPublication;

    [ObservableProperty]
    private bool m_allowWriteBack;

    [ObservableProperty]
    private bool m_allowResponder;

    [ObservableProperty]
    private bool m_allowAnonymousBroker;

    [ObservableProperty]
    private bool m_allowAction;

    [ObservableProperty]
    private UadpDiscoveryType m_discoveryType = UadpDiscoveryType.DataSetMetaData;

    [ObservableProperty]
    private string m_advancedConfiguration = string.Empty;

    [ObservableProperty]
    private string m_configurationSummary = string.Empty;

    [ObservableProperty]
    private string m_operationError = string.Empty;

    [ObservableProperty]
    private string m_phase = "Offline";

    [ObservableProperty]
    private string m_dataSummary = string.Empty;

    [ObservableProperty]
    private string m_actionTarget = string.Empty;

    [ObservableProperty]
    private string m_actionInputs = "[]";

    [ObservableProperty]
    private string m_actionResult = string.Empty;

}
