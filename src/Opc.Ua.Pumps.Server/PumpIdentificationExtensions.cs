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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.AMB.Server.Structure;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Retains application-owned pump identification through the shared asset configuration binding.
    /// </summary>
    public static class PumpIdentificationExtensions
    {
        /// <summary>
        /// Restores AssetId, ComponentName and Location before enabling client writes.
        /// </summary>
        /// <remarks>
        /// ProductInstanceUri must be configured and unique in this owner. Persisted values override the
        /// configured nameplate defaults. Manufacturer-owned fields and ProductInstanceUri remain read-only.
        /// The application supplies role permissions; the server applies them before invoking the commit binding.
        /// A persistent store is required, and a failed commit rejects the write without changing its value.
        /// </remarks>
        public static async ValueTask<IPumpBuilder> WithRetainedIdentificationAsync(
            this PumpsRuntime runtime,
            IPumpBuilder pump,
            IAssetConfigurationStore store,
            ArrayOf<RolePermissionType> permissions,
            CancellationToken cancellationToken = default)
        {
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (pump is null)
            {
                throw new ArgumentNullException(nameof(pump));
            }
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            if (!store.IsPersistent)
            {
                throw new ArgumentException("Retained identification requires a persistent store.", nameof(store));
            }
            if (permissions.IsEmpty)
            {
                throw new ArgumentException("Explicit identity role permissions are required.", nameof(permissions));
            }
            for (int i = 0; i < permissions.Count; i++)
            {
                if (permissions[i] is null || permissions[i].RoleId.IsNull)
                {
                    throw new ArgumentException("Every identity permission must identify a role.", nameof(permissions));
                }
            }
            ValidateOwner(runtime, pump);
            IPumpGroupBuilder identification = pump.Identification;
            if (identification.Child(Opc.Ua.Di.BrowseNames.ProductInstanceUri) is not BaseVariableState identity ||
                !identity.WrappedValue.TryGetValue(out string productInstanceUri) ||
                !Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Configure a stable ProductInstanceUri first.", nameof(pump));
            }
            IdentityRegistrations registrations = s_registrations.GetValue(runtime, static _ => new());
            await registrations.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ValidateOwner(runtime, pump);
                if (registrations.Identities.ContainsKey(productInstanceUri))
                {
                    throw new InvalidOperationException("This ProductInstanceUri already has an identity binding.");
                }
                ArrayOf<PumpState> pumps = runtime.Pumps;
                for (int i = 0; i < pumps.Count; i++)
                {
                    PumpState other = pumps[i];
                    if (ReferenceEquals(other, pump.Pump))
                    {
                        continue;
                    }
                    BaseInstanceState? candidate = other.Identification?.FindChild(runtime.SystemContext,
                        new QualifiedName(Opc.Ua.Di.BrowseNames.ProductInstanceUri, runtime.NamespaceIndices.Di));
                    if (candidate is BaseVariableState value &&
                        value.WrappedValue.TryGetValue(out string otherUri) &&
                        string.Equals(productInstanceUri, otherUri, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Two pumps cannot share a retained ProductInstanceUri.");
                    }
                }
                string[] fields =
                [
                    Opc.Ua.Di.BrowseNames.AssetId,
                    Opc.Ua.Di.BrowseNames.ComponentName,
                    Opc.Ua.Machinery.BrowseNames.Location
                ];
                var variables = new BaseVariableState[fields.Length];
                ILogger logger = runtime.SystemContext.Telemetry.CreateLogger<PumpsRuntime>();
                for (int i = 0; i < fields.Length; i++)
                {
                    variables[i] = identification.Child(fields[i]) as BaseVariableState ??
                        throw new InvalidOperationException("The identification field is not a variable.");
                    if (variables[i].OnSimpleWriteValueAsync is not null ||
                        variables[i].OnSimpleWriteValue is not null ||
                        variables[i].OnWriteValueAsync is not null ||
                        variables[i].OnWriteValue is not null)
                    {
                        throw new InvalidOperationException("An identification field is already bound.");
                    }
                }
                var previous = new DataValue[fields.Length];
                var access = new byte[fields.Length];
                var userAccess = new byte[fields.Length];
                for (int i = 0; i < variables.Length; i++)
                {
                    previous[i] = new DataValue(
                        variables[i].WrappedValue, variables[i].StatusCode, variables[i].Timestamp);
                    access[i] = variables[i].AccessLevel;
                    userAccess[i] = variables[i].UserAccessLevel;
                    variables[i].AccessLevel = (byte)(access[i] & ~AccessLevels.CurrentWrite);
                    variables[i].UserAccessLevel = (byte)(userAccess[i] & ~AccessLevels.CurrentWrite);
                }
                try
                {
                    for (int i = 0; i < fields.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ValidateOwner(runtime, pump);
                        await PersistentProperty.BindAsync(
                            variables[i], store, productInstanceUri, fields[i],
                            i == 1 ? LocalizedTextCodec.Instance : StringCodec.RequiredInstance,
                            maxLength: null, logger, cancellationToken, writable: false).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateOwner(runtime, pump);
                }
                catch
                {
                    for (int i = 0; i < fields.Length; i++)
                    {
                        variables[i].OnSimpleWriteValueAsync = null;
                        variables[i].WrappedValue = previous[i].WrappedValue;
                        variables[i].StatusCode = previous[i].StatusCode;
                        variables[i].Timestamp = previous[i].SourceTimestamp;
                        variables[i].AccessLevel = access[i];
                        variables[i].UserAccessLevel = userAccess[i];
                    }
                    throw;
                }
                for (int i = 0; i < fields.Length; i++)
                {
                    variables[i].RolePermissions = permissions;
                    variables[i].AccessLevel |= AccessLevels.CurrentReadOrWrite;
                    variables[i].UserAccessLevel |= AccessLevels.CurrentReadOrWrite;
                    variables[i].ClearChangeMasks(runtime.SystemContext, false);
                }
                identity.AccessLevel = (byte)(identity.AccessLevel & ~AccessLevels.CurrentWrite);
                identity.UserAccessLevel = (byte)(identity.UserAccessLevel & ~AccessLevels.CurrentWrite);
                registrations.Identities.Add(productInstanceUri, pump.NodeId);
            }
            finally
            {
                registrations.Gate.Release();
            }
            return pump;
        }

        /// <summary>
        /// Rejects removed pumps and runtime shutdown before restoration or writable publication.
        /// </summary>
        private static void ValidateOwner(PumpsRuntime runtime, IPumpBuilder pump)
        {
            if (!ReferenceEquals(runtime.Pump(pump.NodeId)?.Pump, pump.Pump))
            {
                throw new ArgumentException("The pump must belong to this address-space owner.", nameof(pump));
            }
        }

        /// <summary>
        /// Per-owner registrations avoid namespace-index-dependent persisted keys.
        /// </summary>
        private sealed class IdentityRegistrations
        {
            /// <summary>
            /// Serializes restoration and identity reservation.
            /// </summary>
            public SemaphoreSlim Gate { get; } = new(1, 1);

            /// <summary>
            /// Already bound stable identities.
            /// </summary>
            public Dictionary<string, NodeId> Identities { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>Registrations do not extend an address-space owner's lifetime.</summary>
        private static readonly ConditionalWeakTable<PumpsRuntime, IdentityRegistrations> s_registrations = new();
    }
}
