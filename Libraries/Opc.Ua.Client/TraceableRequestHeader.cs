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

        private void UpdateTraceContext(ExtensionObject incomingValue)
        {
            if (Activity.Current != null)
            {
                var traceData = new Dictionary<string, string>();
                _activitySourceAdapter.InjectTraceContext(new PropagationContext(Activity.Current.Context, Baggage.Current), traceData);
                incomingValue.Body = ConvertToXmlElement(traceData);
            }
            base.AdditionalHeader = incomingValue;
        }

        private XmlElement ConvertToXmlElement(Dictionary<string, string> traceData)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("TraceableSessionTraceData");

            foreach (var kvp in traceData)
            {
                XmlElement element = doc.CreateElement(kvp.Key);
                element.InnerText = kvp.Value;
                root.AppendChild(element);
            }

            return root;
        }
    }
}
