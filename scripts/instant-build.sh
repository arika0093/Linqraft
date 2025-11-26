# build only linqraft source files for instant feedback
sh scripts/cleanup.sh
dotnet build ./src/Linqraft/ --no-incremental
