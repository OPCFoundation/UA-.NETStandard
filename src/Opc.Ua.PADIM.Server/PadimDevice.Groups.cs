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

namespace Opc.Ua.PADIM.Server
{
    public sealed partial class PadimDevice
    {
        /// <summary>
        /// Configures the selected device family's declared condition fields.
        /// </summary>
        public ValueTask<BaseObjectState> ConfigureDeviceConditionsAsync(
            Action<PadimGroupBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            return AddDeclaredGroupAsync(State, "DeviceConditionSet", QualifiedName.Null, configure, cancellationToken);
        }

        /// <summary>
        /// Configures the selected signal family's declared condition fields.
        /// </summary>
        public ValueTask<BaseObjectState> ConfigureSignalConditionsAsync(
            SignalState signal,
            Action<PadimGroupBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            EnsureOwned(signal);
            return AddDeclaredGroupAsync(
                signal, "SignalConditionSet", QualifiedName.Null, configure, cancellationToken);
        }

        /// <summary>
        /// Creates one named calibration using the signal family's own calibration interface and fields.
        /// </summary>
        public ValueTask<BaseObjectState> AddCalibrationAsync(
            AnalogSignalObjectState signal,
            QualifiedName browseName,
            Action<PadimGroupBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            EnsureOwned(signal);
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("A calibration instance name is required.", nameof(browseName));
            }
            return AddDeclaredGroupAsync(
                signal, "<SignalCalibrationIdentifier>", browseName, configure, cancellationToken);
        }

        /// <summary>
        /// Creates a modular child device under the published SubDevices group.
        /// </summary>
        public async ValueTask<PadimDevice> AddSubDeviceAsync(
            QualifiedName browseName,
            PadimDeviceKind kind = PadimDeviceKind.Device,
            CancellationToken cancellationToken = default)
        {
            PadimDevice? child = null;
            await RunAsync(async ct =>
            {
                State.AddSubDevices(Context);
                var builder = await Manager.CreatePadimDeviceAsync(
                    browseName, kind, State.SubDevices, ct).ConfigureAwait(false);
                child = builder.BindPadim();
                await Manager.AddPredefinedNodeAsync(State, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            return child!;
        }

        /// <summary>
        /// Uses the compiled declaration rather than a generic child factory that loses member-specific metadata.
        /// </summary>
        private async ValueTask<BaseObjectState> AddDeclaredGroupAsync(
            BaseObjectState parent,
            string declarationName,
            QualifiedName instanceName,
            Action<PadimGroupBuilder> configure,
            CancellationToken cancellationToken)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            BaseObjectState? group = null;
            await RunAsync(async ct =>
            {
                var name = new QualifiedName(
                    declarationName, (ushort)Context.NamespaceUris.GetIndex(Namespaces.PADIM));
                BaseObjectState declaration = FindGroupDeclaration(parent.TypeDefinitionId, name);
                QualifiedName actualName = instanceName.IsNull ? name : instanceName;
                if (parent.FindChild(Context, actualName) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                group = (BaseObjectState)PadimGroupBuilder.Instantiate(Context, declaration, parent, actualName);
                DisableUnboundWrites(group);
                try
                {
                    configure(new PadimGroupBuilder(this, group, declaration));
                    await Manager.AddPredefinedNodeAsync(State, ct).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    lock (m_bindingGate)
                    {
                        m_bindings.RemoveAll(binding => IsDescendantOf(binding.Variable, group));
                    }
                    parent.RemoveChild(group);
                    try
                    {
                        await Manager.DeleteNodeAsync(Context, group.NodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(
                            "Group configuration and rollback failed.", failure, cleanupFailure);
                    }
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
            return group!;
        }

        /// <summary>
        /// Resolves inherited or overridden instance declarations using the configured type table.
        /// </summary>
        private BaseObjectState FindGroupDeclaration(NodeId typeId, QualifiedName name)
        {
            var visited = new HashSet<NodeId>();
            while (!typeId.IsNull && visited.Add(typeId))
            {
                if (Manager.FindPredefinedNode(typeId)?.FindChild(Context, name) is BaseObjectState declaration)
                {
                    return declaration;
                }
                typeId = Context.TypeTable.FindSuperType(typeId);
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "The selected family does not declare '{0}'.",
                name);
        }

        /// <summary>
        /// Identifies bindings that must be removed after a failed subtree configuration.
        /// </summary>
        private static bool IsDescendantOf(BaseInstanceState node, NodeState root)
        {
            NodeState? current = node;
            while (current is BaseInstanceState instance)
            {
                if (ReferenceEquals(instance, root))
                {
                    return true;
                }
                current = instance.Parent;
            }
            return false;
        }
    }
}
