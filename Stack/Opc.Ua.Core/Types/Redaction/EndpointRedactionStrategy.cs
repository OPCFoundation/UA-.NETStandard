using System;

namespace Opc.Ua.Redaction
{
    internal class EndpointRedactionStrategy : UriRedactionStrategy, IRedactionStrategy<string>
    {
        private const int k_VisibleCharacters = 3;
        private const int k_MaximumCharacters = 10;

        public string Redact(string value)
        {
            if (value is null)
            {
                return "null";
            }

            if (Uri.IsWellFormedUriString(value, UriKind.Absolute))
            {
                return Redact(new Uri(value));
            }

            return RedactStringWithVisibleChars(value, k_VisibleCharacters, k_MaximumCharacters);
        }
    }
}
