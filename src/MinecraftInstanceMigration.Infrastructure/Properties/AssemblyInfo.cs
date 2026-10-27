using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("MinecraftInstanceMigration.Infrastructure.Tests")]

// ntdll and kernel32 are known DLLs; restricting the search keeps any future import from being
// resolved from the application or current directory of a user-extracted portable package.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
