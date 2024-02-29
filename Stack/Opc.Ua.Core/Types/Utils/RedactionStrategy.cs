using System;
using System.Globalization;

namespace Opc.Ua
{
    /// <summary>
    /// Collection of the most common redaction strategies.
    /// The redaction (censoring) can be applied to log message parameters, exception messages, and other sensitive data.
    /// <br/>
    /// The redaction is off by default and can be enabled by setting <see cref="Redact.IsEnabled"/> to true.
    /// <br/>
    /// To use a strategy wrap the value with <see cref="RedactionWrapper{T}"/>, e.g.
    /// <code>
    /// Utils.LogDebug("The password is {0}", RedactionWrapper.Create(password, RedactionStrategy.PasswordStrategy));
    /// Utils.LogError("An exception occurred: {0}", RedactionWrapper.Create(exception, RedactionStrategy.DefaultStrategy)
    /// </code>
    /// </summary>
    public static class RedactionStrategy
    {
        private const int kSomeVisibleCharactersCount = 2;
        private const int kLengthRestrictionCharactersCount = 8;

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
        public static IRedactionStrategy<string> EndpointStrategy { get; } = new EndpointRedactionStrategy<string>();

        /// <summary>
        /// Gets a default redaction strategy for the given type.
        /// </summary>
        /// <remarks>
        /// If the string is longer than 2 chars, the first 2 chars will be shown and the rest will be replaced with *.
        /// The minimum length of the result is 8 chars, shorter results will be padded with *.
        /// Examples: "a" -> "********", "abc" -> "ab******", "username" -> "us******", null -> "null".
        /// </remarks>
        public static IRedactionStrategy<T> GetDefaultStrategy<T>()
        {
            return new GenericRedactionStrategy<T>(kSomeVisibleCharactersCount);
        }

        /// <summary>
        /// Censors a string.
        /// The minimum length of the result is 8 chars, shorter results will be padded with *.
        /// If <paramref name="numberOfVisibleChars"/> equals 0, the string will be fully censored.
        /// If <paramref name="maxLength"/> is greater than 0, the string length will be limited to that value.
        /// </summary>
        private static string RedactStringWithVisibleChars(string input, int numberOfVisibleChars, int maxLength = 0)
        {
            const int MinLength = 8;

            if (input is null)
            {
                return "null";
            }

            string redacted = input.Length < numberOfVisibleChars + 1
                ? new string('*', input.Length)
                : $"{input.Substring(0, numberOfVisibleChars)}{new string('*', input.Length - numberOfVisibleChars)}";

            // Limit length to MaxLength chars.
            if (maxLength > 0)
            {
                redacted = redacted.Length > maxLength
                    ? redacted.Substring(0, maxLength)
                    : redacted;
            }

            return $"{redacted}{new string('*', Math.Max(0, MinLength - redacted.Length))}";
        }

        private class GenericRedactionStrategy<T> : IRedactionStrategy<T>
        {
            private readonly int _numberOfVisibleChars;
            private readonly int _maxLength;

            public GenericRedactionStrategy(int numberOfVisibleChars) : this(numberOfVisibleChars, 0)
            { }

            public GenericRedactionStrategy(int numberOfVisibleChars, int maxLength)
            {
                _numberOfVisibleChars = numberOfVisibleChars;
                _maxLength = maxLength;
            }

            public string Redact(T value)
            {
                string result = value?.ToString();

                if (result is null)
                {
                    return "null";
                }

                return RedactStringWithVisibleChars(result, _numberOfVisibleChars, _maxLength);
            }
        }

        private class NullRedactionStrategy<T> : IRedactionStrategy<T>
        {
            public string Redact(T value)
            {
                return value?.ToString() ?? "null";
            }
        }

        private class EndpointRedactionStrategy<T> : IRedactionStrategy<T>
        {
            public string Redact(T value)
            {
                string input = value?.ToString();

                if (input is null)
                {
                    return "null";
                }

                const int MaxLength = 10;
                const int SchemeLength = 10; // Length of "opc.tcp://"
                string prefix = string.Empty;
                string port = string.Empty;

                // Remove prefix before redacting.
                if (input.Length > SchemeLength && string.Equals(input.Substring(0, SchemeLength), "opc.tcp://", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = "opc.tcp://";

                    if (Uri.IsWellFormedUriString(input, UriKind.Absolute))
                    {
                        // Try to separate host from port and censor only the host.
                        var uri = new Uri(input);
                        input = uri.Host;
                        port = uri.Port.ToString(CultureInfo.InvariantCulture);

                        port = port != "-1"
                            ? $":{port}"
                            : string.Empty;
                    }
                    else
                    {
                        // Fallback.
                        input = input.Substring(SchemeLength);
                    }
                }

                string redacted = RedactStringWithVisibleChars(input, numberOfVisibleChars: 3, MaxLength);

                return $"{prefix}{redacted}{port}";
            }
        }
    }
}
