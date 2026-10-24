/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>A verified active PubSub Object and its native configuration selection.</summary>
    public sealed class PubSubAddressSpaceTarget
    {
        internal PubSubAddressSpaceTarget(
            BaseObjectState node,
            PubSubConnectionDataType connection,
            WriterGroupDataType? group,
            DataSetWriterDataType? writer,
            DataSetReaderDataType? reader,
            NamespaceTable namespaceUris)
        {
            Node = node;
            Source = NodeId.ToExpandedNodeId(node.NodeId, namespaceUris);
            m_connection = connection;
            m_group = group;
            m_writer = writer;
            m_reader = reader;
        }

        /// <summary>Gets the real server-owned Object. Its identity is not derived by consumers.</summary>
        public BaseObjectState Node { get; }

        /// <summary>Gets the portable identity established by the owning server's namespace table.</summary>
        public ExpandedNodeId Source { get; }

        /// <summary>Returns the native connection snapshot.</summary>
        public PubSubConnectionDataType Connection => (PubSubConnectionDataType)m_connection.Clone();

        /// <summary>Returns the selected WriterGroup, if this is a publisher.</summary>
        public WriterGroupDataType? WriterGroup => (WriterGroupDataType?)m_group?.Clone();

        /// <summary>Returns the selected writer, if this is a DataSetWriter Object.</summary>
        public DataSetWriterDataType? Writer => (DataSetWriterDataType?)m_writer?.Clone();

        /// <summary>Returns the selected reader, if this is a DataSetReader Object.</summary>
        public DataSetReaderDataType? Reader => (DataSetReaderDataType?)m_reader?.Clone();

        private readonly PubSubConnectionDataType m_connection;
        private readonly WriterGroupDataType? m_group;
        private readonly DataSetWriterDataType? m_writer;
        private readonly DataSetReaderDataType? m_reader;
    }

    /// <summary>
    /// One coherent server-owned PubSub configuration/address-space generation.
    /// Objects are valid only between activation and retirement notifications.
    /// </summary>
    public sealed class PubSubConfigurationView
    {
        internal PubSubConfigurationView(PubSubConfigurationDataType configuration, ArrayOf<PubSubAddressSpaceTarget> targets)
        {
            m_configuration = (PubSubConfigurationDataType)configuration.Clone();
            Targets = targets;
        }

        /// <summary>Returns a private native configuration snapshot.</summary>
        public PubSubConfigurationDataType Configuration => (PubSubConfigurationDataType)m_configuration.Clone();

        /// <summary>Gets the actual active WriterGroup, writer and reader Objects.</summary>
        public ArrayOf<PubSubAddressSpaceTarget> Targets { get; }

        private readonly PubSubConfigurationDataType m_configuration;
    }

    /// <summary>Participates in the serialized lifetime of real PubSub address-space Objects.</summary>
    public interface IPubSubConfigurationViewObserver
    {
        /// <summary>Invalidates every association before the old runtime or Objects retire.</summary>
        ValueTask RetiringAsync(CancellationToken cancellationToken);

        /// <summary>Publishes associations only after all new Objects are registered.</summary>
        ValueTask ActivatedAsync(PubSubConfigurationView view, CancellationToken cancellationToken);
    }

    public sealed partial class PubSubNodeManager
    {
        /// <summary>Gets the application whose configuration backs the exposed Objects.</summary>
        public IPubSubApplication Application => m_application;

        /// <summary>Gets the active view, or null during retirement or before initialization.</summary>
        public PubSubConfigurationView? ConfigurationView => Volatile.Read(ref m_configurationView);

        /// <summary>
        /// Attaches an observer and replays the active generation under the same gate as retirement.
        /// The observer must not reenter this manager or mutate its application.
        /// </summary>
        public async ValueTask AddViewObserverAsync(
            IPubSubConfigurationViewObserver observer, CancellationToken cancellationToken = default)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            await m_viewGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_viewObservers.Contains(observer))
                {
                    throw new InvalidOperationException("The view observer is already attached.");
                }
                m_viewObservers.Add(observer);
                if (m_configurationView is { } view)
                {
                    await observer.ActivatedAsync(view, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_viewGate.Release();
            }
        }

        /// <summary>Detaches an observer after invalidating its associations.</summary>
        public async ValueTask RemoveViewObserverAsync(
            IPubSubConfigurationViewObserver observer, CancellationToken cancellationToken = default)
        {
            await m_viewGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_viewObservers.Remove(observer);
                await observer.RetiringAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_viewGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ConfigurationRetiringAsync(
            PubSubConfigurationDataType configuration, CancellationToken cancellationToken)
        {
            await m_viewGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RetireConfigurationViewAsync().ConfigureAwait(false);
            }
            finally
            {
                m_viewGate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask ConfigurationActivatedAsync(
            PubSubConfigurationDataType configuration, CancellationToken cancellationToken)
        {
            return RebuildConfigurationAddressSpaceAsync(cancellationToken);
        }

        private async ValueTask RetireConfigurationViewAsync()
        {
            Volatile.Write(ref m_configurationView, null);
            foreach (IPubSubConfigurationViewObserver observer in m_viewObservers)
            {
                await observer.RetiringAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async ValueTask ActivateConfigurationViewAsync(
            PubSubConfigurationDataType configuration, ArrayOf<PubSubAddressSpaceTarget> targets)
        {
            var view = new PubSubConfigurationView(configuration, targets);
            Volatile.Write(ref m_configurationView, view);
            foreach (IPubSubConfigurationViewObserver observer in m_viewObservers)
            {
                await observer.ActivatedAsync(view, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private readonly SemaphoreSlim m_viewGate = new(1, 1);
        private readonly List<IPubSubConfigurationViewObserver> m_viewObservers = [];
        private PubSubConfigurationView? m_configurationView;
    }
}
