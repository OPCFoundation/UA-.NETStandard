using System.Collections.Generic;
using Opc.Ua.Client.Methods;

namespace Opc.Ua.Client.Events
{
    /// <summary>
    /// Interface of an event
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// Event fields
        /// </summary>
        IReadOnlyList<EventField> Fields { get; }

        /// <summary>
        /// Acknowledge
        /// </summary>
        MethodCallReturnValue Acknowledge(LocalizedText comment);

        /// <summary>
        /// Confirm
        /// </summary>
        MethodCallReturnValue Confirm(LocalizedText comment);

        /// <summary>
        /// Add comment to this event
        /// </summary>
        MethodCallReturnValue AddComment(LocalizedText comment);
    }
}
