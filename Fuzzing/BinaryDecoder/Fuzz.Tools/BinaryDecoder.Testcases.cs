
using System.IO;
using Opc.Ua;

public static partial class Testcases
{
    public static void Run(string directoryPath)
    {
        foreach (var messageEncoder in Testcases.MessageEncoders)
        {
            var encoder = new BinaryEncoder(Testcases.MessageContext);
            messageEncoder(encoder);
            var message = encoder.CloseAndReturnBuffer();
            FuzzTestcase(message);
            File.WriteAllBytes(Path.Combine(directoryPath, $"{messageEncoder.Method.Name}.bin".ToLowerInvariant()), message);
        }
    }
}
