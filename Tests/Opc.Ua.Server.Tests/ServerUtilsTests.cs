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

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ServerUtils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerUtilsTests
    {
        private ILogger m_logger;

        [SetUp]
        public void SetUp()
        {
            m_logger = NUnitTelemetryContext.Create().CreateLogger("ServerUtilsTests");
        }

        private static OperationContext CreateContext(DiagnosticsMasks mask)
        {
            var header = new RequestHeader {
                ReturnDiagnostics = (uint)mask
            };
            return new OperationContext(header, null, RequestType.Read, RequestLifetime.None);
        }

        [Test]
        public void CreateErrorByIndex_WithDiagnostics_SetsDiagnosticInfo()
        {
            var context = CreateContext(DiagnosticsMasks.OperationAll);
            var diagnosticInfos = new List<DiagnosticInfo> { null, null };

            uint code = ServerUtils.CreateError(
                StatusCodes.BadNodeIdInvalid.Code, context, diagnosticInfos, 0, m_logger);

            Assert.That(code, Is.EqualTo(StatusCodes.BadNodeIdInvalid.Code));
            Assert.That(diagnosticInfos[0], Is.Not.Null);
            Assert.That(diagnosticInfos[1], Is.Null);
        }

        [Test]
        public void CreateErrorByIndex_WithoutDiagnostics_LeavesNull()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var diagnosticInfos = new List<DiagnosticInfo> { null };

            uint code = ServerUtils.CreateError(
                StatusCodes.BadNodeIdInvalid.Code, context, diagnosticInfos, 0, m_logger);

            Assert.That(code, Is.EqualTo(StatusCodes.BadNodeIdInvalid.Code));
            Assert.That(diagnosticInfos[0], Is.Null);
        }

        [Test]
        public void CreateErrorAppend_WithDiagnostics_AddsResultAndDiagnostic()
        {
            var context = CreateContext(DiagnosticsMasks.OperationAll);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            bool hasDiag = ServerUtils.CreateError(
                StatusCodes.BadTypeMismatch.Code, results, diagnosticInfos, context, m_logger);

            Assert.That(hasDiag, Is.True);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(diagnosticInfos, Has.Count.EqualTo(1));
            Assert.That(diagnosticInfos[0], Is.Not.Null);
        }

        [Test]
        public void CreateErrorAppend_WithoutDiagnostics_ReturnsFalse()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            bool hasDiag = ServerUtils.CreateError(
                StatusCodes.BadTypeMismatch.Code, results, diagnosticInfos, context, m_logger);

            Assert.That(hasDiag, Is.False);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(diagnosticInfos, Is.Empty);
        }

        [Test]
        public void CreateErrorAtIndex_WithDiagnostics_SetsAtPosition()
        {
            var context = CreateContext(DiagnosticsMasks.OperationAll);
            var results = new List<StatusCode> { StatusCodes.Good, StatusCodes.Good };
            var diagnosticInfos = new List<DiagnosticInfo> { null, null };

            bool hasDiag = ServerUtils.CreateError(
                StatusCodes.BadAttributeIdInvalid.Code, results, diagnosticInfos, 1, context, m_logger);

            Assert.That(hasDiag, Is.True);
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(results[1], Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(diagnosticInfos[0], Is.Null);
            Assert.That(diagnosticInfos[1], Is.Not.Null);
        }

        [Test]
        public void CreateErrorAtIndex_WithoutDiagnostics_ReturnsFalse()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var results = new List<StatusCode> { StatusCodes.Good };
            var diagnosticInfos = new List<DiagnosticInfo> { null };

            bool hasDiag = ServerUtils.CreateError(
                StatusCodes.BadAttributeIdInvalid.Code, results, diagnosticInfos, 0, context, m_logger);

            Assert.That(hasDiag, Is.False);
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(diagnosticInfos[0], Is.Null);
        }

        [Test]
        public void CreateSuccess_AddsGoodStatusCode()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            ServerUtils.CreateSuccess(results, diagnosticInfos, context);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagnosticInfos, Is.Empty);
        }

        [Test]
        public void CreateSuccess_WithDiagnostics_AddsNullDiagnostic()
        {
            var context = CreateContext(DiagnosticsMasks.OperationAll);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            ServerUtils.CreateSuccess(results, diagnosticInfos, context);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagnosticInfos, Has.Count.EqualTo(1));
            Assert.That(diagnosticInfos[0], Is.Null);
        }

        [Test]
        public void CreateDiagnosticInfoCollection_WithDiagnostics_ReturnsCollection()
        {
            var context = CreateContext(DiagnosticsMasks.OperationAll);
            var errors = new List<ServiceResult>
            {
                ServiceResult.Good,
                new ServiceResult(StatusCodes.BadNodeIdInvalid),
                ServiceResult.Good
            };

            List<DiagnosticInfo> result =
                ServerUtils.CreateDiagnosticInfoCollection(context, errors, m_logger);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0], Is.Null);
            Assert.That(result[1], Is.Not.Null);
            Assert.That(result[2], Is.Null);
        }

        [Test]
        public void CreateDiagnosticInfoCollection_WithoutDiagnostics_ReturnsNull()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var errors = new List<ServiceResult>
            {
                new ServiceResult(StatusCodes.BadNodeIdInvalid)
            };

            List<DiagnosticInfo> result =
                ServerUtils.CreateDiagnosticInfoCollection(context, errors, m_logger);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void CreateStatusCodeCollection_AllGood_ReturnsGoodCodes()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var errors = new List<ServiceResult>
            {
                ServiceResult.Good,
                ServiceResult.Good
            };

            List<StatusCode> result =
                ServerUtils.CreateStatusCodeCollection(context, errors, out List<DiagnosticInfo> diagnosticInfos, m_logger);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(result[1], Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void CreateStatusCodeCollection_WithErrors_ReturnsErrorCodes()
        {
            var context = CreateContext(DiagnosticsMasks.None);
            var errors = new List<ServiceResult>
            {
                ServiceResult.Good,
                new ServiceResult(StatusCodes.BadNodeIdInvalid)
            };

            List<StatusCode> result =
                ServerUtils.CreateStatusCodeCollection(context, errors, out List<DiagnosticInfo> diagnosticInfos, m_logger);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(result[1], Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void CreateDiagnosticInfo_WithNullError_ReturnsNull()
        {
            var serverMock = new Mock<IServerInternal>();
            var context = CreateContext(DiagnosticsMasks.OperationAll);

            DiagnosticInfo result = ServerUtils.CreateDiagnosticInfo(
                serverMock.Object, context, null, m_logger);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void CreateDiagnosticInfo_WithError_ReturnsDiagnosticInfo()
        {
            var serverMock = new Mock<IServerInternal>();
            using var resourceMgr = new ResourceManager(new ApplicationConfiguration());
            serverMock.Setup(s => s.ResourceManager).Returns(resourceMgr);
            var context = CreateContext(DiagnosticsMasks.ServiceLocalizedText);

            var error = new ServiceResult(StatusCodes.BadNodeIdInvalid);

            DiagnosticInfo result = ServerUtils.CreateDiagnosticInfo(
                serverMock.Object, context, error, m_logger);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void CreateDiagnosticInfo_WithoutServiceLocalizedText_SkipsTranslation()
        {
            var serverMock = new Mock<IServerInternal>();
            var context = CreateContext(DiagnosticsMasks.OperationSymbolicId);

            var error = new ServiceResult(StatusCodes.BadNodeIdInvalid);

            DiagnosticInfo result = ServerUtils.CreateDiagnosticInfo(
                serverMock.Object, context, error, m_logger);

            Assert.That(result, Is.Not.Null);
            serverMock.Verify(s => s.ResourceManager, Times.Never);
        }
    }
}
