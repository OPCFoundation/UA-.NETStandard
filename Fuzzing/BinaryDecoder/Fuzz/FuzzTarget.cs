

using System.IO;
using Opc.Ua;

namespace BinaryDecoder.Fuzz
{
    public static class FuzzableCode
    {
        private static ServiceMessageContext messageContext = null;

        public static void FuzzTarget(Stream stream)
        {
            if (messageContext == null)
            {
                messageContext = new ServiceMessageContext();
            }

            try
            {
                using (var decoder = new Opc.Ua.BinaryDecoder(stream, messageContext))
                {
                    _ = decoder.DecodeMessage(null);
                }
            }
            catch (ServiceResultException)
            {

            }
        }
    }
}

#if mist
    public static class FuzzableCode
    {
        //public static void FuzzTargetMethod(ReadOnlySpan<byte> input)
        public static void FuzzTargetMethod(byte[] input)
        {
            try
            {
                var messageContext = new ServiceMessageContext();
                using (var decoder = new BinaryDecoder(input, messageContext))
                {
                    decoder.DecodeMessage(null);
                }
            }
            catch (Exception ex) when (ex is ServiceResultException)
            {
                // This is an example. You should filter out any
                // *expected* exception(s) from your code here,
                // but it’s an anti-pattern to catch *all* Exceptions,
                // as you might suppress legitimate problems, such as 
                // your code throwing a NullReferenceException.
            }
        }
    }
#endif

