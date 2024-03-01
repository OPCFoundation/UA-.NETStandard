namespace Opc.Ua.Client.Events
{
    /// <summary>
    /// Interface for an object containing the session
    /// </summary>
    public interface ISessionContainer
    {
        /// <summary>
        /// The contained session object
        /// </summary>
        ISession Session { get; set; }
    }
}
