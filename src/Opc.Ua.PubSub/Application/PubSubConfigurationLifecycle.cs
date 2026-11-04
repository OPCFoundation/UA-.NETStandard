/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Additive server integration seam for invalidation before runtime retirement and updates
    /// after configuration activation. Does not start or configure a transport.
    /// </summary>
    public interface IPubSubConfigurationLifecycle
    {
        /// <summary>Attaches a server observer. Observers must not mutate the application.</summary>
        void AddConfigurationObserver(IPubSubConfigurationObserver observer);

        /// <summary>Detaches a server observer.</summary>
        void RemoveConfigurationObserver(IPubSubConfigurationObserver observer);
    }

    /// <summary>Receives serialized successful configuration lifecycle transitions.</summary>
    public interface IPubSubConfigurationObserver
    {
        /// <summary>
        /// Invalidates associations before old runtime components are stopped or disposed.
        /// The implementation must complete without throwing.
        /// </summary>
        ValueTask ConfigurationRetiringAsync(PubSubConfigurationDataType configuration, CancellationToken cancellationToken);

        /// <summary>
        /// Activates associations after the runtime configuration is replaced.
        /// The implementation must complete without throwing.
        /// </summary>
        ValueTask ConfigurationActivatedAsync(PubSubConfigurationDataType configuration, CancellationToken cancellationToken);
    }

    public sealed partial class PubSubApplication
    {
        /// <inheritdoc/>
        public void AddConfigurationObserver(IPubSubConfigurationObserver observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            lock (m_gate)
            {
                if (m_configurationObservers.Contains(observer))
                {
                    throw new InvalidOperationException("The observer is already attached.");
                }
                m_configurationObservers.Add(observer);
            }
        }

        /// <inheritdoc/>
        public void RemoveConfigurationObserver(IPubSubConfigurationObserver observer)
        {
            lock (m_gate)
            {
                m_configurationObservers.Remove(observer);
            }
        }

        private async ValueTask NotifyConfigurationRetiringAsync(PubSubConfigurationDataType configuration)
        {
            foreach (IPubSubConfigurationObserver observer in ConfigurationObservers())
            {
                await observer.ConfigurationRetiringAsync(
                    (PubSubConfigurationDataType)configuration.Clone(), CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async ValueTask NotifyConfigurationActivatedAsync(PubSubConfigurationDataType configuration)
        {
            foreach (IPubSubConfigurationObserver observer in ConfigurationObservers())
            {
                await observer.ConfigurationActivatedAsync(
                    (PubSubConfigurationDataType)configuration.Clone(), CancellationToken.None).ConfigureAwait(false);
            }
        }

        private IPubSubConfigurationObserver[] ConfigurationObservers()
        {
            lock (m_gate)
            {
                return [.. m_configurationObservers];
            }
        }

        private readonly List<IPubSubConfigurationObserver> m_configurationObservers = [];
    }
}
