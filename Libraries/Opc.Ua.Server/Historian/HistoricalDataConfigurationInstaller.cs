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

            if (!string.IsNullOrEmpty(capabilities.Definition))
            {
                config.AddAndGetDefinition(context).Value = capabilities.Definition!;
            }

            if (capabilities.MaxTimeInterval > 0)
            {
                config.AddAndGetMaxTimeInterval(context).Value = capabilities.MaxTimeInterval;
            }

            if (capabilities.MinTimeInterval > 0)
            {
                config.AddAndGetMinTimeInterval(context).Value = capabilities.MinTimeInterval;
            }

            if (capabilities.MaxTimeStoredValues > 0)
            {
                config.AddAndGetMaxTimeStoredValues(context).Value = capabilities.MaxTimeStoredValues;
            }

            if (capabilities.MaxCountStoredValues > 0)
            {
                config.AddAndGetMaxCountStoredValues(context).Value = capabilities.MaxCountStoredValues;
            }

            if (capabilities.StartOfArchive != DateTimeUtc.MinValue)
            {
                config.AddAndGetStartOfArchive(context).Value = capabilities.StartOfArchive;
            }

            if (capabilities.StartOfOnlineArchive != DateTimeUtc.MinValue)
            {
                config.AddAndGetStartOfOnlineArchive(context).Value = capabilities.StartOfOnlineArchive;
            }

            if (config.ServerTimestampSupported == null && capabilities.ServerTimestampSupported)
            {
                config.AddServerTimestampSupported(context);
            }
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
    }
}
