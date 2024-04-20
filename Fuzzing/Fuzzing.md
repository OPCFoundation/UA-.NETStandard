# Fuzz testing for UA.NET Standard

This project provides integration of Sharpfuzz with the encoders used by the UA .NET Standard library. 

## Installation

The fuzzing project executes on a linux subsystem. The following steps are required to set up the environment:

- Install a Windows Subsystem for Linux (WSL) by following the instructions at https://docs.microsoft.com/en-us/windows/wsl/install or by installing e.g. the Ubuntu app from the Microsoft store.
- The full instructions for setting up sharpfuzz can be found at https://github.com/Metalnem/sharpfuzz/blob/master/README.md.

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
See https://learn.microsoft.com/en-us/powershell/scripting/install/install-ubuntu?view=powershell-7.4

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

## Usage

To run the fuzzing project, execute the following commands, e.g. for the BinaryDecoder fuzzer:

```bash
#/bin/sh

cd BinaryDecoder
./fuzz.sh
```

Now the fuzzer is started and will run until it is stopped manually by hitting Ctrl-C. The fuzzer will create a directory `findings` in the fuzzer directory, which contains the test cases that caused the fuzzer to crash. 