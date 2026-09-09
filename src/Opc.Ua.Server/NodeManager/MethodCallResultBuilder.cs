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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Builds Call results using the receiving service's diagnostic StringTable.
    /// </summary>
    internal static class MethodCallResultBuilder
    {
        public static ServiceResult Apply(
            ServiceResult? operation,
            ArrayOf<ServiceResult> argumentResults,
            ArrayOf<Variant> outputs,
            CallMethodResult result,
            OperationContext? context,
            ILogger logger)
        {
            operation ??= ServiceResult.Good;
            if (ServiceResult.IsBad(operation) && operation.StatusCode != StatusCodes.BadInvalidArgument)
            {
                return operation;
            }
            bool failedArgument = false;
            foreach (ServiceResult input in argumentResults)
            {
                failedArgument |= ServiceResult.IsBad(input);
            }
            if (failedArgument || (operation.StatusCode == StatusCodes.BadInvalidArgument && !argumentResults.IsEmpty))
            {
                var statuses = new List<StatusCode>(argumentResults.Count);
                var diagnostics = new List<DiagnosticInfo>(argumentResults.Count);
                bool includeDiagnostics = context is not null &&
                    (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0;
                foreach (ServiceResult input in argumentResults)
                {
                    statuses.Add(input?.StatusCode ?? StatusCodes.Good);
                    if (includeDiagnostics)
                    {
                        diagnostics.Add(HasDiagnosticDetails(input)
                            ? new DiagnosticInfo(input, context!.DiagnosticsMask, false, context.StringTable, logger)
                            : null!);
                    }
                }
                result.InputArgumentResults = statuses;
                result.InputArgumentDiagnosticInfos = diagnostics;
                result.StatusCode = StatusCodes.BadInvalidArgument;
                return operation.StatusCode == StatusCodes.BadInvalidArgument
                    ? operation : new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            if (ServiceResult.IsGoodOrUncertain(operation))
            {
                result.OutputArguments = outputs;
            }
            return operation;
        }

        internal static bool HasDiagnosticDetails([NotNullWhen(true)] ServiceResult? input)
        {
            return input is not null &&
                (input.StatusCode != StatusCodes.Good ||
                input.SymbolicId != StatusCodes.Good.SymbolicId ||
                !string.IsNullOrEmpty(input.NamespaceUri) ||
                !input.LocalizedText.IsNullOrEmpty ||
                input.AdditionalInfo is not null ||
                input.InnerResult is not null);
        }
    }
}
