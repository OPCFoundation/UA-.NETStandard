namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// Method collection for standard events
    /// </summary>
    public class EventMethodCollection : IEventMethodCollection
    {
        /// <summary>
        /// Acknowledge
        /// </summary>
        public IEventMethod Acknowledge { get; }

        /// <summary>
        /// Confirm
        /// </summary>
        public IEventMethod Confirm { get; }

        /// <summary>
        /// Add comment
        /// </summary>
        public IEventMethod AddComment { get; }

        internal EventMethodCollection(ISession session)
        {
            Acknowledge = new EventMethod(session, MethodIds.AcknowledgeableConditionType_Acknowledge);
            Confirm = new EventMethod(session, MethodIds.AcknowledgeableConditionType_Confirm);
            AddComment = new EventMethod(session, MethodIds.ConditionType_AddComment);
        }
    }
}
