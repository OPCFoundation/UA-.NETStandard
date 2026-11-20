// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.StateMachine;

namespace UaLens.Plugins.PubSub;

internal sealed class PubSubObservationBinding : IDisposable
{
    public PubSubObservationBinding(IPubSubApplication application, PubSubObservationStore store)
    {
        m_application = application ?? throw new ArgumentNullException(nameof(application));
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_states = EnumerateStates(application);
        foreach (IPubSubConnection connection in application.Connections)
        {
            m_receivers.Add(connection.RegisterReceivedNetworkMessageSink(store));
        }
        application.MetaDataRegistry.MetaDataChanged += OnMetadataChanged;
        foreach (DataSetMetaDataKey key in application.MetaDataRegistry.Keys)
        {
            application.MetaDataRegistry.TryGet(key, out DataSetMetaDataType? metadata);
            if (metadata is not null)
            {
                store.ObserveMetadata(
                    application.MetaDataRegistry, new DataSetMetaDataChangedEventArgs(key, null, metadata));
            }
        }
        foreach (PubSubStateMachine state in m_states)
        {
            state.StateChanged += OnStateChanged;
        }
    }

    public void Dispose()
    {
        m_application.MetaDataRegistry.MetaDataChanged -= OnMetadataChanged;
        foreach (PubSubStateMachine state in m_states)
        {
            state.StateChanged -= OnStateChanged;
        }
        foreach (IDisposable receiver in m_receivers)
        {
            receiver.Dispose();
        }
        m_receivers.Clear();
    }

    public static ArrayOf<PubSubStateMachine> EnumerateStates(IPubSubApplication application)
    {
        var states = new List<PubSubStateMachine> { application.State };
        foreach (IPubSubConnection connection in application.Connections)
        {
            states.Add(connection.State);
            foreach (IWriterGroup group in connection.WriterGroups)
            {
                states.Add(group.State);
                foreach (IDataSetWriter writer in group.DataSetWriters)
                {
                    states.Add(writer.State);
                }
            }
            foreach (IReaderGroup group in connection.ReaderGroups)
            {
                states.Add(group.State);
                foreach (IDataSetReader reader in group.DataSetReaders)
                {
                    states.Add(reader.State);
                }
            }
        }
        return [.. states];
    }

    private void OnMetadataChanged(object? sender, DataSetMetaDataChangedEventArgs args)
    {
        m_store.ObserveMetadata(m_application.MetaDataRegistry, args);
    }

    private void OnStateChanged(object? sender, PubSubStateChangedEventArgs args)
    {
        if (sender is PubSubStateMachine state)
        {
            m_store.RecordEvidence("Component", state.StatusCode,
                string.Create(CultureInfo.InvariantCulture,
                    $"{state.ComponentName}: {state.State}. This is runtime state, not proof of delivery."));
        }
    }

    private readonly IPubSubApplication m_application;
    private readonly PubSubObservationStore m_store;
    private readonly ArrayOf<PubSubStateMachine> m_states;
    private readonly List<IDisposable> m_receivers = [];
}
