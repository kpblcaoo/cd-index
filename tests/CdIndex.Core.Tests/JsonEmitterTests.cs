using System.Text.Json;
using Xunit;
using CdIndex.Core;
using CdIndex.Core.Tree;
using CdIndex.Emit;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Linq;

namespace CdIndex.Core.Tests;

public class JsonEmitterTests
{
    [Fact]
    public void JsonEmitter_EmitString_GeneratesValidJson()
    {
        // Arrange
        var index = CreateSampleProjectIndex();

        // Act
        var json = JsonEmitter.EmitString(index);

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        
        // Should be valid JSON
        var parsed = JsonDocument.Parse(json);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void JsonEmitter_Emit_IsIdempotent()
    {
        // Arrange
        var index = CreateSampleProjectIndex();

        // Act
        var json1 = JsonEmitter.EmitString(index);
        var json2 = JsonEmitter.EmitString(index);

        // Assert
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void JsonEmitter_Emit_OrdersCollectionsConsistently()
    {
        // Arrange - use fixed timestamp for deterministic testing
        var fixedTime = "2024-01-01T00:00:00.0000000Z";
        var index1 = CreateProjectIndexWithFiles(fixedTime, "file1.cs", "file2.cs");
        var index2 = CreateProjectIndexWithFiles(fixedTime, "file2.cs", "file1.cs");

        // Act
        var json1 = JsonEmitter.EmitString(index1);
        var json2 = JsonEmitter.EmitString(index2);

        // Assert
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void JsonEmitter_Emit_GeneratesCompactJson()
    {
        // Arrange
        var index = CreateSampleProjectIndex();

        // Act
        var json = JsonEmitter.EmitString(index);

        // Assert
        // Should not contain pretty-print formatting
        Assert.DoesNotContain("  ", json); // No spaces for indentation
        Assert.DoesNotContain("\n", json); // No newlines
        Assert.DoesNotContain("\r", json); // No carriage returns
    }

    [Fact]
    public void JsonEmitter_Emit_ValidatesAgainstSchema()
    {
        // Arrange
        var schemaJson = File.ReadAllText("/home/runner/work/cd-index/cd-index/schema/project_index.schema.json");
        var schema = JSchema.Parse(schemaJson);
        var index = CreateSampleProjectIndex();

        // Act
        var json = JsonEmitter.EmitString(index);
        var jsonObject = JObject.Parse(json);

        // Assert
        var isValid = jsonObject.IsValid(schema, out IList<string> errors);
        Assert.True(isValid, $"JSON validation failed: {string.Join(", ", errors)}");
    }

    [Fact]
    public void JsonEmitter_Emit_HandlesUtcDates()
    {
        // Arrange
        var utcDate = DateTime.UtcNow.ToString("O"); // ISO-8601 UTC format
        var meta = new MetaSection(utcDate, "1.0.0", "https://github.com/test/repo");
        var index = new ProjectIndex(
            meta,
            Array.Empty<ProjectSection>(),
            Array.Empty<TreeSection>(),
            Array.Empty<DISection>(),
            Array.Empty<EntrypointSection>(),
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );

        // Act
        var json = JsonEmitter.EmitString(index);

        // Assert
        Assert.Contains(utcDate, json);
    }

    private static ProjectIndex CreateSampleProjectIndex()
    {
        var meta = new MetaSection(
            DateTime.UtcNow.ToString("O"),
            "1.0.0",
            "https://github.com/test/repo"
        );

        var project = new ProjectSection(
            "TestProject",
            "src/TestProject/TestProject.csproj",
            "net9.0",
            "C#"
        );

        var files = new[]
        {
            new FileEntry("src/Program.cs", "a".PadRight(64, '0'), 10, "source"),
            new FileEntry("README.md", "b".PadRight(64, '0'), 5, "documentation")
        };

        var tree = new TreeSection(files);

        var di = new DISection(new[]
        {
            new DIEntry("IService", "Service", "Scoped")
        });

        var entrypoints = new EntrypointSection(new[]
        {
            new EntrypointEntry("Main", "src/Program.cs", "Console", "Main entry point")
        });

        return new ProjectIndex(
            meta,
            new[] { project },
            new[] { tree },
            new[] { di },
            new[] { entrypoints },
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );
    }

    private static ProjectIndex CreateProjectIndexWithFiles(string timestamp, params string[] filePaths)
    {
        var meta = new MetaSection(timestamp, "1.0.0");

        var files = filePaths.Select(path => 
            new FileEntry(path, "a".PadRight(64, '0'), 10, "source")).ToArray();

        var tree = new TreeSection(files);

        return new ProjectIndex(
            meta,
            Array.Empty<ProjectSection>(),
            new[] { tree },
            Array.Empty<DISection>(),
            Array.Empty<EntrypointSection>(),
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );
    }
}