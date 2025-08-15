using System.Text.Json;
using Xunit;
using CdIndex.Core;
using CdIndex.Emit;

namespace CdIndex.Core.Tests;

public class MetaSectionsTests
{
    [Fact]
    public void MetaSections_ListsPresentSectionsOnly()
    {
        var meta = new Meta("1.0.0", "1.2", DateTime.UtcNow, null, null);
        var index = new ProjectIndex(
            meta,
            new[] { new ProjectSection("P", "src/P/P.csproj") },
            new[] { new TreeSection(Array.Empty<FileEntry>()) }, // Tree present (even if empty list internally)
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        var json = JsonEmitter.EmitString(index, pretty: false);
        using var doc = JsonDocument.Parse(json);
        var metaEl = doc.RootElement.GetProperty("Meta");
        var sections = metaEl.GetProperty("Sections").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Tree", sections);
        Assert.DoesNotContain("DI", sections);
        Assert.DoesNotContain("Entrypoints", sections);
    }

    [Fact]
    public void MetaSections_OmittedWhenNoOptionalSections()
    {
        var meta = new Meta("1.0.0", "1.2", DateTime.UtcNow, null, null);
        var index = new ProjectIndex(
            meta,
            Array.Empty<ProjectSection>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        var json = JsonEmitter.EmitString(index, pretty: false);
        using var doc = JsonDocument.Parse(json);
        var metaEl = doc.RootElement.GetProperty("Meta");
        if (metaEl.TryGetProperty("Sections", out var sectionsProp))
        {
            Assert.Equal(0, sectionsProp.GetArrayLength());
        }
    }
}
