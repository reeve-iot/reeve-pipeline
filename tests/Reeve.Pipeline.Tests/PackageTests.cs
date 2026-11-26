using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace Reeve.Pipeline.Tests;

public sealed class PackageTests
{
    [Fact]
    public void The_shipped_package_takes_no_dependency()
    {
        var nupkg = FindNupkg();
        nupkg.Should().NotBeNull(
            "run `dotnet pack src/Reeve.Pipeline -c Release -o artifacts/nupkg` before `dotnet test`");

        using var archive = ZipFile.OpenRead(nupkg!);
        var nuspecEntry = archive.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);

        var dependencies = nuspec.Descendants()
            .Where(e => e.Name.LocalName == "dependency")
            .Select(e => e.Attribute("id")?.Value)
            .ToList();

        dependencies.Should().BeEmpty("the package is meant to take no dependencies");
    }

    private static string? FindNupkg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reeve.Pipeline.slnx")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? null
            : Directory.EnumerateFiles(dir.FullName, "Reeve.Pipeline.*.nupkg", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
    }
}
