// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Native.QuicGo.Tests;

bool expectIncompatible = args.Length == 2 && args[0] == "--expect-incompatible";
string path = expectIncompatible ? args[1] : args.Length == 1 ? args[0] : string.Empty;
if (!Path.IsPathFullyQualified(path))
{
    Console.Error.WriteLine("Supply the absolute reviewed native library path.");
    return 2;
}
using FileStream stream = File.OpenRead(path);
byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
var backend = new NativeQuicBackend(new NativeQuicLibrary(path, new ByteString(hash)));
if (expectIncompatible)
{
    try
    {
        await backend.GetCountsAsync().ConfigureAwait(false);
        Console.Error.WriteLine("An older native interface was incorrectly accepted.");
        return 1;
    }
    catch (InvalidDataException ex) when (
        ex.Message.StartsWith("Incompatible native QUIC ABI.", StringComparison.Ordinal))
    {
        Console.WriteLine("PASS OlderNativeInterfaceRejectedBeforeOperationAdmission");
        return 0;
    }
}
int checks = await NativeQuicLiveScenario.RunAsync(backend,
    static name => Console.WriteLine("PASS " + name)).ConfigureAwait(false);
Console.WriteLine($"PASS {checks} actual native managed checks; all ownership released.");
return 0;
