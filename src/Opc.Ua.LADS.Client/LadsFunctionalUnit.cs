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
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A handle to a LADS functional unit that offers the program, state
    /// machine and function operations of <see cref="LadsClient"/> fluently.
    /// </summary>
    public sealed class LadsFunctionalUnit
    {
        /// <summary>
        /// Creates a handle.
        /// </summary>
        internal LadsFunctionalUnit(LadsClient client, NodeId nodeId, string name, string displayName)
        {
            Client = client;
            NodeId = nodeId;
            Name = name;
            DisplayName = displayName;
        }

        /// <summary>
        /// The client the handle belongs to.
        /// </summary>
        public LadsClient Client { get; }

        /// <summary>
        /// The NodeId of the functional unit.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The browse name of the functional unit.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The display name of the functional unit.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Reads a snapshot of the functional unit.
        /// </summary>
        /// <param name="includeFunctions">Whether to read the functions too.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public ValueTask<LadsFunctionalUnitSnapshot> ReadAsync(
            bool includeFunctions = true,
            CancellationToken cancellationToken = default)
        {
            return Client.ReadFunctionalUnitAsync(NodeId, includeFunctions, cancellationToken);
        }

        /// <summary>
        /// Reads the current state of the <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The state, or <c>null</c> when the unit publishes none.</returns>
        public async ValueTask<LadsStateSnapshot?> ReadStateAsync(CancellationToken cancellationToken = default)
        {
            NodeId state = await StateMachineIdAsync(cancellationToken).ConfigureAwait(false);
            return await Client.ReadStateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the functions of the functional unit.
        /// </summary>
        /// <param name="recursive">Whether to include the functions nested in functions.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functions.</returns>
        public ValueTask<ArrayOf<LadsFunction>> GetFunctionsAsync(
            bool recursive = true,
            CancellationToken cancellationToken = default)
        {
            return Client.GetFunctionsAsync(NodeId, recursive, cancellationToken);
        }

        /// <summary>
        /// Gets a function by browse name, display name or path such as
        /// <c>Heater/Temperature</c>.
        /// </summary>
        /// <param name="nameOrPath">The browse name, display name or path.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The function.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when the unit has no such function.
        /// </exception>
        public async ValueTask<LadsFunction> GetFunctionAsync(
            string nameOrPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(nameOrPath))
            {
                throw new ArgumentException("A function name is required.", nameof(nameOrPath));
            }
            ArrayOf<LadsFunction> functions = await GetFunctionsAsync(true, cancellationToken).ConfigureAwait(false);
            foreach (LadsFunction function in functions)
            {
                if (string.Equals(function.Path, nameOrPath, StringComparison.Ordinal))
                {
                    return function;
                }
            }
            foreach (LadsFunction function in functions)
            {
                if (LadsClient.Matches(function.Name, function.DisplayName, nameOrPath))
                {
                    return function;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotFound, "The functional unit '{0}' has no function '{1}'.", Name, nameOrPath);
        }

        /// <summary>
        /// Lists the program templates of the unit's program manager.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The program templates.</returns>
        public ValueTask<ArrayOf<LadsProgramTemplate>> GetProgramTemplatesAsync(
            CancellationToken cancellationToken = default)
        {
            return Client.GetProgramTemplatesAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Downloads the content of a program template.
        /// </summary>
        /// <param name="templateId">The identifier of the program template.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The additional parameters and the template content.</returns>
        public ValueTask<(ArrayOf<KeyValueType> AdditionalParameters, ByteString Data)> DownloadProgramTemplateAsync(
            string templateId,
            CancellationToken cancellationToken = default)
        {
            return Client.DownloadProgramTemplateAsync(NodeId, templateId, cancellationToken);
        }

        /// <summary>
        /// Uploads a program template.
        /// </summary>
        /// <param name="data">The template content.</param>
        /// <param name="additionalParameters">Additional parameters, for example a <c>TemplateId</c>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The identifier the device assigned to the template.</returns>
        public ValueTask<string> UploadProgramTemplateAsync(
            ByteString data,
            ArrayOf<KeyValueType> additionalParameters = default,
            CancellationToken cancellationToken = default)
        {
            return Client.UploadProgramTemplateAsync(NodeId, data, additionalParameters, cancellationToken);
        }

        /// <summary>
        /// Removes a program template.
        /// </summary>
        /// <param name="templateId">The identifier of the program template.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask RemoveProgramTemplateAsync(string templateId, CancellationToken cancellationToken = default)
        {
            return Client.RemoveProgramTemplateAsync(NodeId, templateId, cancellationToken);
        }

        /// <summary>
        /// Reads the progress of the active program.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The active program, or <c>null</c> when the unit has no program manager.</returns>
        public ValueTask<LadsActiveProgram?> ReadActiveProgramAsync(CancellationToken cancellationToken = default)
        {
            return Client.ReadActiveProgramAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Reads the results of the unit's program manager.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The results.</returns>
        public ValueTask<ArrayOf<LadsResult>> GetResultsAsync(CancellationToken cancellationToken = default)
        {
            return Client.GetResultsAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Reads the result of a program run.
        /// </summary>
        /// <param name="deviceProgramRunId">
        /// The run id <see cref="StartProgramAsync(LadsProgramRequest, CancellationToken)"/> returned.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result, or <c>null</c> when the unit has none for the run.</returns>
        public async ValueTask<LadsResult?> FindResultAsync(
            string deviceProgramRunId,
            CancellationToken cancellationToken = default)
        {
            foreach (LadsResult result in await GetResultsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(result.DeviceProgramRunId, deviceProgramRunId, StringComparison.Ordinal) ||
                    string.Equals(result.Name, deviceProgramRunId, StringComparison.Ordinal))
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// Starts a program.
        /// </summary>
        /// <param name="request">The program to start.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The <c>DeviceProgramRunId</c> the device assigned to the run.</returns>
        public ValueTask<string> StartProgramAsync(
            LadsProgramRequest request,
            CancellationToken cancellationToken = default)
        {
            return Client.StartProgramAsync(NodeId, request, cancellationToken);
        }

        /// <summary>
        /// Starts a program template without properties or samples.
        /// </summary>
        /// <param name="templateId">The id of the program template.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The <c>DeviceProgramRunId</c> the device assigned to the run.</returns>
        public ValueTask<string> StartProgramAsync(string templateId, CancellationToken cancellationToken = default)
        {
            return Client.StartProgramAsync(NodeId, LadsProgramRequest.For(templateId), cancellationToken);
        }

        /// <summary>
        /// Starts the unit without a program.
        /// </summary>
        /// <param name="properties">The properties to start with.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask StartAsync(
            ArrayOf<KeyValuePair> properties = default,
            CancellationToken cancellationToken = default)
        {
            return Client.StartAsync(NodeId, properties, cancellationToken);
        }

        /// <summary>
        /// Stops the unit.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return Client.StopAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Aborts the unit.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            return Client.AbortAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Clears the stopped or aborted unit.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            return Client.ClearAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Holds the running program.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask HoldAsync(CancellationToken cancellationToken = default)
        {
            return Client.HoldAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Resumes the held program.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask UnholdAsync(CancellationToken cancellationToken = default)
        {
            return Client.UnholdAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Suspends the running program.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask SuspendAsync(CancellationToken cancellationToken = default)
        {
            return Client.SuspendAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Resumes the suspended program.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask UnsuspendAsync(CancellationToken cancellationToken = default)
        {
            return Client.UnsuspendAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Resets the running state machine.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            return Client.ResetAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Asks the unit to run its program to completion.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ToCompleteAsync(CancellationToken cancellationToken = default)
        {
            return Client.ToCompleteAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Observes the <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="streaming">
        /// The streaming subscription to monitor with; <c>null</c> to use the
        /// default streaming subscription of a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token that ends the observation.</param>
        /// <returns>The states, starting with the current state.</returns>
        public async IAsyncEnumerable<LadsStateSnapshot> ObserveStateAsync(
            IStreamingSubscription? streaming = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId state = await StateMachineIdAsync(cancellationToken).ConfigureAwait(false);
            await foreach (LadsStateSnapshot snapshot in Client.ObserveStateAsync(
                state, streaming, cancellationToken).ConfigureAwait(false))
            {
                yield return snapshot;
            }
        }

        /// <summary>
        /// Waits until the <c>FunctionalUnitState</c> reaches a state, for example
        /// <c>Stopped</c> after a program ran to completion.
        /// </summary>
        /// <param name="stateName">The name of the state to wait for.</param>
        /// <param name="timeout">The longest time to wait.</param>
        /// <param name="streaming">
        /// The streaming subscription to monitor with; <c>null</c> to use the
        /// default streaming subscription of a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The state that was reached.</returns>
        public async ValueTask<LadsStateSnapshot> WaitForStateAsync(
            string stateName,
            TimeSpan timeout,
            IStreamingSubscription? streaming = null,
            CancellationToken cancellationToken = default)
        {
            NodeId state = await StateMachineIdAsync(cancellationToken).ConfigureAwait(false);
            return await Client.WaitForStateAsync(state, stateName, timeout, streaming, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return DisplayName;
        }

        private ValueTask<NodeId> StateMachineIdAsync(CancellationToken cancellationToken)
        {
            return Client.RequireAsync(NodeId, cancellationToken, Client.Lads(BrowseNames.FunctionalUnitState));
        }
    }
}
