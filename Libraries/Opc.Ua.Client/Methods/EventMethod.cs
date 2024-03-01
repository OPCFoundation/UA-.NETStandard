using System.Collections.Generic;

namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// A standard event method
    /// </summary>
    internal class EventMethod : MethodBase, IEventMethod
    {
        internal EventMethod(ISession session, NodeId methodNodeId) : base(session, methodNodeId)
        {
        }

        /// <summary>
        /// Call this method
        /// </summary>
        public MethodCallReturnValue CallMethod(NodeId objectNodeId, byte[] eventId, LocalizedText comment)
        {
            return Call(objectNodeId, new List<Variant> { eventId, comment });
        }
    }
}
