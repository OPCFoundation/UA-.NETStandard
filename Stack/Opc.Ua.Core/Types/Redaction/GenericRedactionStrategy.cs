using System;
namespace Opc.Ua.Redaction
{
    internal class GenericRedactionStrategy<T> : RedactionStrategyBase, IRedactionStrategy<T>
    {
        private readonly int m_numberOfVisibleChars;
        private readonly int m_maxLength;

        public GenericRedactionStrategy(int numberOfVisibleChars) : this(numberOfVisibleChars, 0)
        { }

        public GenericRedactionStrategy(int numberOfVisibleChars, int maxLength)
        {
            m_numberOfVisibleChars = numberOfVisibleChars;
            m_maxLength = maxLength;
        }

        public string Redact(T value)
        {
            string result = value?.ToString();

            if (result is null)
            {
                return "null";
            }

            return RedactStringWithVisibleChars(result, m_numberOfVisibleChars, m_maxLength);
        }
    }
}
