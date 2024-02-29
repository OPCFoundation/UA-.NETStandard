namespace Opc.Ua
{
    /// <summary>
    /// Helper class for redacting sensitive data.
    /// </summary>
    public static class Redact
    {
        /// <summary>
        /// Gets or sets if the redaction is enabled.
        /// </summary>
        public static bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Creates a wrapper to hold <paramref name="value"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the supplied <paramref name="strategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<T> Default<T>(T value, IRedactionStrategy<T> strategy)
        {
            return new RedactionWrapper<T>(value, strategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="username"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the <see cref="RedactionStrategy.UsernameStrategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<string> Username(string username)
        {
            return new RedactionWrapper<string>(username, RedactionStrategy.UsernameStrategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="password"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the <see cref="RedactionStrategy.PasswordStrategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<string> Password(string password)
        {
            return new RedactionWrapper<string>(password, RedactionStrategy.PasswordStrategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="endpoint"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the <see cref="RedactionStrategy.EndpointStrategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<string> Endpoint(string endpoint)
        {
            return new RedactionWrapper<string>(endpoint, RedactionStrategy.EndpointStrategy);
        }
    }
}
