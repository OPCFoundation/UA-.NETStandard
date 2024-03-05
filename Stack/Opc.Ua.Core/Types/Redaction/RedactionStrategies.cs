using System;

namespace Opc.Ua.Redaction
{
    /// <summary>
    /// Collection of the most common redaction strategies.
    /// The redaction (censoring) can be applied to log message parameters, exception messages, and other sensitive data.
    /// <br/>
    /// The redaction is off by default and can be enabled by setting <see cref="Redact.IsEnabled"/> to true.
    /// <br/>
    /// To use a strategy wrap the value with <see cref="RedactionWrapper{T}"/>, e.g.
    /// <code>
    /// Utils.LogDebug("The password is {0}", RedactionWrapper.Create(password, RedactionStrategies.PasswordStrategy));
    /// Utils.LogError("An exception occurred: {0}", RedactionWrapper.Create(exception, RedactionStrategies.DefaultStrategy)
    /// </code>
    /// </summary>
    public static partial class RedactionStrategies
    {
        private const int kSomeVisibleCharactersCount = 2;
        private const int kLengthRestrictionCharactersCount = 8;

        private readonly static UriRedactionStrategy s_uriStrategy = new UriRedactionStrategy();

        /// <summary>
        /// Gets a redaction strategy for a username.
        /// </summary>
        /// <remarks>
        /// If the string is longer than 2 chars, the first 2 chars will be shown and the rest will be replaced with *.
        /// The minimum length of the result is 8 chars, shorter results will be padded with *.
        /// Examples: "a" -> "********", "abc" -> "ab******", "username" -> "us******", null -> "null".
        /// </remarks>
        public static IRedactionStrategy<string> UsernameStrategy { get; } = new GenericRedactionStrategy<string>(numberOfVisibleChars: kSomeVisibleCharactersCount);

        /// <summary>
        /// Gets the password redaction strategy.
        /// </summary>
        /// <remarks>
        /// The exact length of the result is 8 chars, shorter results will be padded with *.
        /// Examples: "1234" -> "********", "1234567890" -> "**********", null -> "null".
        /// </remarks>
        public static IRedactionStrategy<string> PasswordStrategy { get; } = new GenericRedactionStrategy<string>(numberOfVisibleChars: 0, maxLength: kLengthRestrictionCharactersCount);

        /// <summary>
        /// Gets the endpoint redaction strategy.
        /// </summary>
        /// <remarks>
        /// If the string is longer than 3 chars, the first 3 chars will be shown and the rest will be replaced with *.
        /// The minimum length of the endpoint is 8 chars, shorter results will be padded with *.
        /// The maximum length of the endpoint host name is 10 chars.
        /// The port is preserved and not censored if the string includes the opc.tcp:// scheme.
        /// The optional prefix opc.tcp:// is preserved and does not count towards the maximum limit.
        /// Examples: "opc" -> "********", "opcplc:50000" -> "opc*******", "127.0.1.2:1234" -> "127*******".
        /// "opc.tcp://opc" -> "opc.tcp://********", "opc.tcp://opcplc:50000" -> "opc.tcp://opc*****:50000", "opc.tcp://127.0.1.2:1234" -> "opc.tcp://127******:1234".
        /// </remarks>
        public static IRedactionStrategy<string> EndpointStrategy { get; } = new EndpointRedactionStrategy();

        /// <summary>
        /// Gets the redaction strategy for a <see cref="Uri"/>.
        /// </summary>
        /// <remarks>
        /// See <see cref="EndpointStrategy"/> for details.
        /// </remarks>
        public static IRedactionStrategy<Uri> UriStrategy { get => s_uriStrategy; }

        /// <summary>
        /// Gets the redaction strategy for a <see cref="UriBuilder"/>.
        /// </summary>
        public static IRedactionStrategy<UriBuilder> UriBuilderStrategy { get => s_uriStrategy; }

        /// <summary>
        /// Gets a default redaction strategy for the given type.
        /// </summary>
        /// <remarks>
        /// If the string is longer than 2 chars, the first 2 chars will be shown and the rest will be replaced with *.
        /// The minimum length of the result is 8 chars, shorter results will be padded with *.
        /// Examples: "a" -> "********", "abc" -> "ab******", "username" -> "us******", null -> "null".
        /// </remarks>
        public static IRedactionStrategy<T> GetDefaultStrategy<T>(int minimumVisibleCharacters = -1, int maximumCharacterCount = 0)
        {
            return new GenericRedactionStrategy<T>(
                minimumVisibleCharacters == -1 ? kSomeVisibleCharactersCount: minimumVisibleCharacters,
                maximumCharacterCount);
        }

        private class NullRedactionStrategy<T> : IRedactionStrategy<T>
        {
            public string Redact(T value)
            {
                return value?.ToString() ?? "null";
            }
        }
    }
}
