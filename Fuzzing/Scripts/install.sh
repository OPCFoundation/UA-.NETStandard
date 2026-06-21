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
