
using System.IO;
using System.Text;
using Opc.Ua;

public static partial class Testcases
{

    public enum TestCaseEncoders : int
    {
        Binary=0,
        Json=1,
        Xml=2
    };

    public static string[] TestcaseEncoderSuffixes = new string[] { ".Binary", ".Json", ".Xml" };

    public static void Run(string directoryPath)
    {
        string workPath = Path.TrimEndingDirectorySeparator(directoryPath);

        // Create the Testcases for the binary decoder.
        string pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Binary];
        string pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        foreach (var messageEncoder in MessageEncoders)
        {
            byte[] message;
            using (var encoder = new BinaryEncoder(MessageContext))
            {
                messageEncoder(encoder);
                message = encoder.CloseAndReturnBuffer();
            }

            // Test the fuzz targets with the message.
            FuzzableCode.LibfuzzBinaryDecoder(message);
            FuzzableCode.LibfuzzBinaryEncoder(message);
            using (var stream = new MemoryStream(message))
            {
                FuzzableCode.AflfuzzBinaryDecoder(stream);
            }
            using (var stream = new MemoryStream(message))
            {
                FuzzableCode.AflfuzzBinaryEncoder(stream);
            }
            using (var stream = new MemoryStream(message))
            {
                FuzzableCode.FuzzBinaryDecoderCore(stream, true);
            }

            string fileName = Path.Combine(pathTarget, $"{messageEncoder.Method.Name}.bin".ToLowerInvariant());
            File.WriteAllBytes(fileName, message);
        }

        // Create the Testcases for the json decoder.
        pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Json];
        pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        foreach (var messageEncoder in MessageEncoders)
        {
            byte[] message;
            using (var memoryStream = new MemoryStream(0x1000))
            using (var encoder = new JsonEncoder(MessageContext, true, false, memoryStream))
            {
                messageEncoder(encoder);
                encoder.Close();
                message = memoryStream.ToArray();
            }


            // Test the fuzz targets with the message.
            FuzzableCode.LibfuzzJsonDecoder(message);
            FuzzableCode.LibfuzzJsonEncoder(message);
            string json = Encoding.UTF8.GetString(message);
            FuzzableCode.AflfuzzJsonDecoder(json);
            FuzzableCode.AflfuzzJsonEncoder(json);
            FuzzableCode.FuzzJsonDecoderCore(json, true);

            string fileName = Path.Combine(pathTarget, $"{messageEncoder.Method.Name}.json".ToLowerInvariant());
            File.WriteAllBytes(fileName, message);
        }

        // Create the Testcases for the xml decoder.
        pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Xml];
        pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        foreach (var messageEncoder in MessageEncoders)
        {
            string message;
            using (var encoder = new XmlEncoder(MessageContext))
            {
                encoder.SetMappingTables(MessageContext.NamespaceUris, MessageContext.ServerUris);
                messageEncoder(encoder);
                message = encoder.CloseAndReturnText();
            }

            // Test the fuzz targets with the message.

            string fileName = Path.Combine(pathTarget, $"{messageEncoder.Method.Name}.xml".ToLowerInvariant());
            File.WriteAllBytes(fileName, Encoding.UTF8.GetBytes(message));
        }
    }
}
