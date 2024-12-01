cd .\Tracker\
dotnet build -c Debug -r linux-x64
cd ..\Node\
dotnet build -c Debug -r linux-x64
cd ..\Streamer\
dotnet build -c Debug -r linux-x64
cd ..\Client.Desktop\
dotnet build -c Debug -r linux-x64