// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

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
            Assert.That(diagnosticInfo, Is.Not.Null);
            Assert.That(diagnosticInfo.SymbolicId, Is.Zero);
            Assert.That(diagnosticInfo.NamespaceUri, Is.EqualTo(1));
            Assert.That(diagnosticInfo.Locale, Is.EqualTo(2));
            Assert.That(diagnosticInfo.LocalizedText, Is.EqualTo(3));

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
            Assert.That(diagnosticInfo, Is.Not.Null);
            int depth = 0;
            DiagnosticInfo innerDiagnosticInfo = diagnosticInfo;
            Assert.That(innerDiagnosticInfo, Is.Not.Null);
            while (innerDiagnosticInfo != null)
            {
                depth++;
                innerDiagnosticInfo = innerDiagnosticInfo.InnerDiagnosticInfo!;
                if (depth > DiagnosticInfo.MaxInnerDepth)
                {
                    Assert.That(innerDiagnosticInfo, Is.Null);
                    break;
                }
            }
        }
    }
}
