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

using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DiagnosticInfoTests
    {
        /// <summary>
        /// Ensure nested service result is truncated.
        /// </summary>
        [Test]
        public void DiagnosticInfoInnerDiagnostics()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var stringTable = new StringTable();
            var serviceResult = new ServiceResult(
                Namespaces.OpcUa,
                new StatusCode(StatusCodes.BadAggregateConfigurationRejected.Code, "SymbolicId"),
                new LocalizedText("The text", "en-us"),
                new IOException("The inner exception."));
            ILogger logger = telemetry.CreateLogger<DiagnosticInfoTests>();
            var diagnosticInfo = new DiagnosticInfo(
                serviceResult,
                DiagnosticsMasks.All,
                true,
                stringTable,
                logger);
            Assert.NotNull(diagnosticInfo);
            Assert.AreEqual(0, diagnosticInfo.SymbolicId);
            Assert.AreEqual(1, diagnosticInfo.NamespaceUri);
            Assert.AreEqual(2, diagnosticInfo.Locale);
            Assert.AreEqual(3, diagnosticInfo.LocalizedText);

            // recursive inner diagnostics, ensure its truncated
            for (int ii = 0; ii < DiagnosticInfo.MaxInnerDepth + 1; ii++)
            {
                serviceResult = new ServiceResult(serviceResult, serviceResult);
            }
            diagnosticInfo = new DiagnosticInfo(
                serviceResult,
                DiagnosticsMasks.All,
                true,
                stringTable,
                logger);
            Assert.NotNull(diagnosticInfo);
            int depth = 0;
            DiagnosticInfo innerDiagnosticInfo = diagnosticInfo;
            Assert.NotNull(innerDiagnosticInfo);
            while (innerDiagnosticInfo != null)
            {
                depth++;
                innerDiagnosticInfo = innerDiagnosticInfo.InnerDiagnosticInfo;
                if (depth > DiagnosticInfo.MaxInnerDepth)
                {
                    Assert.Null(innerDiagnosticInfo);
                    break;
                }
            }
        }
    }
}
