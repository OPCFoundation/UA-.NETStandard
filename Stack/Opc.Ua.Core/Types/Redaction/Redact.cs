using System;

namespace Opc.Ua.Redaction
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
        /// the <see cref="RedactionStrategies.UsernameStrategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<string> Username(string username)
        {
            return new RedactionWrapper<string>(username, RedactionStrategies.UsernameStrategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="password"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the <see cref="RedactionStrategies.PasswordStrategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<string> Password(string password)
        {
            return new RedactionWrapper<string>(password, RedactionStrategies.PasswordStrategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="endpoint"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the <see cref="RedactionStrategies.EndpointStrategy"/> will be invoked to redact the sensitive data.
        /// </summary>
        public static RedactionWrapper<string> Endpoint(string endpoint)
        {
            return new RedactionWrapper<string>(endpoint, RedactionStrategies.EndpointStrategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="message"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the result will be redacted to 10 characters.
        /// </summary>
        public static RedactionWrapper<string> ExceptionMessage(string message)
        {
            return new RedactionWrapper<string>(message, RedactionStrategies.GetDefaultStrategy<string>(10));
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="uriBuilder"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the result will be redacted to according to the <see cref="RedactionStrategies.UriBuilderStrategy"/>.
        /// </summary>
        /// <param name="uriBuilder"></param>
        public static RedactionWrapper<UriBuilder> Uri(UriBuilder uriBuilder)
        {
            return new RedactionWrapper<UriBuilder>(uriBuilder, RedactionStrategies.UriBuilderStrategy);
        }

        /// <summary>
        /// Creates a wrapper to hold <paramref name="uri"/>.
        /// When <see cref="object.ToString()"/> is called on the resulting wrapper
        /// the result will be redacted to according to the <see cref="RedactionStrategies.UriStrategy"/>.
        /// </summary>
        public static RedactionWrapper<Uri> Uri(Uri uri)
        {
            return new RedactionWrapper<Uri>(uri, RedactionStrategies.UriStrategy);
        }
    }
}
