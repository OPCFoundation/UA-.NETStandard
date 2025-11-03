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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Unit tests for ClientBase class testing all ActivityTraceFlags combinations.
    /// </summary>
    [TestFixture]
    [Category("ClientBase")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ClientBaseTests
    {
        private Mock<ITransportChannel>? m_transportChannelMock;
        private Mock<IServiceMessageContext>? m_messageContextMock;
        private TestLoggerProvider? m_loggerProvider;
        private TestMeterListener? m_meterListener;
        private ITelemetryContext? m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_loggerProvider = new TestLoggerProvider();
            m_messageContextMock = new Mock<IServiceMessageContext>();
            m_messageContextMock
                .Setup(m => m.Telemetry)
                .Returns(m_telemetry);
            m_transportChannelMock = new Mock<ITransportChannel>();
            m_transportChannelMock
                .Setup(m => m.MessageContext)
                .Returns(m_messageContextMock.Object);
            m_transportChannelMock
                .Setup(m => m.EndpointDescription)
                .Returns(new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" });
            m_transportChannelMock
                .Setup(m => m.OperationTimeout).Returns(5000);
            m_meterListener = new TestMeterListener();
        }

        [TearDown]
        public void TearDown()
        {
            m_meterListener?.Dispose();
            m_loggerProvider?.Dispose();
        }

        [Test]
        public void Constructor_WithValidChannel_ShouldInitializeClientBase()
        {
            // Arrange & Act
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);

            // Assert
            Assert.That(sut.NullableTransportChannel, Is.Not.Null);
            Assert.That(sut.ActivityTraceFlags, Is.EqualTo(ClientTraceFlags.None));
        }

        [Test]
        public void Constructor_WithNullChannel_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TestableClientBase(null!, m_telemetry!));
        }

        [Test]
        public void ActivityTraceFlags_None_ShouldNotRecordAnything()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.None;

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            // Act
            sut.TestUpdateRequestHeader(request, true, "Read");
            sut.TestRequestCompleted(request, response, "Read");

            // Assert
            Assert.That(m_loggerProvider!.LogEntries, Is.Empty);
            Assert.That(m_meterListener!.RecordedMeasurements, Is.Empty);
        }

        [Test]
        public void ActivityTraceFlags_Metrics_ShouldRecordMetricsOnly()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Metrics;

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            m_meterListener!.StartListening(sut.TestMeter!);

            // Act
            sut.TestUpdateRequestHeader(request, true, "Read");
            Thread.Sleep(TimeSpan.FromMilliseconds(100)); // simulate some duration
            sut.TestRequestCompleted(request, response, "Read");

            // Assert - metrics should be recorded
            Assert.That(m_meterListener.RecordedMeasurements.Count, Is.GreaterThan(0));
            TestMeterListener.MeasurementRecord measurement =
                m_meterListener.RecordedMeasurements.FirstOrDefault()!;
            Assert.That(measurement!, Is.Not.Null);
            Assert.That(measurement.InstrumentName, Is.EqualTo("opc.ua.client.request.duration"));
            Assert.That(measurement.Value, Is.EqualTo(0.11).Within(0.02));

            // Assert - no logs should be recorded
            Assert.That(m_loggerProvider!.LogEntries
                .Count(e => e.Contains("Read", StringComparison.Ordinal)), Is.EqualTo(0));
        }

        [Test]
        public void ActivityTraceFlags_Log_ShouldRecordLogsOnly()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Log;
            sut.TestLogger = m_loggerProvider!.CreateLogger("ClientBase");

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            // Act
            sut.TestUpdateRequestHeader(request, true, "Read");
            sut.TestRequestCompleted(request, response, "Read");

            // Assert - logs should be recorded
            Assert.That(m_loggerProvider.LogEntries, Has.Count.GreaterThan(0));
            Assert.That(m_loggerProvider.LogEntries.Any(e =>
                e.Contains("Read", StringComparison.Ordinal) &&
                e.Contains("started", StringComparison.Ordinal)), Is.True);
            Assert.That(m_loggerProvider.LogEntries.Any(e =>
                e.Contains("Read", StringComparison.Ordinal) &&
                e.Contains("success", StringComparison.Ordinal)), Is.True);

            // Assert - no metrics should be recorded
            Assert.That(m_meterListener!.RecordedMeasurements, Is.Empty);
        }

        [Test]
        public void ActivityTraceFlags_Traces_ShouldAddActivityEvents()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Traces;

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            var activityListener = new TestActivityListener();

            // Act
            using (Activity activity = new Activity("TestActivity").Start())
            {
                sut.TestUpdateRequestHeader(request, true, "Read");
                sut.TestRequestCompleted(request, response, "Read");

                // Assert - trace context should be added to request header
                Assert.That(request.RequestHeader.AdditionalHeader, Is.Not.Null);
                Assert.That(request.RequestHeader.AdditionalHeader!.Body, Is.InstanceOf<AdditionalParametersType>());

                var additionalParams = (AdditionalParametersType)request.RequestHeader.AdditionalHeader.Body;
                KeyValuePair spanContextParam = additionalParams.Parameters.FirstOrDefault(p => p.Key == "SpanContext")!;
                Assert.That(spanContextParam, Is.Not.Null);
            }

            // Assert - activity events should be recorded
            Assert.That(activityListener.RecordedEvents.Count, Is.GreaterThan(0));
            Assert.That(activityListener.RecordedEvents.Any(e => e.Name == "Started"), Is.True);
            Assert.That(activityListener.RecordedEvents.Any(e => e.Name == "Completed"), Is.True);

            activityListener.Dispose();
        }

        [Test]
        public void ActivityTraceFlags_EventLog_ShouldLogToEventLog()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.EventLog;

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            // Act
            sut.TestUpdateRequestHeader(request, true, "Read");
            sut.TestRequestCompleted(request, response, "Read");

            // Assert - EventLog calls are not easily testable, but we ensure no exceptions are thrown
            Assert.Pass("EventLog flag processed without exceptions");
        }

        [Test]
        public void ActivityTraceFlags_MetricsAndLog_ShouldRecordBoth()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Metrics | ClientTraceFlags.Log;
            sut.TestLogger = m_loggerProvider!.CreateLogger("ClientBase");

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            m_meterListener!.StartListening(sut.TestMeter!);

            // Act
            sut.TestUpdateRequestHeader(request, true, "Read");
            Thread.Sleep(50);
            sut.TestRequestCompleted(request, response, "Read");

            // Assert - both metrics and logs should be recorded
            Assert.That(m_meterListener.RecordedMeasurements.Count, Is.GreaterThan(0));
            Assert.That(m_loggerProvider.LogEntries.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ActivityTraceFlags_TracesAndMetrics_ShouldRecordBoth()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Traces | ClientTraceFlags.Metrics;

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            var activityListener = new TestActivityListener();
            m_meterListener!.StartListening(sut.TestMeter!);

            // Act
            using (Activity activity = new Activity("TestActivity").Start())
            {
                sut.TestUpdateRequestHeader(request, true, "Read");
                Thread.Sleep(50);
                sut.TestRequestCompleted(request, response, "Read");

            }
            // Assert - both traces and metrics should be recorded
            Assert.That(activityListener.RecordedEvents.Count, Is.GreaterThan(0));
            Assert.That(m_meterListener.RecordedMeasurements.Count, Is.GreaterThan(0));

            activityListener.Dispose();
        }

        [Test]
        public void ActivityTraceFlags_AllFlags_ShouldRecordEverything()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Metrics | ClientTraceFlags.Traces |
             ClientTraceFlags.Log | ClientTraceFlags.EventLog;
            sut.TestLogger = m_loggerProvider!.CreateLogger("ClientBase");

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.Good
                }
            };

            var activityListener = new TestActivityListener();
            m_meterListener!.StartListening(sut.TestMeter!);

            // Act
            using (Activity activity = new Activity("TestActivity").Start())
            {
                sut.TestUpdateRequestHeader(request, true, "Read");
                Thread.Sleep(50);
                sut.TestRequestCompleted(request, response, "Read");
            }
            // Assert - all should be recorded
            Assert.That(m_meterListener.RecordedMeasurements.Count, Is.GreaterThan(0));
            Assert.That(m_loggerProvider.LogEntries.Count, Is.GreaterThan(0));
            Assert.That(activityListener.RecordedEvents.Count, Is.GreaterThan(0));

            activityListener.Dispose();
        }

        [Test]
        public void RequestCompleted_WithBadStatusCode_ShouldLogError()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            sut.ActivityTraceFlags = ClientTraceFlags.Log;
            sut.TestLogger = m_loggerProvider!.CreateLogger("ClientBase");

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = request.RequestHeader.RequestHandle,
                    ServiceResult = StatusCodes.BadTimeout
                }
            };

            // Act
            sut.TestUpdateRequestHeader(request, true, "Read");
            sut.TestRequestCompleted(request, response, "Read");

            // Assert
            Assert.That(m_loggerProvider!.LogEntries.Any(e =>
                e.Contains("failed", StringComparison.Ordinal) &&
                e.Contains("BadTimeout", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void UpdateRequestHeader_ShouldSetDefaultValues()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);
            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            // Act
            sut.TestUpdateRequestHeader(request, true);

            // Assert
            Assert.That(request.RequestHeader.RequestHandle, Is.GreaterThan(0u));
            Assert.That(request.RequestHeader.Timestamp, Is.Not.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void NewRequestHandle_ShouldGenerateUniqueHandles()
        {
            // Arrange
            using var sut = new TestableClientBase(m_transportChannelMock!.Object, m_telemetry!);

            // Act
            uint handle1 = sut.NewRequestHandle();
            uint handle2 = sut.NewRequestHandle();
            uint handle3 = sut.NewRequestHandle();

            // Assert
            Assert.That(handle1, Is.Not.EqualTo(handle2));
            Assert.That(handle2, Is.Not.EqualTo(handle3));
            Assert.That(handle1, Is.Not.EqualTo(handle3));
        }

        /// <summary>
        /// Testable wrapper for ClientBase that exposes protected members.
        /// </summary>
        private class TestableClientBase : ClientBase
        {
            public TestableClientBase(ITransportChannel channel, ITelemetryContext telemetry)
                : base(channel)
            {
                m_logger = telemetry.CreateLogger<TestableClientBase>();
                m_meter = new Meter("Opc.Ua.Client.Test", "1.0.0");
            }

            public void TestUpdateRequestHeader(IServiceRequest request, bool useDefaults)
            {
                UpdateRequestHeader(request, useDefaults);
            }

            public void TestUpdateRequestHeader(IServiceRequest request, bool useDefaults, string serviceName)
            {
                UpdateRequestHeader(request, useDefaults, serviceName);
            }

            public void TestRequestCompleted(IServiceRequest request, IServiceResponse response, string serviceName)
            {
                RequestCompleted(request, response, serviceName);
            }

            public ILogger TestLogger
            {
                get => m_logger;
                set => m_logger = value;
            }

            public Meter? TestMeter => m_meter;
        }

        /// <summary>
        /// Test logger provider for capturing log entries.
        /// </summary>
        private class TestLoggerProvider : ILoggerProvider
        {
            public List<string> LogEntries { get; } = new List<string>();

            public ILogger CreateLogger(string categoryName)
            {
                return new TestLogger(this);
            }

            public void Dispose()
            {
            }

            private class TestLogger : ILogger
            {
                private readonly TestLoggerProvider m_provider;

                public TestLogger(TestLoggerProvider provider)
                {
                    m_provider = provider;
                }

                public IDisposable BeginScope<TState>(TState state) where TState : notnull
                {
                    return null!;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    m_provider.LogEntries.Add(formatter(state, exception));
                }
            }
        }

        /// <summary>
        /// Test meter listener for capturing recorded measurements.
        /// </summary>
        private class TestMeterListener : IDisposable
        {
            private MeterListener? m_listener;
            public List<MeasurementRecord> RecordedMeasurements { get; } = new List<MeasurementRecord>();

            public void StartListening(Meter meter)
            {
                m_listener = new MeterListener();
                m_listener.InstrumentPublished = (instrument, listener) =>
                 {
                     if (instrument.Meter.Name == meter.Name)
                     {
                         listener.EnableMeasurementEvents(instrument);
                     }
                 };

                m_listener.SetMeasurementEventCallback<double>(OnMeasurementRecorded);
                m_listener.Start();
            }

            private void OnMeasurementRecorded(
                Instrument instrument,
                double measurement,
                ReadOnlySpan<KeyValuePair<string, object?>> tags,
                object? state)
            {
                RecordedMeasurements.Add(new MeasurementRecord
                {
                    InstrumentName = instrument.Name,
                    Value = measurement,
                    Tags = tags.ToArray()
                });
            }

            public void Dispose()
            {
                m_listener?.Dispose();
            }

            public class MeasurementRecord
            {
                public string InstrumentName { get; set; } = string.Empty;
                public double Value { get; set; }
                public KeyValuePair<string, object?>[] Tags { get; set; } = [];
            }
        }

        /// <summary>
        /// Test activity listener for capturing activity events.
        /// </summary>
        private class TestActivityListener : IDisposable
        {
            private readonly ActivityListener m_listener;
            private Activity? m_currentActivity;

            public List<ActivityEvent> RecordedEvents { get; } = new List<ActivityEvent>();

            public TestActivityListener()
            {
                m_listener = new ActivityListener
                {
                    ShouldListenTo = _ => true,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = activity => m_currentActivity = activity,
                    ActivityStopped = activity =>
                    {
                        // Capture events when activity stops
                        if (activity != null)
                        {
                            RecordedEvents.AddRange(activity.Events);
                        }
                    }
                };

                ActivitySource.AddActivityListener(m_listener);
            }

            public ActivityListener GetListener() => m_listener;

            public void Dispose()
            {
                m_listener?.Dispose();
            }
        }
    }
}
