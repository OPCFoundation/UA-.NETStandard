using System;

namespace Opc.Ua.Redaction
{
    /// <summary>
    /// Base class for redaction strategies.
    /// </summary>
    public abstract class RedactionStrategyBase
    {
        /// <summary>
        /// Placeholder text for <c>null</c> values.
        /// </summary>
        protected static readonly string NullPlaceHolder = "null";

        /// <summary>
        /// Censors a string.
        /// The minimum length of the result is 8 chars, shorter results will be padded with *.
        /// If <paramref name="numberOfVisibleChars"/> equals 0, the string will be fully censored.
        /// If <paramref name="maxLength"/> is greater than 0, the string length will be limited to that value.
        /// </summary>
        protected static string RedactStringWithVisibleChars(string input, int numberOfVisibleChars, int maxLength = 0)
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
    }
}
