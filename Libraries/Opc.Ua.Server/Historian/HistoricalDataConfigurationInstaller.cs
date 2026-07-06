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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Installs a populated <c>HistoricalDataConfigurationType</c>
    /// companion object under a historizing variable (Part 11 §5.2.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion object is created via the source-generated
    /// <c>HistoricalDataConfigurationStateActivator</c>, populated from
    /// the per-node <see cref="HistorianNodeCapabilities"/> reported by
    /// the bound <see cref="IHistorianProvider"/>, and attached to the
    /// variable via <c>HasHistoricalConfiguration</c> reference.
    /// </para>
    /// <para>
    /// This utility is opt-in: server implementers call it explicitly
    /// once a historizing variable is registered with a provider. There
    /// is no global "auto-install" hook because the dispatcher does not
    /// own the address space.
    /// </para>
    /// </remarks>
    public static class HistoricalDataConfigurationInstaller
    {
        /// <summary>
        /// Ensures that <paramref name="variable"/> has an attached
        /// <c>HistoricalDataConfigurationType</c> child populated from
        /// <paramref name="provider"/>'s per-node capabilities. Idempotent:
        /// when an existing configuration child is already present, the
        /// utility refreshes the value-bearing properties but does not
        /// add new children.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public static async ValueTask<HistoricalDataConfigurationState> EnsureInstalledAsync(
            ISystemContext context,
            BaseVariableState variable,
            IHistorianProvider provider,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(variable.NodeId, cancellationToken)
                .ConfigureAwait(false);

            HistoricalDataConfigurationState? config = FindExistingConfiguration(context, variable);
            if (config == null)
            {
                var browseName = new QualifiedName(BrowseNames.HAConfiguration);
                config = context.CreateInstanceOfHistoricalDataConfigurationType(variable, browseName);

                // A freshly created instance still carries the type's NodeId; assign a
                // unique instance NodeId via the node manager's factory before wiring the
                // HasHistoricalConfiguration reference, otherwise the reference targets the
                // HistoricalDataConfigurationType node instead of this instance. Mandatory
                // children (Stepped, AggregateConfiguration, ...) are materialised with the
                // type's shared NodeIds too, so assign the whole subtree to avoid collisions
                // when more than one variable installs a configuration object.
                if (context.NodeIdFactory != null)
                {
                    config.NodeId = context.NodeIdFactory.New(context, config);
                    AssignInstanceNodeIds(context, config);
                }

                variable.AddReference(ReferenceTypeIds.HasHistoricalConfiguration, false, config.NodeId);
                config.AddReference(ReferenceTypeIds.HasHistoricalConfiguration, true, variable.NodeId);
                variable.AddChild(config);
            }

            PopulateProperties(context, config, capabilities);
            return config;
        }

        private static void PopulateProperties(
            ISystemContext context,
            HistoricalDataConfigurationState config,
            HistorianNodeCapabilities capabilities)
        {
            config.Stepped?.Value = capabilities.Stepped;
            config
                .AddDefinition(context,
                    !string.IsNullOrEmpty(capabilities.Definition),
                    c => c.Value = capabilities.Definition!)
                .AddMaxTimeInterval(context,
                    capabilities.MaxTimeInterval > 0,
                    c => c.Value = capabilities.MaxTimeInterval)
                .AddMinTimeInterval(context,
                    capabilities.MinTimeInterval > 0,
                    c => c.Value = capabilities.MinTimeInterval)
                .AddMaxTimeStoredValues(context,
                    capabilities.MaxTimeStoredValues > 0,
                    c => c.Value = capabilities.MaxTimeStoredValues)
                .AddMaxCountStoredValues(context,
                    capabilities.MaxCountStoredValues > 0,
                    c => c.Value = capabilities.MaxCountStoredValues)
                .AddStartOfArchive(context,
                    capabilities.StartOfArchive != DateTimeUtc.MinValue,
                    c => c.Value = capabilities.StartOfArchive)
                .AddStartOfOnlineArchive(context,
                    capabilities.StartOfOnlineArchive != DateTimeUtc.MinValue,
                    c => c.Value = capabilities.StartOfOnlineArchive)
                // ServerTimestampSupported: only materialise the slot when the
                // capability is true; sync the value to a pre-existing slot
                // afterwards so disabled capabilities also propagate (matches
                // the v1 semantics).
                .AddServerTimestampSupported(context,
                    capabilities.ServerTimestampSupported,
                    _ => { });
            config.ServerTimestampSupported?.Value = capabilities.ServerTimestampSupported;
        }

        private static HistoricalDataConfigurationState? FindExistingConfiguration(
            ISystemContext context, BaseVariableState variable)
        {
            // Walk the directly-attached children rather than relying on
            // address-space lookups (the companion may not yet be added).
            var browseName = new QualifiedName(BrowseNames.HAConfiguration);
            return variable.FindChild(context, browseName) as HistoricalDataConfigurationState;
        }

        private static void AssignInstanceNodeIds(ISystemContext context, NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = context.NodeIdFactory.New(context, child);
                AssignInstanceNodeIds(context, child);
            }
        }
    }
}
