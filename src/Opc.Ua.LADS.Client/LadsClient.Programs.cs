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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AMB;

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Lists the program templates of a functional unit's program manager.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The program templates; empty when the unit has no program manager.</returns>
        public async ValueTask<ArrayOf<LadsProgramTemplate>> GetProgramTemplatesAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            NodeId set = await ResolveAsync(
                functionalUnitId,
                cancellationToken,
                Lads(BrowseNames.ProgramManager),
                Lads(BrowseNames.ProgramTemplateSet)).ConfigureAwait(false);
            var templates = new List<LadsProgramTemplate>();
            foreach (ReferenceDescription reference in await BrowseAsync(set, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                NodeId templateId = ToNodeId(reference.NodeId);
                if (templateId.IsNull)
                {
                    continue;
                }
                templates.Add(await ReadProgramTemplateAsync(
                    templateId, reference.BrowseName.Name ?? string.Empty, DisplayNameOf(reference), cancellationToken)
                    .ConfigureAwait(false));
            }
            return templates.ToArrayOf();
        }

        /// <summary>
        /// Reads the progress of the program a functional unit is running.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The active program, or <c>null</c> when the unit has no program manager.</returns>
        public async ValueTask<LadsActiveProgram?> ReadActiveProgramAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            NodeId active = await ResolveAsync(
                functionalUnitId,
                cancellationToken,
                Lads(BrowseNames.ProgramManager),
                Lads(BrowseNames.ActiveProgram)).ConfigureAwait(false);
            if (active.IsNull)
            {
                return null;
            }
            DataValue[] values = await ReadPathsAsync(
                active,
                [
                    [Lads(BrowseNames.CurrentProgramTemplate)],
                    [Lads(BrowseNames.DeviceProgramRunId)],
                    [Lads(BrowseNames.CurrentStepName)],
                    [Lads(BrowseNames.CurrentStepNumber)],
                    [Lads(BrowseNames.EstimatedStepNumbers)],
                    [Lads(BrowseNames.CurrentRuntime)],
                    [Lads(BrowseNames.CurrentPauseTime)],
                    [Lads(BrowseNames.CurrentStepRuntime)],
                    [Lads(BrowseNames.EstimatedRuntime)],
                    [Lads(BrowseNames.EstimatedStepRuntime)]
                ],
                cancellationToken).ConfigureAwait(false);
            NameNodeIdDataType? template = AsNameNodeId(values[0]);
            return new LadsActiveProgram(active)
            {
                CurrentProgramTemplate = template?.Name.Text,
                CurrentProgramTemplateId = template?.NodeId ?? NodeId.Null,
                DeviceProgramRunId = AsString(values[1]),
                CurrentStepName = AsString(values[2]),
                CurrentStepNumber = AsUInt32(values[3]),
                EstimatedStepNumbers = AsUInt32(values[4]),
                CurrentRuntime = AsDuration(values[5]),
                CurrentPauseTime = AsDuration(values[6]),
                CurrentStepRuntime = AsDuration(values[7]),
                EstimatedRuntime = AsDuration(values[8]),
                EstimatedStepRuntime = AsDuration(values[9])
            };
        }

        /// <summary>
        /// Reads the results of a functional unit's program manager, including
        /// their properties, samples and result files.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The results; empty when the unit has no program manager.</returns>
        public async ValueTask<ArrayOf<LadsResult>> GetResultsAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            NodeId set = await ResolveAsync(
                functionalUnitId,
                cancellationToken,
                Lads(BrowseNames.ProgramManager),
                Lads(BrowseNames.ResultSet)).ConfigureAwait(false);
            var results = new List<LadsResult>();
            foreach (ReferenceDescription reference in await BrowseAsync(set, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                NodeId resultId = ToNodeId(reference.NodeId);
                if (resultId.IsNull)
                {
                    continue;
                }
                results.Add(await ReadResultAsync(
                    resultId, reference.BrowseName.Name ?? string.Empty, DisplayNameOf(reference), cancellationToken)
                    .ConfigureAwait(false));
            }
            return results.ToArrayOf();
        }

        /// <summary>
        /// Reads a result, including its properties, samples and result files.
        /// </summary>
        /// <param name="resultId">The NodeId of the result.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result.</returns>
        public async ValueTask<LadsResult> ReadResultAsync(
            NodeId resultId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(resultId, nameof(resultId));
            string displayName = await ReadDisplayNameAsync(resultId, cancellationToken).ConfigureAwait(false);
            return await ReadResultAsync(resultId, displayName, displayName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists the files of a result's <c>FileSet</c>.
        /// </summary>
        /// <param name="resultId">The NodeId of the result.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result files.</returns>
        public async ValueTask<ArrayOf<LadsResultFile>> GetResultFilesAsync(
            NodeId resultId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(resultId, nameof(resultId));
            NodeId set = await ResolveAsync(resultId, cancellationToken, Lads(BrowseNames.FileSet))
                .ConfigureAwait(false);
            var files = new List<LadsResultFile>();
            foreach (ReferenceDescription reference in await BrowseAsync(set, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                NodeId fileId = ToNodeId(reference.NodeId);
                if (fileId.IsNull)
                {
                    continue;
                }
                string fileName = reference.BrowseName.Name ?? string.Empty;
                files.Add(await ReadResultFileAsync(fileId, fileName, cancellationToken).ConfigureAwait(false));
            }
            return files.ToArrayOf();
        }

        /// <summary>
        /// Starts a program on a functional unit through the <c>StartProgram</c>
        /// method of its <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="request">The program to start.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The <c>DeviceProgramRunId</c> the device assigned to the run.</returns>
        public async ValueTask<string> StartProgramAsync(
            NodeId functionalUnitId,
            LadsProgramRequest request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            if (request is null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }
            FunctionalUnitStateMachineTypeClient state = await FunctionalUnitStateAsync(
                functionalUnitId, cancellationToken).ConfigureAwait(false);
            string? runId = await state.StartProgramAsync(
                request.TemplateId,
                request.Properties,
                request.SupervisoryJobId ?? string.Empty,
                request.SupervisoryTaskId ?? string.Empty,
                request.Samples,
                cancellationToken).ConfigureAwait(false);
            return runId ?? string.Empty;
        }

        /// <summary>
        /// Starts a functional unit without a program through the <c>Start</c>
        /// method of its <c>FunctionalUnitState</c>.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="properties">The properties to start with.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask StartAsync(
            NodeId functionalUnitId,
            ArrayOf<KeyValuePair> properties = default,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            FunctionalUnitStateMachineTypeClient state = await FunctionalUnitStateAsync(
                functionalUnitId, cancellationToken).ConfigureAwait(false);
            await state.StartAsync(properties, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the content of a program template through the <c>Download</c>
        /// method of the functional unit's program manager.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="templateId">The identifier of the program template.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The additional parameters and the template content.</returns>
        /// <exception cref="ServiceResultException">
        /// The unit has no program manager or the template is unknown.
        /// </exception>
        public async ValueTask<(ArrayOf<KeyValueType> AdditionalParameters, ByteString Data)>
            DownloadProgramTemplateAsync(
                NodeId functionalUnitId,
                string templateId,
                CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            if (string.IsNullOrEmpty(templateId))
            {
                throw new System.ArgumentException("A template id is required.", nameof(templateId));
            }
            ProgramManagerTypeClient manager = await ProgramManagerAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            (ArrayOf<KeyValueType> parameters, ByteString data) = await manager
                .DownloadAsync(templateId, cancellationToken).ConfigureAwait(false);
            return (parameters, data);
        }

        /// <summary>
        /// Uploads a program template through the <c>Upload</c> method of the
        /// functional unit's program manager.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="data">The template content.</param>
        /// <param name="additionalParameters">Additional parameters, for example a <c>TemplateId</c>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The identifier the device assigned to the template.</returns>
        /// <exception cref="ServiceResultException">
        /// The unit has no program manager or the content is invalid.
        /// </exception>
        public async ValueTask<string> UploadProgramTemplateAsync(
            NodeId functionalUnitId,
            ByteString data,
            ArrayOf<KeyValueType> additionalParameters = default,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            ProgramManagerTypeClient manager = await ProgramManagerAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            string? templateId = await manager.UploadAsync(additionalParameters, data, cancellationToken)
                .ConfigureAwait(false);
            return templateId ?? string.Empty;
        }

        /// <summary>
        /// Removes a program template through the <c>Remove</c> method of the
        /// functional unit's program manager.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="templateId">The identifier of the program template.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// The unit has no program manager or the template is unknown.
        /// </exception>
        public async ValueTask RemoveProgramTemplateAsync(
            NodeId functionalUnitId,
            string templateId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            if (string.IsNullOrEmpty(templateId))
            {
                throw new System.ArgumentException("A template id is required.", nameof(templateId));
            }
            ProgramManagerTypeClient manager = await ProgramManagerAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            await manager.RemoveAsync(templateId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Decodes a <see cref="NameNodeIdDataType"/> value.
        /// </summary>
        internal NameNodeIdDataType? AsNameNodeId(DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode))
            {
                return null;
            }
            return value.WrappedValue.TryGetStructure<NameNodeIdDataType>(
                Session.MessageContext,
                out NameNodeIdDataType? structure)
                ? structure
                : null;
        }

        /// <summary>
        /// Decodes an array of structures, accepting a scalar as a single element.
        /// </summary>
        /// <typeparam name="T">The structure type.</typeparam>
        internal ArrayOf<T> AsStructures<T>(DataValue value)
            where T : class, IEncodeable, new()
        {
            if (StatusCode.IsBad(value.StatusCode))
            {
                return [];
            }
            if (value.WrappedValue.TryGetStructure(Session.MessageContext, out ArrayOf<T> array))
            {
                return array;
            }
            return value.WrappedValue.TryGetStructure<T>(Session.MessageContext, out T? single) && single is not null
                ? [single]
                : [];
        }

        private async ValueTask<LadsProgramTemplate> ReadProgramTemplateAsync(
            NodeId templateId,
            string name,
            string displayName,
            CancellationToken cancellationToken)
        {
            DataValue[] values = await ReadPathsAsync(
                templateId,
                [
                    [Lads(BrowseNames.Author)],
                    [Lads(BrowseNames.Description)],
                    [Lads(BrowseNames.DeviceTemplateId)],
                    [Lads(BrowseNames.SupervisoryTemplateId)],
                    [Lads(BrowseNames.Version)],
                    [Lads(BrowseNames.Created)],
                    [Lads(BrowseNames.Modified)]
                ],
                cancellationToken).ConfigureAwait(false);
            return new LadsProgramTemplate(templateId, name)
            {
                DisplayName = displayName,
                Author = AsString(values[0]),
                Description = AsString(values[1]),
                DeviceTemplateId = AsString(values[2]),
                SupervisoryTemplateId = AsString(values[3]),
                Version = AsString(values[4]),
                Created = AsDate(values[5]),
                Modified = AsDate(values[6])
            };
        }

        private async ValueTask<LadsResult> ReadResultAsync(
            NodeId resultId,
            string name,
            string displayName,
            CancellationToken cancellationToken)
        {
            DataValue[] values = await ReadPathsAsync(
                resultId,
                [
                    [Lads(BrowseNames.Description)],
                    [Lads(BrowseNames.ApplicationUri)],
                    [Lads(BrowseNames.User)],
                    [Lads(BrowseNames.SupervisoryJobId)],
                    [Lads(BrowseNames.SupervisoryTaskId)],
                    [Lads(BrowseNames.DeviceProgramRunId)],
                    [Lads(BrowseNames.Started)],
                    [Lads(BrowseNames.Stopped)],
                    [Lads(BrowseNames.TotalRuntime)],
                    [Lads(BrowseNames.TotalPauseTime)],
                    [Lads(BrowseNames.EstimatedRuntime)],
                    [Lads(BrowseNames.Properties)],
                    [Lads(BrowseNames.Samples)]
                ],
                cancellationToken).ConfigureAwait(false);
            ArrayOf<LadsResultFile> files = await GetResultFilesAsync(resultId, cancellationToken)
                .ConfigureAwait(false);
            return new LadsResult(resultId, name)
            {
                DisplayName = displayName,
                Description = AsString(values[0]),
                ApplicationUri = AsString(values[1]),
                User = AsString(values[2]),
                SupervisoryJobId = AsString(values[3]),
                SupervisoryTaskId = AsString(values[4]),
                DeviceProgramRunId = AsString(values[5]),
                Started = AsDate(values[6]),
                Stopped = AsDate(values[7]),
                TotalRuntime = AsDuration(values[8]),
                TotalPauseTime = AsDuration(values[9]),
                EstimatedRuntime = AsDuration(values[10]),
                Properties = AsStructures<KeyValueType>(values[11]),
                Samples = AsStructures<SampleInfoType>(values[12]),
                Files = files
            };
        }

        private async ValueTask<LadsResultFile> ReadResultFileAsync(
            NodeId fileId,
            string name,
            CancellationToken cancellationToken)
        {
            DataValue[] values = await ReadPathsAsync(
                fileId,
                [
                    [Lads(BrowseNames.Name)],
                    [Lads(BrowseNames.MimeType)],
                    [Lads(BrowseNames.URL)]
                ],
                cancellationToken).ConfigureAwait(false);
            NodeId file = await ResolveAsync(fileId, cancellationToken, Lads(BrowseNames.File)).ConfigureAwait(false);
            return new LadsResultFile(fileId, name)
            {
                FileName = AsString(values[0]),
                MimeType = AsString(values[1]),
                Url = AsString(values[2]),
                FileId = file
            };
        }

        private async ValueTask<FunctionalUnitStateMachineTypeClient> FunctionalUnitStateAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken)
        {
            NodeId state = await RequireAsync(
                functionalUnitId, cancellationToken, Lads(BrowseNames.FunctionalUnitState)).ConfigureAwait(false);
            return new FunctionalUnitStateMachineTypeClient(Session, state, Telemetry);
        }

        private async ValueTask<ProgramManagerTypeClient> ProgramManagerAsync(
            NodeId functionalUnitId,
            CancellationToken cancellationToken)
        {
            NodeId manager = await RequireAsync(
                functionalUnitId, cancellationToken, Lads(BrowseNames.ProgramManager)).ConfigureAwait(false);
            return new ProgramManagerTypeClient(Session, manager, Telemetry);
        }
    }
}
