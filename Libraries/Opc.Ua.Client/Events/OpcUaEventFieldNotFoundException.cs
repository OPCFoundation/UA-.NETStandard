using System;

namespace Opc.Ua.Client.Events
{
    /// <summary>
    /// Exception when event field not found
    /// </summary>
    public class OpcUaEventFieldNotFoundException : Exception
    {
        private const string DefaultMessage = "Event field with given name not found!";

        /// <summary>
        /// Name of the event field
        /// </summary>
        public string EventFieldName { get; }

        /// <summary>
        /// Exception when event field not found
        /// </summary>
        public OpcUaEventFieldNotFoundException(string eventFieldName)
            : this(eventFieldName, "")
        {
            EventFieldName = eventFieldName;
        }

        /// <summary>
        /// Exception when event field not found
        /// </summary>
        public OpcUaEventFieldNotFoundException(string eventFieldName, string userMessage)
            : this(eventFieldName, userMessage, null)
        {

        }

        /// <summary>
        /// Exception when event field not found
        /// </summary>
        public OpcUaEventFieldNotFoundException() : this("")
        {

        }

        /// <summary>
        /// Exception when event field not found
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public OpcUaEventFieldNotFoundException(string message, Exception innerException) : this("", message, innerException)
        {

        }

        /// <summary>
        /// Exception when event field not found
        /// </summary>
        /// <param name="eventFieldName">Field that was not found</param>
        /// <param name="userMessage">userdefined message for the exception</param>
        /// <param name="innerException">Inner Exception</param>
        public OpcUaEventFieldNotFoundException(string eventFieldName, string userMessage, Exception innerException)
            : base($"{DefaultMessage}{eventFieldName}{userMessage}", innerException)
        {
            EventFieldName = eventFieldName;
        }
    }
}
