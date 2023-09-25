using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTelemetry.Context.Propagation;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Adapter for Traceable Session ActivitySource.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TraceableSessionActivitySourceAdapter<T>
    {
        private readonly ActivitySource _activitySource;
        private readonly string _className;
        private readonly ConcurrentDictionary<string, string> _activityNameDict;
        private readonly TraceContextPropagator _traceContextPropagator = new TraceContextPropagator();

        /// <summary>
        /// Initializes a new instance of the <see cref="TraceableSessionActivitySourceAdapter{T}"/> class.
        /// </summary>
        /// <param name="activitySource"></param>
        public TraceableSessionActivitySourceAdapter(ActivitySource activitySource)
        {
            _activitySource = activitySource;
            _className = typeof(T).Name;
            _activityNameDict = new ConcurrentDictionary<string, string>();
        }

        /// <inheritdoc/>
        public Activity StartActivity([CallerMemberName] string callingMethod = "", bool isRoot = false, ActivityContext parentContext = default(ActivityContext), params KeyValuePair<string, object>[] tags)
        {
            if (isRoot)
            {
                Activity.Current = null;
            }

            var activity = _activitySource.StartActivity(GetActivityName(callingMethod), ActivityKind.Internal, parentContext, tags);
            if (activity == null)
            {
                throw new InvalidOperationException("Failed to start the activity.");
            }
            return activity;

        }

        /// <inheritdoc/>
        public void InjectTraceContext(PropagationContext parentContext, IDictionary<string, string> userProperties)
        {
            _traceContextPropagator.Inject(parentContext, userProperties, (properties, key, value) => {
                properties.Add(key, value);
            });
        }

        /// <inheritdoc/>
        public PropagationContext ExtractTraceContext(IDictionary<string, string> userProperties)
        {
            return _traceContextPropagator.Extract(default, userProperties, (properties, key) => {
                if (properties?.TryGetValue(key, out string value) == true)
                {
                    return Enumerable.Repeat(value, 1);
                }

                return Enumerable.Empty<string>();
            });
        }

        /// <summary>
        /// Gets the activity name.
        /// </summary>
        private string GetActivityName(string methodName)
        {
            return _activityNameDict.GetOrAdd(methodName, key => $"{_className}.{key}");
        }
    }

}
