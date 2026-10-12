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
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Binds companion-declared nodes without replacing their standard identities.
    /// </summary>
    public static class MachineryCompanionBuilderExtensions
    {
        /// <summary>
        /// Prepares Machinery bindings for a root whose registration belongs to another companion builder.
        /// </summary>
        /// <remarks>
        /// Enlist the returned participant with the registration owner. The callback runs during preparation,
        /// before publication. Do not call the callback builder's BuildAsync: the owner registers the tree once.
        /// Disposal before commitment releases this participant without deleting the root.
        /// </remarks>
        /// <typeparam name="TState">The externally staged state type.</typeparam>
        /// <param name="context">The Machinery context on the same node manager and instance namespace.</param>
        /// <param name="machine">The exact unregistered root instance.</param>
        /// <param name="configure">Configures shared Machinery bindings without publishing.</param>
        /// <returns>A single-use registration participant.</returns>
        public static ICompanionRegistrationParticipant BindMachine<TState>(
            this IMachineryBuildContext context,
            TState machine,
            Action<IMachineBuilder<TState>> configure)
            where TState : BaseObjectState
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (context is not MachineryBuildContext implementation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The Machinery context does not support externally staged bindings.");
            }
            return implementation.BindMachine(machine, configure);
        }

        /// <summary>
        /// Configures an existing machine or component nameplate at its companion-declared location.
        /// </summary>
        /// <typeparam name="TState">The containing machine or peripheral state.</typeparam>
        /// <param name="builder">The containing builder.</param>
        /// <param name="identification">The existing derived identification descendant.</param>
        /// <param name="configure">Supplies the validated identification values.</param>
        /// <returns>The containing builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">The identification is incompatible or belongs elsewhere.</exception>
        public static IMachineBuilder<TState> WithIdentification<TState>(
            this IMachineBuilder<TState> builder,
            MachineryItemIdentificationState identification,
            Action<MachineryIdentificationData> configure)
            where TState : BaseObjectState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (identification == null)
            {
                throw new ArgumentNullException(nameof(identification));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.GetMachineBuilder().BindIdentification(identification, configure);
        }

        /// <summary>
        /// Enlists a binding after node publication and before this machine's successful commitment.
        /// </summary>
        /// <remarks>
        /// A returned resource is disposed on rollback and owned by the machine on success.
        /// A callback that throws before returning must release any resources it allocated itself.
        /// </remarks>
        /// <typeparam name="TState">The machine's generated state type.</typeparam>
        /// <param name="builder">The unregistered machine builder.</param>
        /// <param name="bind">Creates the binding and returns its lifetime resource.</param>
        /// <returns>The same machine builder.</returns>
        public static IMachineBuilder<TState> WithRegistrationBinding<TState>(
            this IMachineBuilder<TState> builder,
            Func<TState, ISystemContext, CancellationToken, ValueTask<IAsyncDisposable>> bind)
            where TState : BaseObjectState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (bind == null)
            {
                throw new ArgumentNullException(nameof(bind));
            }
            return builder.GetMachineBuilder().WithRegistrationBinding(bind);
        }

        /// <summary>
        /// Abandons an unregistered machine after a configuration failure.
        /// </summary>
        /// <remarks>
        /// Only valid before BuildAsync starts. Releases staged resources and reservations,
        /// never deletes an indexed tree, and makes the abandoned builder unusable.
        /// </remarks>
        /// <typeparam name="TState">The machine's generated state type.</typeparam>
        /// <param name="builder">The machine builder to abandon.</param>
        public static ValueTask AbortAsync<TState>(this IMachineBuilder<TState> builder)
            where TState : BaseObjectState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.GetMachineBuilder().AbortAsync();
        }

        /// <summary>
        /// Publishes a numeric sample without losing its scalar type, quality or acquisition timestamp.
        /// </summary>
        /// <remarks>
        /// A bad sample may carry a null value. Server timestamps remain owned by the OPC UA read service.
        /// Cancellation or an incompatible value leaves the previously published sample unchanged.
        /// </remarks>
        /// <param name="handle">The bound process-value handle.</param>
        /// <param name="value">The acquired sample.</param>
        /// <param name="cancellationToken">Cancels publication before changing the signal.</param>
        /// <returns>The publication operation.</returns>
        /// <exception cref="ArgumentNullException">The handle is null.</exception>
        /// <exception cref="ServiceResultException">The handle or sample cannot be published.</exception>
        public static ValueTask SetValueAsync(
            this IProcessValueHandle handle,
            in DataValue value,
            CancellationToken cancellationToken = default)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }
            if (handle is not ProcessValueBuilder implementation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The process-value handle does not support publishing complete samples.");
            }
            return implementation.SetValueAsync(value, cancellationToken);
        }

        /// <summary>
        /// Binds an existing derived process value without replacing the object or its signal.
        /// </summary>
        /// <remarks>
        /// The existing signal must use ProcessValueVariableType or a subtype so the shared
        /// limit and percentage facilities remain available. Its PADIM-qualified browse name
        /// and instance identity are preserved.
        /// </remarks>
        /// <typeparam name="TState">The containing machine state type.</typeparam>
        /// <param name="builder">The machine builder.</param>
        /// <param name="processValue">The existing process-value descendant.</param>
        /// <param name="configure">Configures standard process-value behavior.</param>
        /// <returns>The machine builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">The descendant or its signal cannot be bound.</exception>
        public static IMachineBuilder<TState> WithProcessValue<TState>(
            this IMachineBuilder<TState> builder,
            ProcessValueState processValue,
            Action<IProcessValueBuilder> configure)
            where TState : BaseObjectState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (processValue == null)
            {
                throw new ArgumentNullException(nameof(processValue));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.GetMachineBuilder().BindProcessValue(processValue, configure);
        }

        /// <summary>
        /// Binds existing companion job management, requiring both control and response providers.
        /// </summary>
        /// <remarks>
        /// Provider validation runs before the machine enters the address space. The existing
        /// receiver-only Machinery overload remains unchanged.
        /// </remarks>
        /// <typeparam name="TState">The machine state type.</typeparam>
        /// <param name="builder">The machine builder.</param>
        /// <param name="management">The existing job-management descendant.</param>
        /// <param name="configure">Configures the per-machine providers.</param>
        /// <returns>The machine builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The builder cannot bind companion nodes or the node is outside its machine.
        /// </exception>
        public static IMachineBuilder<TState> WithJobManagement<TState>(
            this IMachineBuilder<TState> builder,
            JobManagementState management,
            Action<IJobManagementBuilder> configure)
            where TState : BaseObjectState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (management == null)
            {
                throw new ArgumentNullException(nameof(management));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.GetMachineBuilder().BindJobManagement(management, configure);
        }

        /// <summary>
        /// Binds an existing result-management descendant to the Machinery result runtime.
        /// </summary>
        /// <remarks>
        /// The descendant keeps its parent, browse name, reference type and type definition.
        /// This overload requires a builder returned by an OPC UA Machinery build context.
        /// </remarks>
        /// <typeparam name="TState">The machine state type.</typeparam>
        /// <param name="builder">The machine builder.</param>
        /// <param name="management">The existing result-management descendant.</param>
        /// <param name="configure">Configures its store and selected result features.</param>
        /// <returns>The machine builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The builder does not support companion bindings, the node is not its descendant,
        /// or result management is already configured.
        /// </exception>
        public static IMachineBuilder<TState> WithResultManagement<TState>(
            this IMachineBuilder<TState> builder,
            ResultManagementState management,
            Action<IResultManagementBuilder> configure)
            where TState : BaseObjectState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (management == null)
            {
                throw new ArgumentNullException(nameof(management));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.GetMachineBuilder().BindResultManagement(management, configure);
        }

        /// <summary>
        /// Creates the result ring using a companion's derived result-variable factory.
        /// </summary>
        /// <param name="builder">The result-management builder.</param>
        /// <param name="publishedResults">The number of result slots, or zero for no slots.</param>
        /// <param name="createResult">Creates an initialized result variable of the required subtype.</param>
        /// <returns>The result-management builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">The builder cannot bind companion result nodes.</exception>
        public static IResultManagementBuilder WithResultsFolder(
            this IResultManagementBuilder builder,
            int publishedResults,
            Func<ISystemContext, NodeState, QualifiedName, ResultState> createResult)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (createResult == null)
            {
                throw new ArgumentNullException(nameof(createResult));
            }
            return builder.GetResultBuilder().WithResultsFolder(publishedResults, createResult);
        }

        /// <summary>
        /// Publishes a companion's result-ready subtype through the shared result publisher.
        /// </summary>
        /// <remarks>
        /// The factory receives the result being published and must return an initialized instance
        /// of the selected event type. Its specialized result child is preserved. A concrete
        /// application subtype is registered without altering the released event declaration.
        /// </remarks>
        /// <param name="builder">The result-management builder.</param>
        /// <param name="eventTypeId">The declared result-ready event subtype.</param>
        /// <param name="createEvent">Creates the specialized event for a result.</param>
        /// <returns>The result-management builder.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ServiceResultException">The builder or event type cannot bind result-ready events.</exception>
        public static IResultManagementBuilder WithResultReadyEvents(
            this IResultManagementBuilder builder,
            NodeId eventTypeId,
            Func<ISystemContext, NodeState, MachineryResult, ResultReadyEventState> createEvent)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (createEvent == null)
            {
                throw new ArgumentNullException(nameof(createEvent));
            }
            return builder.GetResultBuilder().WithResultReadyEvents(eventTypeId, createEvent);
        }

        /// <summary>
        /// Resolves the built-in result builder without extending existing implementers' contracts.
        /// </summary>
        private static ResultManagementBuilder GetResultBuilder(this IResultManagementBuilder builder)
        {
            return builder as ResultManagementBuilder ??
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The result-management builder does not support companion result factories.");
        }

        /// <summary>
        /// Resolves the built-in machine builder without extending existing implementers' contracts.
        /// </summary>
        /// <typeparam name="TState">The machine state type.</typeparam>
        private static MachineBuilder<TState> GetMachineBuilder<TState>(this IMachineBuilder<TState> builder)
            where TState : BaseObjectState
        {
            return builder as MachineBuilder<TState> ??
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The machine builder does not support binding existing companion nodes.");
        }
    }
}
