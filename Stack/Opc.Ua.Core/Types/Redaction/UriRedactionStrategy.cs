using System;
using System.Text;

namespace Opc.Ua.Redaction
{
    internal class UriRedactionStrategy : RedactionStrategyBase, IRedactionStrategy<Uri>, IRedactionStrategy<UriBuilder>
    {
        private const int MaxLength = 10;

        public string Redact(Uri input)
        {
            System.Diagnostics.Debug.WriteLine(input);
            if (input is null)
            {
                return "null";
            }

            if (!input.IsWellFormedOriginalString() || !input.IsAbsoluteUri || string.IsNullOrEmpty(input.Host))
            {
                return RedactStringWithVisibleChars(input.ToString(), numberOfVisibleChars: 3, MaxLength);
            }

            string redactedHost = RedactStringWithVisibleChars(input.Host, numberOfVisibleChars: 3, MaxLength);

            StringBuilder sb = new StringBuilder()
                .Append(input.Scheme)
                .Append(Uri.SchemeDelimiter)
                .Append(redactedHost);

            if (!input.IsDefaultPort)
            {
                sb.Append(':').Append(input.Port);
            }

            if (input.LocalPath != null && !string.Equals(input.LocalPath, "/", StringComparison.Ordinal))
            {
                sb.Append(input.LocalPath);
            }

            return sb.ToString();
        }

        public string Redact(UriBuilder value)
        {
            return Redact(value.Uri);
        }
    }
}
