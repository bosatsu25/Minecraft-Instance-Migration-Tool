using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace MinecraftInstanceMigration.Tests;

internal static class ArchitectureAssertions
{
    private const string Prefix = "MinecraftInstanceMigration.";

    public static async Task ProjectRespectsBoundary(
        string layer, string configuration, params string[] allowedLayers)
    {
        var root = FindRepositoryRoot();
        var name = Prefix + layer;
        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "msbuild",
            Path.Combine("src", name, name + ".csproj"),
            "-nologo",
            "-property:Configuration=" + configuration,
            "-getItem:ProjectReference,FrameworkReference",
            "-getProperty:TargetFramework,UseWPF,UseWindowsForms",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("MSBuild architecture evaluation exceeded 60 seconds.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, "MSBuild architecture evaluation failed: " + error);
        using var document = JsonDocument.Parse(output);
        var properties = document.RootElement.GetProperty("Properties");
        Assert.Equal("net10.0", properties.GetProperty("TargetFramework").GetString());
        Assert.False(string.Equals("true", properties.GetProperty("UseWPF").GetString(),
            StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals("true", properties.GetProperty("UseWindowsForms").GetString(),
            StringComparison.OrdinalIgnoreCase));

        var items = document.RootElement.GetProperty("Items");
        foreach (var reference in items.GetProperty("ProjectReference").EnumerateArray())
        {
            var referencedName = reference.GetProperty("Filename").GetString();
            Assert.Contains(referencedName, allowedLayers.Select(allowed => Prefix + allowed));
        }

        foreach (var reference in items.GetProperty("FrameworkReference").EnumerateArray())
        {
            Assert.Equal("Microsoft.NETCore.App", reference.GetProperty("Identity").GetString());
        }
    }

    public static void AssemblyRespectsBoundary(string layer, params string[] allowedLayers)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Prefix + layer + ".dll"));
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        Assert.Equal(Prefix + layer, metadata.GetString(metadata.GetAssemblyDefinition().Name));

        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                Assert.Contains(name, allowedLayers.Select(allowed => Prefix + allowed));
            }

            Assert.False(IsUiAssembly(name), "UI dependency found: " + name);
        }
    }

    public static void DomainDoesNotReferenceFilesystemImplementations()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Prefix + "Domain.dll"));
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        string[] forbiddenTypes =
        [
            "File", "Directory", "FileInfo", "DirectoryInfo", "FileSystemInfo",
            "FileStream", "FileSystemWatcher", "DriveInfo",
        ];

        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            if (metadata.GetString(type.Namespace) == "System.IO")
            {
                Assert.DoesNotContain(metadata.GetString(type.Name), forbiddenTypes);
            }
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (metadata.GetString(member.Name) != ".ctor"
                || member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(type.Namespace) != "System.IO"
                || metadata.GetString(type.Name) is not ("StreamReader" or "StreamWriter"))
            {
                continue;
            }

            // These BCL constructors are non-generic and return void. Their first
            // parameter distinguishes filesystem paths from caller-owned streams.
            var signature = metadata.GetBlobReader(member.Signature);
            signature.ReadSignatureHeader();
            var parameterCount = signature.ReadCompressedInteger();
            Assert.Equal(SignatureTypeCode.Void, signature.ReadSignatureTypeCode());
            if (parameterCount > 0)
            {
                Assert.NotEqual(SignatureTypeCode.String, signature.ReadSignatureTypeCode());
            }
        }
    }

    private static bool IsUiAssembly(string name) =>
        name.StartsWith("Presentation", StringComparison.Ordinal)
        || name.StartsWith("System.Windows", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.UI", StringComparison.Ordinal)
        || name is "WindowsBase" or "System.Xaml";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MinecraftInstanceMigration.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run architecture tests from a built repository checkout.");
    }
}
