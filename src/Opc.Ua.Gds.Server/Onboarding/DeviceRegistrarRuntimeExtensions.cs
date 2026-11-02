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
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Binds generated DeviceRegistrarState methods to the registrar runtime.
    /// </summary>
    public static class DeviceRegistrarRuntimeExtensions
    {
        /// <summary>
        /// Wires the registrar runtime methods, including identity selection and candidate registration.
        /// </summary>
        public static void BindToRuntime(this DeviceRegistrarState registrar, IDeviceRegistrarRuntime runtime)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (registrar.ProvideIdentities == null ||
                registrar.UpdateSoftwareStatus == null ||
                registrar.RegisterDeviceEndpoint == null ||
                registrar.GetManagers == null ||
                registrar.RegisterManagedApplication == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            // Generated typed callbacks take precedence over MethodState's raw asynchronous callback.
            registrar.UpdateSoftwareStatus.OnCall = null;
            registrar.UpdateSoftwareStatus.OnCallAsync = null;
            registrar.RegisterDeviceEndpoint.OnCall = null;
            registrar.RegisterDeviceEndpoint.OnCallAsync = null;
            registrar.GetManagers.OnCall = null;
            registrar.GetManagers.OnCallAsync = null;
            registrar.RegisterManagedApplication.OnCall = null;
            registrar.RegisterManagedApplication.OnCallAsync = null;
            registrar.ProvideIdentities.OnCall = null;
            registrar.ProvideIdentities.OnCallAsync = null;
            registrar.ProvideIdentities.OnCallMethod2Async = (context, _, _, inputs, outputs, ct) =>
                ProvideIdentitiesAsync(runtime, context, inputs, outputs, ct);
            registrar.UpdateSoftwareStatus.OnCallMethod2Async = (context, _, _, inputs, _, ct) =>
                UpdateSoftwareStatusAsync(runtime, context, inputs, ct);
            registrar.RegisterDeviceEndpoint.OnCallMethod2Async = (context, _, _, inputs, _, ct) =>
                RegisterDeviceEndpointAsync(runtime, context, inputs, ct);
            registrar.GetManagers.OnCallMethod2Async = (context, _, _, inputs, outputs, ct) =>
                GetManagersAsync(runtime, context, inputs, outputs, ct);
            registrar.RegisterManagedApplication.OnCallMethod2Async = (context, _, _, inputs, outputs, ct) =>
                RegisterManagedApplicationAsync(runtime, context, inputs, outputs, ct);
        }

        private static async ValueTask<ServiceResult> ProvideIdentitiesAsync(
            IDeviceRegistrarRuntime runtime,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            ServiceResult count = CheckCount(inputs.Count, 3);
            if (ServiceResult.IsBad(count))
            {
                return count;
            }
            if (!inputs[0].TryGetValue(out ArrayOf<ByteString> identities))
            {
                return StatusCodes.BadTypeMismatch;
            }
            ArrayOf<ByteString> issuers = default;
            ArrayOf<string> tickets = default;
            if ((!inputs[1].IsNull && !inputs[1].TryGetValue(out issuers)) ||
                (!inputs[2].IsNull && !inputs[2].TryGetValue(out tickets)))
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (outputs.Count < 4)
            {
                return StatusCodes.BadInternalError;
            }
            DeviceRegistrarIdentityResult result = await runtime.ProvideIdentitiesAsync(
                context, identities, issuers, tickets, cancellationToken).ConfigureAwait(false);
            outputs[0] = result.SelectedIdentity;
            outputs[1] = Variant.FromStructure(result.MatchingTicket);
            outputs[2] = result.ApplicationId;
            outputs[3] = Variant.FromStructure(result.SoftwareUpdateManager!);
            return ServiceResult.Good;
        }

        private static async ValueTask<ServiceResult> UpdateSoftwareStatusAsync(
            IDeviceRegistrarRuntime runtime,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            ServiceResult count = CheckCount(inputs.Count, 3);
            if (ServiceResult.IsBad(count))
            {
                return count;
            }
            if (!inputs[0].TryGetValue(out string productInstanceUri) ||
                !inputs[1].TryGetValue(out bool status) ||
                !inputs[2].TryGetValue(out string softwareRevision))
            {
                return StatusCodes.BadTypeMismatch;
            }
            await runtime.UpdateSoftwareStatusAsync(
                context, productInstanceUri, status, softwareRevision, cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
        }

        private static async ValueTask<ServiceResult> RegisterDeviceEndpointAsync(
            IDeviceRegistrarRuntime runtime,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            ServiceResult count = CheckCount(inputs.Count, 1);
            if (ServiceResult.IsBad(count))
            {
                return count;
            }
            if (!inputs[0].TryGetStructure<ApplicationDescription>(out ApplicationDescription? application) ||
                application == null)
            {
                return StatusCodes.BadTypeMismatch;
            }
            await runtime.RegisterDeviceEndpointAsync(context, application, cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
        }

        private static async ValueTask<ServiceResult> GetManagersAsync(
            IDeviceRegistrarRuntime runtime,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            ServiceResult count = CheckCount(inputs.Count, 0);
            if (ServiceResult.IsBad(count))
            {
                return count;
            }
            if (outputs.Count < 1)
            {
                return StatusCodes.BadInternalError;
            }
            ArrayOf<ManagerDescription> managers = await runtime.GetManagersAsync(
                context, cancellationToken).ConfigureAwait(false);
            outputs[0] = Variant.FromStructure(managers);
            return ServiceResult.Good;
        }

        private static async ValueTask<ServiceResult> RegisterManagedApplicationAsync(
            IDeviceRegistrarRuntime runtime,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            ServiceResult count = CheckCount(inputs.Count, 2);
            if (ServiceResult.IsBad(count))
            {
                return count;
            }
            if (!inputs[0].TryGetStructure<ApplicationRecordDataType>(out ApplicationRecordDataType? application) ||
                application == null ||
                !inputs[1].TryGetValue(out string protocolUri))
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (outputs.Count < 1)
            {
                return StatusCodes.BadInternalError;
            }
            NodeId applicationId = await runtime.RegisterManagedApplicationAsync(
                context, application, protocolUri, cancellationToken).ConfigureAwait(false);
            outputs[0] = applicationId;
            return ServiceResult.Good;
        }

        private static ServiceResult CheckCount(int actual, int expected)
        {
            if (actual < expected)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            return actual > expected ? StatusCodes.BadTooManyArguments : ServiceResult.Good;
        }
    }
}
