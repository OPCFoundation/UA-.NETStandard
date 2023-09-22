using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTelemetry.Context.Propagation;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Adapter for Traceable Session ActivitySource.
    /// </summary>
    public interface ITraceableSessionActivitySourceAdapter
    {
        /// <summary>
        /// Starts a new activity.
        /// </summary>
        Activity StartActivity(string activityName);

        /// <summary>
        /// Starts a new asset sourced activity.
        /// </summary>
        Activity StartAssetSourcedActivity(string activityName);

        /// <summary>
        /// Injects the current trace context into a user properties dictionary.
        /// </summary>
        PropagationContext ExtractTraceContext(IDictionary<string, string>? userProperties);

        /// <summary>
        /// Extracts a trace context from a user properties dictionary.
        /// </summary>
        void InjectTraceContext(PropagationContext parentContext, IDictionary<string, string> userProperties);

    }

}
