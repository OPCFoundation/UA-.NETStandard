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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Scales.Server.Builders;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// What the runtime of a scale needs from the node manager that hosts it.
    /// </summary>
    internal sealed class ScaleRuntimeServices
    {
        public ScaleRuntimeServices(
            ISystemContext context,
            ScaleNamespaceIndices namespaces,
            ScalesServerOptions options,
            Action<NodeState> register,
            Func<NodeId, CancellationToken, ValueTask<bool>> delete,
            Func<NodeId, NodeId, bool> isTypeOf,
            ILockService? lockService,
            ILogger logger)
        {
            Context = context;
            Namespaces = namespaces;
            Options = options;
            Register = register;
            Delete = delete;
            IsTypeOf = isTypeOf;
            LockService = lockService;
            Logger = logger;
        }

        public ISystemContext Context { get; }

        public ScaleNamespaceIndices Namespaces { get; }

        public ScalesServerOptions Options { get; }

        /// <summary>
        /// Registers a subtree created after the scale was registered.
        /// </summary>
        public Action<NodeState> Register { get; }

        /// <summary>
        /// Deletes a registered subtree.
        /// </summary>
        public Func<NodeId, CancellationToken, ValueTask<bool>> Delete { get; }

        /// <summary>
        /// Checks a type is the second type or a subtype of it.
        /// </summary>
        public Func<NodeId, NodeId, bool> IsTypeOf { get; }

        /// <summary>
        /// The DI locking service, when products are lockable.
        /// </summary>
        public ILockService? LockService { get; }

        public ILogger Logger { get; }

        /// <summary>
        /// Builds a NodeId in the Scales namespace.
        /// </summary>
        public NodeId ScalesId(uint id)
        {
            return new NodeId(id, Namespaces.Scales);
        }

        /// <summary>
        /// Builds a browse name for an application instance.
        /// </summary>
        public QualifiedName InstanceName(string name)
        {
            return new QualifiedName(name, Namespaces.Scales);
        }
    }

    /// <summary>
    /// Value helpers shared by the scale runtime.
    /// </summary>
    internal static class ScaleValues
    {
        /// <summary>
        /// Stamps a written value and reports the change to monitored items.
        /// </summary>
        public static void Touch(ISystemContext context, BaseVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTimeUtc.Now;
            variable.ClearChangeMasks(context, includeChildren: false);
        }

        /// <summary>
        /// Writes a value and reports it.
        /// </summary>
        public static void Set(ISystemContext context, BaseVariableState? variable, Variant value)
        {
            if (variable == null)
            {
                return;
            }
            variable.WrappedValue = value;
            Touch(context, variable);
        }

        /// <summary>
        /// Writes a typed property value and reports it.
        /// </summary>
        /// <typeparam name="T">The property's value type.</typeparam>
        public static void Set<T>(ISystemContext context, PropertyState<T>? property, T value)
        {
            if (property == null)
            {
                return;
            }
            property.Value = value;
            Touch(context, property);
        }

        /// <summary>
        /// Writes the <c>EngineeringUnits</c> property of an analog variable,
        /// when it has one.
        /// </summary>
        public static void SetUnits(ISystemContext context, BaseVariableState? variable, EUInformation? units)
        {
            if (variable == null || units == null)
            {
                return;
            }
            if (variable.FindChild(context, new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits))
                is BaseVariableState property)
            {
                property.WrappedValue = Variant.From(new ExtensionObject(units));
                Touch(context, property);
            }
        }

        /// <summary>
        /// Writes the <c>EURange</c> property of an analog variable, when it
        /// has one.
        /// </summary>
        public static void SetRange(ISystemContext context, BaseVariableState? variable, Range? range)
        {
            if (variable == null || range == null)
            {
                return;
            }
            if (variable.FindChild(context, new QualifiedName(Opc.Ua.BrowseNames.EURange))
                is BaseVariableState property)
            {
                property.WrappedValue = Variant.From(new ExtensionObject(range));
                Touch(context, property);
            }
        }

        /// <summary>
        /// Checks an engineering unit passed to a method: it has to be one of
        /// the allowed units when the node publishes a list, and an SI unit
        /// when the server requires them (OPC 40200 §7.4.3, §12.2.2 Table 170).
        /// </summary>
        public static ServiceResult CheckUnit(
            EUInformation? unit,
            ArrayOf<EUInformation> allowed,
            bool requireSi)
        {
            if (unit == null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "An engineering unit is required.");
            }
            if (requireSi && !ScaleUnits.IsSiMass(unit))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The unit '{0}' is not an SI unit and this server only accepts SI units.",
                    unit.DisplayName.Text ?? string.Empty);
            }
            if (allowed.Count > 0)
            {
                foreach (EUInformation candidate in allowed)
                {
                    if (ScaleUnits.SameUnit(candidate, unit))
                    {
                        return ServiceResult.Good;
                    }
                }
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The unit '{0}' is not one of the allowed engineering units.",
                    unit.DisplayName.Text ?? string.Empty);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the <c>EngineeringUnits</c> property of a variable.
        /// </summary>
        public static EUInformation? UnitsOf(ISystemContext context, BaseVariableState? variable)
        {
            if (variable?.FindChild(context, new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits))
                is BaseVariableState property &&
                property.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out EUInformation? units))
            {
                return units;
            }
            return null;
        }

        /// <summary>
        /// Sets the current state of a state machine the model supplies no
        /// tables for, such as the OPC 40001-1 MachineryItemState and
        /// MachineryOperationMode, by pointing <c>CurrentState</c> at the
        /// state object.
        /// </summary>
        public static void SetCurrentState(
            ISystemContext context,
            FiniteStateMachineState? machine,
            NodeId stateId,
            string stateName)
        {
            if (machine?.CurrentState == null)
            {
                return;
            }
            machine.CurrentState.Value = new LocalizedText(stateName);
            if (machine.CurrentState.Id != null)
            {
                machine.CurrentState.Id.Value = stateId;
            }
            machine.CurrentState.StatusCode = StatusCodes.Good;
            machine.CurrentState.Timestamp = DateTimeUtc.Now;
            machine.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Rejects a call on behalf of a client that does not hold the lock of
        /// a lockable element another client holds (DI LockingServices, used
        /// by OPC 40200 §7.8.3 as the product access restriction).
        /// </summary>
        public static ServiceResult CheckLock(
            ScaleRuntimeServices services,
            ISystemContext callContext,
            NodeId elementId)
        {
            if (services.LockService is not { } lockService)
            {
                return ServiceResult.Good;
            }
            LockState state = lockService.GetState(elementId);
            if (!state.Locked)
            {
                return ServiceResult.Good;
            }
            // RenewLock only succeeds for the owning session, so it doubles as
            // the ownership test; renewing on the owner's own activity is what
            // a client holding the lock would do anyway.
            int status = lockService.RenewLock(callContext, elementId);
            return status == LockStatus.Ok
                ? ServiceResult.Good
                : ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "The element is locked by '{0}'.",
                    state.LockingClient);
        }
    }
}
