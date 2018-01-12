#!/bin/bash
echo Test the .Net Core console server and console client
workdir=$(pwd)
testresult=0

cd SampleApplications/Samples/NetCoreConsoleServer
echo build server
rm -r obj
dotnet build NetCoreConsoleServer.csproj
echo start server
dotnet run --no-restore --no-build --project NetCoreConsoleServer.csproj -t 60 -a &
serverpid="$!"
cd $workdir

cd SampleApplications/Samples/NetCoreConsoleClient
echo build client
rm -r obj
dotnet build NetCoreConsoleClient.csproj
echo start client
dotnet run --no-restore --no-build --project NetCoreConsoleClient.csproj -t 20 &
clientpid="$!"
cd $workdir

echo wait for client
wait $clientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresult=$?
	echo "FAILED - Client test failed with a status of $testresult"
fi

echo wait for server
wait $serverpid
serverresult=$?

if [ $? -eq 0 ]; then
	echo "SUCCESS - Server test passed"
else
	serverresult=$?
	echo "FAILED - Server test failed with a status of $serverresult"
fi

exit $testresult



