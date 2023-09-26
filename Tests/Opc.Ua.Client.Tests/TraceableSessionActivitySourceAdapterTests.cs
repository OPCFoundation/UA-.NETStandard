using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
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
            var _injectionAdapter = new TraceableSessionActivitySourceAdapter<int>(new ActivitySource(TraceableSession.ActivitySourceName));
            Activity intectionTestActivity =_injectionAdapter.StartActivity("Trace Propogation Test Activity");
            intectionTestActivity.Should().NotBeNull();

            var traceData = new Dictionary<string, string>();
            _injectionAdapter.InjectTraceContext(new PropagationContext(Activity.Current.Context, Baggage.Current), traceData);
            var incomingValue = new ExtensionObject();

            var _extractionAdapter = new TraceableSessionActivitySourceAdapter<int>(new ActivitySource("Extract-Activity-Source"));
            var requestHeader = new TraceableRequestHeader(_extractionAdapter);
            incomingValue.Body = TraceableRequestHeader.ConvertTraceDataToXmlElement(traceData);
            requestHeader.AdditionalHeader = incomingValue;

            var extractedContext = _extractionAdapter.ExtractTraceContext(traceData);
            extractedContext.ActivityContext.TraceId.Should().Be(Activity.Current.Context.TraceId);
            extractedContext.ActivityContext.SpanId.Should().Be(Activity.Current.Context.SpanId);
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
