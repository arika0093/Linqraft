# Recursively deletes /bin, /obj, /.generated directories, then runs dotnet clean.
find . -type d \( -name bin -o -name obj -o -name ".generated" \) -exec rm -rf {} +
dotnet clean
