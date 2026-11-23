// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Optional provider capability for formatting diagnostic RWData selected by OPC 30010 CodeTypesRWData.
    /// </summary>
    /// <remarks>
    /// The catalogue is declared in AutoIdReaderParameterCapabilities.CodeTypesRWData and selections are
    /// committed through IAutoIdScanParametersProvider. Empty selections disable RWData without calling
    /// the formatter. For multiple selections the provider chooses one format it can actually produce;
    /// selection order does not define priority. Use the supplied selection snapshot even if a parameter
    /// commit occurs during formatting. Formatting changes neither method outputs nor event data.
    /// The runtime copies input and output storage and keeps the reporting operation active until completion.
    /// A formatter must not await disposal of its reader or an operation waiting for its own callback.
    /// </remarks>
    public interface IAutoIdRfidDiagnosticFormatter : IAutoIdReaderParametersProvider
    {
        /// <summary>
        /// Converts credential-excluded user data into one selected diagnostic representation.
        /// </summary>
        /// <param name="data">An independent snapshot of the reported user data.</param>
        /// <param name="sourceCodeType">The reported RWData code type, or empty when the report omits it.</param>
        /// <param name="selectedCodeTypes">The nonempty set of selected supported format names.</param>
        /// <param name="cancellationToken">Cancels formatting before diagnostic publication.</param>
        /// <returns>One selected code type and the matching ScanData value.</returns>
        /// <remarks>
        /// Throw when no selected format can represent the input. The runtime records the failure and
        /// publishes RWData as unavailable without changing an already executed command's outcome.
        /// Cancellation propagates without publishing a partial diagnostic update.
        /// </remarks>
        ValueTask<AutoIdRfidDiagnosticData> FormatRWDataAsync(
            ScanData data,
            string sourceCodeType,
            ArrayOf<string> selectedCodeTypes,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A formatted diagnostic value and the code type that describes its ScanData union field.
    /// </summary>
    public sealed class AutoIdRfidDiagnosticData
    {
        /// <summary>
        /// Creates a diagnostic representation for validation and copying by the runtime.
        /// </summary>
        /// <param name="codeType">The selected format name.</param>
        /// <param name="data">The converted union value.</param>
        /// <exception cref="ArgumentNullException">A required value is null.</exception>
        public AutoIdRfidDiagnosticData(string codeType, ScanData data)
        {
            CodeType = codeType ?? throw new ArgumentNullException(nameof(codeType));
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// Gets the selected code type actually produced by the formatter.
        /// </summary>
        public string CodeType { get; }

        /// <summary>
        /// Gets the corresponding ScanData value; the runtime copies it before retaining it.
        /// </summary>
        public ScanData Data { get; }
    }
}
