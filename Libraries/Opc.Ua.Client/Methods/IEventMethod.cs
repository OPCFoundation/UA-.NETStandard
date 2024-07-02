namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// A standard event method
    /// </summary>
    public interface IEventMethod
    {
        /// <summary>
        /// Call this method and Return a MethodCallReturnValue
        /// </summary>
        /// <param name="objectNodeId">the Object NodeId</param>
        /// <param name="eventId">the EventId</param>
        /// <param name="comment">Comment to set for the event</param>
        /// <returns>MethodCallReturnValue</returns>
        MethodCallReturnValue CallMethod(NodeId objectNodeId, byte[] eventId, LocalizedText comment);
    }
}
