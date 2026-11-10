/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Binds an existing generated ProvisionableDevice without creating or claiming ownership of Resources.
    /// </summary>
    public static class ProvisionableDeviceRuntimeExtensions
    {
        /// <summary>
        /// Validates singleton layout and binds asynchronous device methods before the host publishes its nodes.
        /// The owning node manager remains responsible for placement under the shared Resources infrastructure.
        /// </summary>
        public static void BindToRuntime(
            this ProvisionableDeviceState device,
            ISystemContext context,
            IProvisionableDeviceRuntime runtime)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (device.IsSingleton == null || device.RequestTickets == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            if (device.SetRegistrarEndpoints is { } registrarMethod &&
                (registrarMethod.OnReadRolePermissions != null ||
                    registrarMethod.OnReadUserRolePermissions != null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Registrar configuration already has dynamic role permissions.");
            }
            var children = new List<BaseInstanceState>();
            device.GetChildren(context, children);
            if (runtime.IsSingleton)
            {
                foreach (BaseInstanceState child in children)
                {
                    if (child is BaseObjectState instance &&
                        (instance is ApplicationConfigurationState ||
                            instance.TypeDefinitionId == Opc.Ua.ObjectTypeIds.ApplicationConfigurationType ||
                            context.TypeTable?.IsTypeOf(
                                instance.TypeDefinitionId, Opc.Ua.ObjectTypeIds.ApplicationConfigurationType) == true))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            "A singleton device cannot expose separate ApplicationConfiguration components.");
                    }
                }
            }
            device.IsSingleton.Value = runtime.IsSingleton;
            device.RequestTickets.OnCall = null;
            device.RequestTickets.OnCallAsync = null;
            device.RequestTickets.OnCallMethod2Async = (_, _, _, inputs, outputs, ct) =>
                RequestTicketsAsync(runtime, inputs, outputs, ct);
            if (device.SetRegistrarEndpoints != null)
            {
                device.SetRegistrarEndpoints.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                    }
                ];
                device.SetRegistrarEndpoints.UserRolePermissions = device.SetRegistrarEndpoints.RolePermissions;
                device.SetRegistrarEndpoints.OnCall = null;
                device.SetRegistrarEndpoints.OnCallAsync = null;
                device.SetRegistrarEndpoints.OnCallMethod2Async = (callContext, _, _, inputs, _, ct) =>
                    SetRegistrarEndpointsAsync(runtime, callContext, inputs, ct);
            }
        }

        /// <summary>
        /// Returns signed String-subtype tickets through the generated method's existing output slot.
        /// </summary>
        private static async ValueTask<ServiceResult> RequestTicketsAsync(
            IProvisionableDeviceRuntime runtime,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count != 0)
            {
                return StatusCodes.BadTooManyArguments;
            }
            if (outputs.Count < 1)
            {
                return StatusCodes.BadInternalError;
            }
            ArrayOf<string> tickets = await runtime.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
            outputs[0] = Variant.From(tickets);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Accepts only the standard ApplicationDescription array, leaving authorization to the runtime.
        /// </summary>
        private static async ValueTask<ServiceResult> SetRegistrarEndpointsAsync(
            IProvisionableDeviceRuntime runtime,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count != 1)
            {
                return inputs.Count == 0 ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments;
            }
            if (!inputs[0].TryGetStructure(out ArrayOf<ApplicationDescription> registrars))
            {
                return StatusCodes.BadTypeMismatch;
            }
            await runtime.SetRegistrarEndpointsAsync(context, registrars, cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
        }
    }
}
