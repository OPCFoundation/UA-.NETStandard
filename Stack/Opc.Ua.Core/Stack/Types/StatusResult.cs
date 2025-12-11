/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        public void ApplyDiagnosticMasks(DiagnosticsMasks diagnosticMasks, StringTable stringTable, ILogger logger)
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
