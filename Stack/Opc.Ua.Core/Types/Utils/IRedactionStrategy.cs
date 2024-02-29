namespace Opc.Ua
{
    /// <summary>
    /// Represents a redaction (censoring) strategy.
    /// </summary>
    /// <remarks>
    /// Use redaction to hide sensitive data in log messages, exception messages, etc.
    /// The redaction is off by default and can be enabled by setting <see cref="Redact.IsEnabled"/> to true.
    /// </remarks>
    /// <typeparam name="T">The type supported.</typeparam>
    public interface IRedactionStrategy<T>
    {
        /// <summary>
        /// Returns a string representation of <paramref name="value"/> without sensitive data.
        /// </summary>
        string Redact(T value);
    }
}
