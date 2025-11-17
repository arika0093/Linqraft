# for git bisect and code-agent tests
rm -rf ./tests/Linqraft.Tests/.generated/
dotnet clean
dotnet build --no-incremental
# run test only .net 10 because short on time
dotnet test -f net10.0 
