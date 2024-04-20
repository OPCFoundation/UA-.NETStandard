

using SharpFuzz;

public static class Program
{
    public static void Main(string[] args)
    {
#if LIBFUZZER
        Fuzzer.LibFuzzer.Run(input => {
            FuzzableCode.FuzzTargetLibfuzzer(input);
        });
#else
        Fuzzer.Run(stream => {
            FuzzableCode.FuzzTarget(stream);
        });
#endif
    }
}
