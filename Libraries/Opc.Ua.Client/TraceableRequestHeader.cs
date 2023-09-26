using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Adapter for Traceable Session ActivitySource.
    /// </summary>
    public class TraceableRequestHeader : RequestHeader
    {
        /// <summary>
        /// The adapter for the activity source.
        /// </summary>
        private readonly TraceableSessionActivitySourceAdapter<int> _activitySourceAdapter;

        /// <summary>
        /// Initializes a new instance of the <see cref="TraceableRequestHeader"/> class.
        /// </summary>
        /// <param name="activitySourceAdapter"></param>
        public TraceableRequestHeader(TraceableSessionActivitySourceAdapter<int> activitySourceAdapter)
        {
            _activitySourceAdapter = activitySourceAdapter;
        }

        /// <inheritdoc/>
        public override ExtensionObject AdditionalHeader
        {
            get
            {
                return base.AdditionalHeader;
            }
            set
            {
                UpdateTraceContext(value);
            }
        }

        /// <summary>
        /// Converts the trace data to an Xml element.
        /// </summary>
        public static XmlElement ConvertTraceDataToXmlElement(Dictionary<string, string> traceData)
        {
            // Creating a new XmlDocument instance.
            XmlDocument xmlDoc = new XmlDocument();

            // Creating the root element for trace data.
            XmlElement root = xmlDoc.CreateElement("TraceableSessionTraceData");

            // Looping through each trace data key-value pair and converting them to Xml elements.
            foreach (var traceKVP in traceData)
            {
                XmlElement element = xmlDoc.CreateElement(traceKVP.Key);
                element.InnerText = traceKVP.Value;
                root.AppendChild(element);
            }

            return root;
        }

        private void UpdateTraceContext(ExtensionObject incomingValue)
        {
            if (Activity.Current != null)
            {
                var traceData = new Dictionary<string, string>();
                _activitySourceAdapter.InjectTraceContext(new PropagationContext(Activity.Current.Context, Baggage.Current), traceData);
                incomingValue.Body = ConvertTraceDataToXmlElement(traceData);
            }
            base.AdditionalHeader = incomingValue;
        }
    }
}
