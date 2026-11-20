// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a StatusCode/DiagnosticInfo.
    /// </summary>
    public partial class StatusResult
    {
        /// <summary>
        /// Initializes the object with a ServiceResult.
        /// </summary>
        public StatusResult(ServiceResult result)
        {
            Initialize();

            m_result = result;

            if (result != null)
            {
                m_statusCode = result.StatusCode;
            }
        }

        /// <summary>
        /// Applies the diagnostic mask if the object was initialize with a ServiceResult.
        /// </summary>
        public void ApplyDiagnosticMasks(
            DiagnosticsMasks diagnosticMasks,
            StringTable stringTable,
            ILogger logger)
        {
            if (m_result != null)
            {
                m_statusCode = m_result.StatusCode;
                m_diagnosticInfo = new DiagnosticInfo(
                    m_result,
                    diagnosticMasks,
                    false,
                    stringTable,
                    logger);
            }
        }

        private readonly ServiceResult m_result;
    }
}
