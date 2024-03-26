using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Client.Methods;

namespace Opc.Ua.Client.Events
{
    /// <summary>
    /// Object for an event
    /// </summary>
    public class OpcEvent : IEvent
    {
        /// <summary>
        /// Event fields
        /// </summary>
        public IReadOnlyList<EventField> Fields { get; }

        private readonly NodeId _objectNodeId;
        private readonly byte[] _eventId;
        private readonly IEventMethodCollection _eventMethods;

        /// <summary>
        /// Constructor
        /// </summary>
        public OpcEvent(IEventMethodCollection eventMethods, IEnumerable<EventField> fields)
        {
            Fields = fields.ToList();
            _eventMethods = eventMethods;
            _objectNodeId = GetEventField<NodeId>("ConditionNodeId", x => x.Name.Equals("ConditionId", StringComparison.Ordinal));
            _eventId = GetEventField<byte[]>("EventId", x => x.Name.Equals("/EventId", StringComparison.Ordinal));
        }

        /// <summary>
        /// Acknowledge this event
        /// </summary>
        public MethodCallReturnValue Acknowledge(LocalizedText comment)
        {
            return _eventMethods.Acknowledge.CallMethod(_objectNodeId, _eventId, comment);
        }

        /// <summary>
        /// Confirm this event
        /// </summary>
        public MethodCallReturnValue Confirm(LocalizedText comment)
        {
            return _eventMethods.Confirm.CallMethod(_objectNodeId, _eventId, comment);
        }

        /// <summary>
        /// Add comment to this event
        /// </summary>
        public MethodCallReturnValue AddComment(LocalizedText comment)
        {
            return _eventMethods.AddComment.CallMethod(_objectNodeId, _eventId, comment);
        }

        private T GetEventField<T>(string eventFieldName, Func<EventField, bool> searchCriteria)
        {
            var relevantField = Fields.FirstOrDefault(searchCriteria);
            if (relevantField is null)
            {
                throw new OpcUaEventFieldNotFoundException(eventFieldName);
            }
            return (T)relevantField.Value.Value;
        }
    }
}
