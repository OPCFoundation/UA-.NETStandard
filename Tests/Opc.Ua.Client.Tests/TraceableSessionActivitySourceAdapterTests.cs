using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Verifies the behavior of <see cref="TraceableSessionActivitySourceAdapter{T}"/>.
    /// </summary>
    public class TraceableSessionActivitySourceAdapterTests
    {
        [Test]
        public void TestTraceContextIsPropagated()
        {
            StartActivityListener(true);

            // Create a new instance of TraceableSessionActivitySourceAdapter and start an activity.
            var _injectionAdapter = new TraceableSessionActivitySourceAdapter<int>(new ActivitySource(TraceableSession.ActivitySourceName));
            Activity intectionTestActivity =_injectionAdapter.StartActivity("Trace Propogation Test Activity");
            Assert.IsNotNull(intectionTestActivity);

            // Inject the trace context into a dictionary.
            var traceData = new Dictionary<string, string>();
            _injectionAdapter.InjectTraceContext(new PropagationContext(Activity.Current.Context, Baggage.Current), traceData);
            var incomingValue = new ExtensionObject();

            // Update the AdditionalHeader property of the ExtensionObject with the trace data.
            var _extractionAdapter = new TraceableSessionActivitySourceAdapter<int>(new ActivitySource("Extract-Activity-Source"));
            var requestHeader = new TraceableRequestHeader(_extractionAdapter);
            incomingValue.Body = TraceableRequestHeader.ConvertTraceDataToXmlElement(traceData);
            requestHeader.AdditionalHeader = incomingValue;

            // Extract the trace context from the AdditionalHeader property of the ExtensionObject.
            var extractedContext = _extractionAdapter.ExtractTraceContext(traceData);

            // Verify that the trace context is propagated.
            Assert.AreEqual(Activity.Current.Context.TraceId, extractedContext.ActivityContext.TraceId);
            Assert.AreEqual(Activity.Current.Context.SpanId, extractedContext.ActivityContext.SpanId);
        }

        /// <summary>
        /// Configures Activity Listener and registers with Activity Source.
        /// </summary>
        private void StartActivityListener(bool shouldListenToAllSources = false, bool shouldWriteStartAndStop = true)
        {
            // Create an instance of ActivityListener and configure its properties
            ActivityListener activityListener = new ActivityListener() {

                // Set ShouldListenTo property to true for all activity sources
                ShouldListenTo = (source) => shouldListenToAllSources || source.Name.Equals(TraceableSession.ActivitySourceName),

                // Sample all data and recorded activities
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,

            };

            if (shouldWriteStartAndStop)
            {
                activityListener.ActivityStarted = activity => Utils.LogInfo("Started: {0,-15} {1,-60}", activity.OperationName, activity.Id);
                activityListener.ActivityStopped = activity => Utils.LogInfo("Stopped: {0,-15} {1,-60} Duration: {2}", activity.OperationName, activity.Id, activity.Duration);
            }

            ActivitySource.AddActivityListener(activityListener);
        }

    }
}
