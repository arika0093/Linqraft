# build only linqraft source files for instant feedback
dotnet clean
dotnet build ./src/Linqraft/ --no-incremental
