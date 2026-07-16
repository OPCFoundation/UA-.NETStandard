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

#nullable enable

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="ServerUtils"/>. The fixture is
    /// non-parallelizable and resets the static <see cref="ServerUtils.EventsEnabled"/>
    /// flag (and its private event queue) after every test so the shared mutable state
    /// never leaks between tests.
    /// </summary>
    [TestFixture]
    [Category("ServerUtilsDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ServerUtilsDeterministicTests
    {
        private ILogger m_logger = null!;

        [SetUp]
        public void SetUp()
        {
            m_logger = NUnitTelemetryContext.Create().CreateLogger("ServerUtilsDeterministicTests");
        }

        [TearDown]
        public void TearDown()
        {
            // ServerUtils holds static mutable state (EventsEnabled plus a private event
            // queue). Setting the flag back to false clears the queue and keeps tests isolated.
            ServerUtils.EventsEnabled = false;
        }

        private static OperationContext CreateContext(DiagnosticsMasks mask)
        {
            var header = new RequestHeader
            {
                ReturnDiagnostics = (uint)mask
            };
            return new OperationContext(header, null, RequestType.Read, RequestLifetime.None);
        }

        private static List<DiagnosticInfo> CreateDiagnosticSlots(int count)
        {
            return [.. new DiagnosticInfo[count]];
        }

        [Test]
        public void EventsEnabledDefaultsToFalse()
        {
            Assert.That(ServerUtils.EventsEnabled, Is.False);
        }

        [Test]
        public void EventsEnabledSetTrueReturnsTrue()
        {
            ServerUtils.EventsEnabled = true;

            Assert.That(ServerUtils.EventsEnabled, Is.True);
        }

        [Test]
        public void EventsEnabledSetTrueThenFalseReturnsFalse()
        {
            ServerUtils.EventsEnabled = true;
            Assert.That(ServerUtils.EventsEnabled, Is.True);

            ServerUtils.EventsEnabled = false;
            Assert.That(ServerUtils.EventsEnabled, Is.False);
        }

        [Test]
        public void EventsEnabledTransitionToFalseWithQueuedEventDoesNotThrow()
        {
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));
            ServerUtils.EventsEnabled = true;
            ServerUtils.ReportQueuedValue(nodeId, 7u, value);

            Assert.That(() => ServerUtils.EventsEnabled = false, Throws.Nothing);
            Assert.That(ServerUtils.EventsEnabled, Is.False);
        }

        [Test]
        public void ReportWriteValueWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(
                () => ServerUtils.ReportWriteValue(nodeId, value, StatusCodes.Good),
                Throws.Nothing);
        }

        [Test]
        public void ReportWriteValueWhenEventsEnabledWithGoodStatusDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(
                () => ServerUtils.ReportWriteValue(nodeId, value, StatusCodes.Good),
                Throws.Nothing);
        }

        [Test]
        public void ReportWriteValueWhenEventsEnabledWithBadStatusDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            // A bad status forces the branch that rewraps the value in a new DataValue.
            Assert.That(
                () => ServerUtils.ReportWriteValue(nodeId, value, StatusCodes.BadNodeIdUnknown),
                Throws.Nothing);
        }

        [Test]
        public void ReportQueuedValueWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportQueuedValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportQueuedValueWhenEventsEnabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportQueuedValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportFilteredValueWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportFilteredValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportFilteredValueWhenEventsEnabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportFilteredValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportDiscardedValueWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportDiscardedValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportDiscardedValueWhenEventsEnabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportDiscardedValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportPublishValueWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportPublishValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportPublishValueWhenEventsEnabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);
            var value = new DataValue(new Variant(42));

            Assert.That(() => ServerUtils.ReportPublishValue(nodeId, 7u, value), Throws.Nothing);
        }

        [Test]
        public void ReportCreateMonitoredItemWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);

            Assert.That(
                () => ServerUtils.ReportCreateMonitoredItem(
                    nodeId, 7u, 100.0, 10u, true, new DataChangeFilter(), MonitoringMode.Reporting),
                Throws.Nothing);
        }

        [Test]
        public void ReportCreateMonitoredItemWhenEventsEnabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);

            Assert.That(
                () => ServerUtils.ReportCreateMonitoredItem(
                    nodeId, 7u, 100.0, 10u, true, new DataChangeFilter(), MonitoringMode.Reporting),
                Throws.Nothing);
        }

        [Test]
        public void ReportModifyMonitoredItemWhenEventsDisabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = false;
            var nodeId = new NodeId(1234u);

            Assert.That(
                () => ServerUtils.ReportModifyMonitoredItem(
                    nodeId, 7u, 250.0, 5u, false, new DataChangeFilter(), MonitoringMode.Sampling),
                Throws.Nothing);
        }

        [Test]
        public void ReportModifyMonitoredItemWhenEventsEnabledDoesNotThrow()
        {
            ServerUtils.EventsEnabled = true;
            var nodeId = new NodeId(1234u);

            Assert.That(
                () => ServerUtils.ReportModifyMonitoredItem(
                    nodeId, 7u, 250.0, 5u, false, new DataChangeFilter(), MonitoringMode.Sampling),
                Throws.Nothing);
        }

        [Test]
        public void CreateErrorByIndexWithOperationAllSetsDiagnosticInfo()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            List<DiagnosticInfo> diagnosticInfos = CreateDiagnosticSlots(2);

            uint code = ServerUtils.CreateError(
                StatusCodes.BadNodeIdUnknown.Code, context, diagnosticInfos, 0, m_logger);

            Assert.That(code, Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
            Assert.That(diagnosticInfos[0], Is.Not.Null);
            Assert.That(diagnosticInfos[1], Is.Null);
        }

        [Test]
        public void CreateErrorByIndexWithNoneLeavesNull()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.None);
            List<DiagnosticInfo> diagnosticInfos = CreateDiagnosticSlots(1);

            uint code = ServerUtils.CreateError(
                StatusCodes.BadNodeIdUnknown.Code, context, diagnosticInfos, 0, m_logger);

            Assert.That(code, Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
            Assert.That(diagnosticInfos[0], Is.Null);
        }

        [Test]
        public void CreateErrorAppendWithOperationAllReturnsTrue()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            bool hasDiagnostic = ServerUtils.CreateError(
                StatusCodes.BadTypeMismatch.Code, results, diagnosticInfos, context, m_logger);

            Assert.That(hasDiagnostic, Is.True);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(diagnosticInfos, Has.Count.EqualTo(1));
            Assert.That(diagnosticInfos[0], Is.Not.Null);
        }

        [Test]
        public void CreateErrorAppendWithNoneReturnsFalse()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.None);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            bool hasDiagnostic = ServerUtils.CreateError(
                StatusCodes.BadTypeMismatch.Code, results, diagnosticInfos, context, m_logger);

            Assert.That(hasDiagnostic, Is.False);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(diagnosticInfos, Is.Empty);
        }

        [Test]
        public void CreateErrorAtIndexWithOperationAllReturnsTrue()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var results = new List<StatusCode> { StatusCodes.Good, StatusCodes.Good };
            List<DiagnosticInfo> diagnosticInfos = CreateDiagnosticSlots(2);

            bool hasDiagnostic = ServerUtils.CreateError(
                StatusCodes.BadAttributeIdInvalid.Code, results, diagnosticInfos, 1, context, m_logger);

            Assert.That(hasDiagnostic, Is.True);
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(results[1], Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(diagnosticInfos[0], Is.Null);
            Assert.That(diagnosticInfos[1], Is.Not.Null);
        }

        [Test]
        public void CreateErrorAtIndexWithNoneReturnsFalse()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.None);
            var results = new List<StatusCode> { StatusCodes.Good };
            List<DiagnosticInfo> diagnosticInfos = CreateDiagnosticSlots(1);

            bool hasDiagnostic = ServerUtils.CreateError(
                StatusCodes.BadAttributeIdInvalid.Code, results, diagnosticInfos, 0, context, m_logger);

            Assert.That(hasDiagnostic, Is.False);
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(diagnosticInfos[0], Is.Null);
        }

        [Test]
        public void CreateSuccessWithOperationAllAddsGoodAndNullPlaceholder()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            ServerUtils.CreateSuccess(results, diagnosticInfos, context);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagnosticInfos, Has.Count.EqualTo(1));
            Assert.That(diagnosticInfos[0], Is.Null);
        }

        [Test]
        public void CreateSuccessWithNoneAddsGoodOnly()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.None);
            var results = new List<StatusCode>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            ServerUtils.CreateSuccess(results, diagnosticInfos, context);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagnosticInfos, Is.Empty);
        }

        [Test]
        public void CreateDiagnosticInfoCollectionWithNoneReturnsNull()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.None);
            var errors = new List<ServiceResult> { new(StatusCodes.BadNodeIdUnknown) };

            List<DiagnosticInfo>? result =
                ServerUtils.CreateDiagnosticInfoCollection(context, errors, m_logger);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void CreateDiagnosticInfoCollectionWithOperationAllMapsBadAndGood()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var errors = new List<ServiceResult>
            {
                new(StatusCodes.BadNodeIdUnknown),
                ServiceResult.Good
            };

            List<DiagnosticInfo> result =
                ServerUtils.CreateDiagnosticInfoCollection(context, errors, m_logger)!;

            Assert.That(result, Has.Count.EqualTo(errors.Count));
            Assert.That(result[0], Is.Not.Null);
            Assert.That(result[1], Is.Null);
        }

        [Test]
        public void CreateStatusCodeCollectionMapsBadAndGoodCodes()
        {
            OperationContext context = CreateContext(DiagnosticsMasks.None);
            var errors = new List<ServiceResult>
            {
                ServiceResult.Good,
                new(StatusCodes.BadNodeIdUnknown)
            };

            List<StatusCode> result =
                ServerUtils.CreateStatusCodeCollection(context, errors, out _, m_logger);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(result[1], Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void CreateStatusCodeCollectionAllGoodWithOperationAllReturnsNonNullDiagnostics()
        {
            // Pins current behavior; ServerUtils.CreateStatusCodeCollection has a known inverted
            // noErrors condition (see repo findings) — do not assert the corrected behavior here.
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var errors = new List<ServiceResult> { ServiceResult.Good, ServiceResult.Good };

            List<StatusCode> result = ServerUtils.CreateStatusCodeCollection(
                context, errors, out List<DiagnosticInfo> diagnosticInfos, m_logger);

            Assert.That(result[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(result[1], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagnosticInfos, Is.Not.Null);
            Assert.That(diagnosticInfos, Has.Count.EqualTo(2));
        }

        [Test]
        public void CreateStatusCodeCollectionWithBadErrorLeavesDiagnosticsNull()
        {
            // Pins current behavior; ServerUtils.CreateStatusCodeCollection has a known inverted
            // noErrors condition (see repo findings) — do not assert the corrected behavior here.
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var errors = new List<ServiceResult>
            {
                ServiceResult.Good,
                new(StatusCodes.BadNodeIdUnknown)
            };

            List<StatusCode> result = ServerUtils.CreateStatusCodeCollection(
                context, errors, out List<DiagnosticInfo> diagnosticInfos, m_logger);

            Assert.That(result[1], Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(diagnosticInfos, Is.Null);
        }

        [Test]
        public void CreateDiagnosticInfoWithNullErrorReturnsNull()
        {
            var serverMock = new Mock<IServerInternal>();
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);

            DiagnosticInfo? result = ServerUtils.CreateDiagnosticInfo(
                serverMock.Object, context, null!, m_logger);

            Assert.That(result, Is.Null);
            serverMock.VerifyNoOtherCalls();
        }

        [Test]
        public void CreateDiagnosticInfoWithoutServiceLocalizedTextSkipsResourceManager()
        {
            var serverMock = new Mock<IServerInternal>();
            OperationContext context = CreateContext(DiagnosticsMasks.OperationAll);
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            DiagnosticInfo? result = ServerUtils.CreateDiagnosticInfo(
                serverMock.Object, context, error, m_logger);

            Assert.That(result, Is.Not.Null);
            serverMock.Verify(s => s.ResourceManager, Times.Never);
        }

        [Test]
        public void CreateDiagnosticInfoWithServiceLocalizedTextAccessesResourceManager()
        {
            using var resourceManager = new ResourceManager(new ApplicationConfiguration());
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.ResourceManager).Returns(resourceManager);
            OperationContext context = CreateContext(DiagnosticsMasks.ServiceLocalizedText);
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            DiagnosticInfo? result = ServerUtils.CreateDiagnosticInfo(
                serverMock.Object, context, error, m_logger);

            Assert.That(result, Is.Not.Null);
            serverMock.Verify(s => s.ResourceManager, Times.Once);
        }
    }
}
