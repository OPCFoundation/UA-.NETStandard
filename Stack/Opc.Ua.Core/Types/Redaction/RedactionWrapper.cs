namespace Opc.Ua.Redaction
{
    /// <summary>
    /// Wraps the supplied value and applies the redaction strategy when converting to string.
    /// </summary>
    /// <remarks>
    /// Enable redaction by setting <see cref="Redact.IsEnabled"/> to <c>true</c>.
    /// </remarks>
    /// <typeparam name="T">Type of the supplied value.</typeparam>
    public class RedactionWrapper<T>
    {
        private readonly T m_value;
        private readonly IRedactionStrategy<T> m_strategy;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedactionWrapper{T}"/> class with the default redaction strategy.
        /// </summary>
        public RedactionWrapper(T value) : this(value, RedactionStrategies.GetDefaultStrategy<T>()) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedactionWrapper{T}"/> class.
        /// </summary>
        public RedactionWrapper(T value, IRedactionStrategy<T> strategy)
        {
            m_value = value;
            m_strategy = strategy;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Redact.IsEnabled ? m_strategy.Redact(m_value) : m_value?.ToString() ?? "null";
        }
    }
}
