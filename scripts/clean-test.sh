# for git bisect and code-agent tests
sh scripts/cleanup.sh
dotnet build --no-incremental
# run test only .net 10 because short on time
dotnet test -f net10.0 
