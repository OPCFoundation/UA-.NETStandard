using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using OpenTelemetry.Context.Propagation;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Adapter for Traceable Session ActivitySource.
    /// </summary>
    public interface ITraceableSessionActivitySourceAdapter<T>
    {
        /// <summary>
        /// Starts a new activity.
        /// </summary>
        Activity StartActivity([CallerMemberName] string callingMethod = "", ActivityContext parentContext = default(ActivityContext), params KeyValuePair<string, object>[] tags);

        /// <summary>
        /// Injects the current trace context into a user properties dictionary.
        /// </summary>
        void InjectTraceContext(PropagationContext parentContext, IDictionary<string, string> userProperties);

        /// <summary>
        /// Extracts a trace context from a user properties dictionary.
        /// </summary>
        PropagationContext ExtractTraceContext(IDictionary<string, string> userProperties);

    }

}
