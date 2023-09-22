using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTelemetry.Context.Propagation;

namespace Opc.Ua.Client
{
    /// <inheritdoc/>
    public class TraceableSessionActivitySourceAdapter : ITraceableSessionActivitySourceAdapter
    {
        private readonly string _className;
        private readonly ConcurrentDictionary<string, string> _activityNameDict;

        /// <summary>
        /// Initializes a new instance of the <see cref="TraceableSessionActivitySourceAdapter{T}"/> class.
        /// </summary>
        public TraceableSessionActivitySourceAdapter()
        {
            _className = nameof(TraceableSession);
            _activityNameDict = new ConcurrentDictionary<string, string>();
        }

        /// <inheritdoc/>
        public Activity StartActivity(string activityName)
        {
            return TraceableSession.ActivitySource.StartActivity(GetActivityName(activityName), ActivityKind.Internal)!;
        }

        /// <inheritdoc/>
        public Activity StartAssetSourcedActivity(string activityName)
        {
            KeyValuePair<string, object?>[] tags = { new KeyValuePair<string, object?>(ActivityTagNames.AssetSourced, null) };
            return TraceableSession.ActivitySource.StartActivity(GetActivityName(activityName), ActivityKind.Internal, parentContext: default, tags)!;
        }

        /// <inheritdoc/>
        public void InjectTraceContext(PropagationContext parentContext, IDictionary<string, string> userProperties)
        {
            // Assuming TraceContextPropagator is a global or static instance or provided from somewhere.
            TraceContextPropagator.Inject(parentContext, userProperties, (userProperties, key, value) => {
                userProperties.Add(key, value);
            });
        }

        /// <inheritdoc/>
        public PropagationContext ExtractTraceContext(IDictionary<string, string>? userProperties)
        {
            return TraceContextPropagator.Extract(default, userProperties, (userProperties, key) => {
                if (userProperties?.TryGetValue(key, out string? value) == true)
                {
                    return Enumerable.Repeat(value, 1);
                }
                return Enumerable.Empty<string>();
            });
        }

        /// <inheritdoc/>
        private string GetActivityName(string methodName)
        {
            return _activityNameDict.GetOrAdd(methodName, key => $"{_className}.{key}");
        }
    }


}
