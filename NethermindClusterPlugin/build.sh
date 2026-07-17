set -e o

# The in-tree nethermind submodule pulls transitive packages flagged by NuGet audit
# (NU1903/NU1904) and sets TreatWarningsAsErrors, which fails the build on restore.
# Demote those audit codes back to warnings so the plugin still builds.
dotnet build -c Release -p:WarningsNotAsErrors=NU1903%3BNU1904

cp bin/Release/net10.0/NethermindClusterPlugin.* out
