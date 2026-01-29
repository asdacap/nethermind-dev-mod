set -e o

dotnet build -c Release

cp bin/Release/net10.0/NethermindDevPlugin.* out
