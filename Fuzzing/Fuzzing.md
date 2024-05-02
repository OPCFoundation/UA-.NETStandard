# Fuzz testing for UA.NET Standard

This project provides integration of Sharpfuzz with the UA.NET Standard library encoders with support for both afl-fuzz and libfuzzer.

Fuzzers for the following decoders are located in the `Fuzzing` directory:
- BinaryDecoder
- JsonDecoder
- XmlDecoder (planned)
- CRL and Certificate decoder functions using the .NET ASN.1 decoder (planned)

Most of the supporting code is shared between all projects, only the project names and the fuzzable support functions differ.

A Tools application supports recreation of the `Testcases` and to replay the test cases that caused the fuzzer to crash or to hang. The application is located in the `*.Fuzz.Tools` folders.

## Installation for afl-fuzz and libfuzzer on Linux

Both fuzzers are supported on Linux. afl-fuzz can be compiled on any Linux system, while for libfuzzer prebuilt binaries are available for Debian and Ubuntu. The instructions were tested on a WSL subsystem on Windows with a Ubuntu installation.

### Extra step to run the fuzzers on Windows on a linux subsystem (WSL)

- Install the Windows Subsystem for Linux (WSL) by following the instructions at https://docs.microsoft.com/en-us/windows/wsl/install or by installing e.g. the Ubuntu app from the Microsoft store.

### Installation of required tools

The full instructions for setting up sharpfuzz can be found at this [README](https://github.com/Metalnem/sharpfuzz/blob/master/README.md).
The following steps are required to set up the environment: 

- Open a terminal and run the following commands to install the required packages to compile afl-fuzz:

```bash
cd <your project root>/Fuzzing

sudo apt-get update

# Install clang and llvm
sudo apt-get install -y build-essential cmake git

# Install .NET 8.0 SDK on Ubuntu
sudo apt-get install -y dotnet-sdk-8.0
```

The supplied scripts require powershell on Linux to be installed. 
See [Powershell install on Linux](https://learn.microsoft.com/en-us/powershell/scripting/install/install-ubuntu?view=powershell-7.4).

To compile and install afl-fuzz and to install sharpfuzz, run the following commands:

```bash
#/bin/sh
set -eux

# Download and extract the latest afl-fuzz source package
wget http://lcamtuf.coredump.cx/afl/releases/afl-latest.tgz
tar -xvf afl-latest.tgz

rm afl-latest.tgz
cd afl-2.52b/

# Install afl-fuzz
sudo make install
cd ..
rm -rf afl-2.52b/

# Install SharpFuzz.CommandLine global .NET tool
dotnet tool install --global SharpFuzz.CommandLine
```

To validate that all required tools are available and working, run the following commands:

```bash
afl-fuzz --help
sharpfuzz
```

## Libfuzzer

### Installation for libfuzzer on Windows

Install the latest dotnet SDK and runtime from https://dotnet.microsoft.com/download/dotnet/.

```commandline
# Install SharpFuzz.CommandLine global .NET tool
dotnet tool install --global SharpFuzz.CommandLine
```

### Usage of libfuzzer on Windows and Linux

First download the latest libfuzzer-dotnet release for Ubuntu, Debian or Windows from the [project website](https://github.com/Metalnem/libfuzzer-dotnet/releases) and store it in the same folder where the libfuzz command and shell scripts are started.

To run a fuzz target with libfuzzer on Windows, from your github cloned project root, execute the following commands:

On Windows, execute the following commands in a Powershell window:
```powershell
cd Fuzzing/Encoders
./libfuzz.bat
```

On Linux, execute the following commands in a bash window:

```bash
cd Fuzzing/Encoders
./libfuzz.sh
```

A menu will show up to allow the selection of a fuzzer target function to execute.

Now the fuzzer is started and runs until it hits a crash or timeout or until it is stopped manually by hitting Ctrl-C. Libfuzz writes findings in the current directory with the prefix crash- or timeout-.

## Afl-fuzz

### Usage of afl-fuzz on Linux

To run the afl-fuzz fuzzing project, execute the following commands:

```bash
cd Fuzzing/Encoders
./aflfuzz.sh
```

A menu will show up to allow the selection of a fuzzer target function to execute.

Now the fuzzer is started and runs until it is stopped manually by hitting Ctrl-C. The fuzzer will create a directory `findings` in the fuzzer directory, which contains the test cases that caused the fuzzer to crash. 

## Replay of crashes and timeouts with Visual Studio

To replay the test cases that cause the fuzzer to crash or timeout, execute the Encoders.Fuzz.Tools project from Visual Studio with the -p -s option (playback and stacktrace).

The playback tool tries to find all crashes and timeouts in the default folders and plays back on all libfuzzer fuzz targets. Playing back on the afl-fuzz targets are skipped because they were duplicates.

## Recreate or improve Testcases with Visual Studio

To recreate or improve the Testcases, execute the Encoders.Fuzz.Tools project from Visual Studio with the -t option, then all Fuzz/Testcases.* folder content is recreated with latest code.
