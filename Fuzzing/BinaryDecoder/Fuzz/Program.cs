

using SharpFuzz;

namespace BinaryDecoder.Fuzz
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Fuzzer.Run(stream => {
                FuzzableCode.FuzzTarget(stream);
            });
        }
    }
}
