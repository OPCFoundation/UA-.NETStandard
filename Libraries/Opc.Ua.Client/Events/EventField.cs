namespace Opc.Ua.Client.Events
{
    /// <summary>
    /// Object representing an event field
    /// </summary>
    public class EventField
    {
        /// <summary>
        /// Name of this event field
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Value of this event
        /// </summary>
        public Variant Value { get; }

        /// <summary>
        /// Object representing an event field
        /// </summary>
        /// <param name="name">Name of the field</param>
        /// <param name="value">Value of the field</param>
        public EventField(string name, Variant value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>
        /// String representation: [Name]: [Value]
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"{Name}: {Value}";
        }
    }
}
