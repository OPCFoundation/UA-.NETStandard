namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// An output argument of a method
    /// </summary>
    public class OutputArgument
    {
        /// <summary>
        /// Name of this argument
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Value of this argument
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// Method Name
        /// </summary>
        /// <returns>The Method Name</returns>
        public override string ToString() => Name;
    }
}
