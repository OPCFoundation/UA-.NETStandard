namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// Method collection for standard events
    /// </summary>
    public interface IEventMethodCollection
    {
        /// <summary>
        /// Acknowledge
        /// </summary>
        IEventMethod Acknowledge { get; }

        /// <summary>
        /// Confirm
        /// </summary>
        IEventMethod Confirm { get; }

        /// <summary>
        /// Add comment
        /// </summary>
        IEventMethod AddComment { get; }
    }
}
