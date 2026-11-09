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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;

namespace CompanionSuite
{
    /// <summary>
    /// An explicitly simulated, one-acquisition inspection provider using only the standard vision operations.
    /// </summary>
    internal sealed class SimulatedInspection : IMachineVisionOperationsProvider
    {
        /// <summary>
        /// Creates an independent camera acquisition provider.
        /// </summary>
        internal SimulatedInspection(int number)
        {
            m_number = number;
        }

        /// <summary>
        /// Gets an actual completed simulated inspection for the read-only workflow.
        /// </summary>
        internal ResultDataType PublishedResult => new()
        {
            ResultId = new ResultIdDataType { Id = "Inspection" + m_number },
            ResultState = m_number,
            IsSimulated = true,
            ResultContent = [Variant.From(m_number)],
            EncodingMask = (uint)ResultDataTypeFields.ResultContent
        };

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> ControlAsync(
            MachineVisionControlAction action, int cause, string causeDescription, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> PrepareRecipeAsync(
            MachineVisionDocument recipe, bool prepare, CancellationToken cancellationToken)
        {
            return Unsupported(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> ActivateConfigurationAsync(
            MachineVisionDocument configuration, CancellationToken cancellationToken)
        {
            return Unsupported(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> ValidateDocumentAsync(
            MachineVisionDocument document, CancellationToken cancellationToken)
        {
            return Unsupported(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionOperationResult> StartJobAsync(
            MachineVisionJob job, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<MachineVisionOperationResult>(job.Simulated && !job.Continuous
                ? MachineVisionOperationResult.Good
                : new MachineVisionOperationResult(new ServiceResult(StatusCodes.BadNotSupported)));
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<MachineVisionAcquisition> ExecuteAsync(
            MachineVisionJob job, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            ResultDataType result = PublishedResult;
            result.ResultId = new ResultIdDataType { Id = "Job-" + job.JobId.Id };
            yield return new MachineVisionAcquisition(true, result, ByteString.From((byte)m_number));
        }

        /// <inheritdoc/>
        public ValueTask<MachineVisionStepResult> ExecuteStepAsync(
            MachineVisionStepRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<MachineVisionStepResult>(new MachineVisionStepResult(
                new MachineVisionOperationResult(new ServiceResult(StatusCodes.BadNotSupported)), false));
        }

        /// <summary>
        /// Refuses unselected optional document operations rather than pretending to apply them.
        /// </summary>
        private static ValueTask<MachineVisionOperationResult> Unsupported(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<MachineVisionOperationResult>(
                new MachineVisionOperationResult(new ServiceResult(StatusCodes.BadNotSupported)));
        }

        /// <summary>The independent camera acquisition identity.</summary>
        private readonly int m_number;
    }
}
