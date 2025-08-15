using System.Text.Json;
using Xunit;
using CdIndex.Core;
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
    var json = JsonEmitter.EmitString(index, pretty: false);

        // Assert
        // Should not contain pretty-print formatting
        Assert.DoesNotContain("  ", json); // No spaces for indentation
        Assert.DoesNotContain("\n", json); // No newlines
        Assert.DoesNotContain("\r", json); // No carriage returns
    }

    [Fact]
    public void JsonEmitter_Emit_PrettyByDefault()
    {
        var index = CreateSampleProjectIndex();
        var json = JsonEmitter.EmitString(index); // default pretty
        Assert.Contains("\n", json);
        Assert.Contains("  ", json); // indentation spaces present
    }

    [Fact]
    public void JsonEmitter_Emit_ValidatesAgainstSchema()
    {
        // Arrange
        var schemaPath = FindSchemaUpwards("project_index.schema.json");
        Assert.True(File.Exists(schemaPath), $"Schema file not found: {schemaPath}");
        var schemaJson = File.ReadAllText(schemaPath);
        var schema = JSchema.Parse(schemaJson);
        var index = CreateSampleProjectIndex();

        // Act
        var json = JsonEmitter.EmitString(index);
        var jsonObject = JObject.Parse(json);

        // Assert
        var isValid = jsonObject.IsValid(schema, out IList<string> errors);
        Assert.True(isValid, $"JSON validation failed: {string.Join(", ", errors)}");
    }

    // Removed outdated UTC date test pending stabilized Meta serialization for DateTime.
    [Fact]
    public void JsonEmitter_Emit_MetaGeneratedAt_IsIso8601Utc()
    {
        // Arrange
        var meta = new Meta("1.0.0", "1.1", DateTime.UtcNow, null);
        var index = new ProjectIndex(
            meta,
            Array.Empty<ProjectSection>(),
            Array.Empty<TreeSection>(),
            Array.Empty<DISection>(),
            Array.Empty<EntrypointsSection>(),
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphsSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );

        // Act
        var json = JsonEmitter.EmitString(index);
        using var doc = JsonDocument.Parse(json);
        var dtString = doc.RootElement.GetProperty("Meta").GetProperty("GeneratedAt").GetString();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(dtString));
        Assert.EndsWith("Z", dtString); // ensure UTC designator
        Assert.True(DateTime.TryParse(dtString, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed));
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        // Should not be default
        Assert.True(parsed > DateTime.UnixEpoch);
    }

    [Fact]
    public void JsonEmitter_Emit_Orders_Entrypoints_And_HostedServices()
    {
        // Arrange: create deliberately unsorted entrypoints and hosted services
        var meta = new Meta("1.0.0", "1.1", DateTime.UtcNow, null);
        var hostedA = new HostedService("ZType", "b/File2.cs", 30);
        var hostedB = new HostedService("AType", "a/File1.cs", 10);
        var hostedC = new HostedService("AType", "a/File1.cs", 5); // same type/file different line

        var ep2 = new EntrypointsSection(new ProjectRef("ZProject", "z/Proj.csproj"), null, new[] { hostedA });
        var ep1 = new EntrypointsSection(new ProjectRef("AProject", "x/Proj.csproj"), new ProgramMain("Program.cs", 1, "Program"), new[] { hostedA, hostedB, hostedC });

        var index = new ProjectIndex(
            meta,
            Array.Empty<ProjectSection>(),
            Array.Empty<TreeSection>(),
            Array.Empty<DISection>(),
            new[] { ep2, ep1 }, // unsorted
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphsSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );

        // Act
        var json = JsonEmitter.EmitString(index);
        using var doc = JsonDocument.Parse(json);
        var entrypoints = doc.RootElement.GetProperty("Entrypoints");

        // Assert: first project should be AProject then ZProject
        Assert.Equal("AProject", entrypoints[0].GetProperty("Project").GetProperty("Name").GetString());
        Assert.Equal("ZProject", entrypoints[1].GetProperty("Project").GetProperty("Name").GetString());

        var hosted = entrypoints[0].GetProperty("HostedServices");
        // Should be ordered by Type asc, then File asc, then Line asc -> hostedB (AType line10), hostedC (AType line5) but line5 < line10 so order should be line5 then line10; need to craft expected list accordingly
        // Re-evaluate expected order: Comparer sorts by Type, then File, then Line. We have two AType same file lines 10 and 5 -> line5 first.
        Assert.Equal("AType", hosted[0].GetProperty("Type").GetString());
        Assert.Equal(5, hosted[0].GetProperty("Line").GetInt32());
        Assert.Equal(10, hosted[1].GetProperty("Line").GetInt32());
        Assert.Equal("ZType", hosted[2].GetProperty("Type").GetString());
    }

    [Fact]
    public void JsonEmitter_Emit_InvalidatesSchema_WhenRequiredFieldMissing()
    {
        // Arrange
        var schemaPath = FindSchemaUpwards("project_index.schema.json");
        var schemaJson = File.ReadAllText(schemaPath);
        var schema = JSchema.Parse(schemaJson);
        // Создаём минимальный JSON без обязательного поля Meta
        var jsonText = @"{
            ""Project"": [],
            ""Tree"": [],
            ""DI"": [],
            ""Entrypoints"": [],
            ""MessageFlow"": [],
            ""Callgraphs"": [],
            ""Configs"": [],
            ""Commands"": [],
            ""Tests"": []
        }";
        var jsonObject = JObject.Parse(jsonText);
        // Act
        IList<string> errors;
        var isValid = jsonObject.IsValid(schema, out errors);
        // Assert
        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("Meta"));
    }

    private static ProjectIndex CreateSampleProjectIndex()
    {
        var meta = new Meta(
            "1.0.0",
            "1.1",
            DateTime.UtcNow,
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
            new FileEntry("src/Program.cs", "cs", 10, new string('a', 64)),
            new FileEntry("README.md", "md", 5, new string('b', 64))
        };

        var tree = new TreeSection(files);

        var di = new DISection(new[]
        {
            new DiRegistration("IService", "Service", "Scoped", "src/Program.cs", 10)
        }, new[]
        {
            new HostedService("BackgroundService", "src/Program.cs", 20)
        });

        var entrypointsSection = new EntrypointsSection(
            new ProjectRef("TestProject", "src/TestProject/TestProject.csproj"),
            new ProgramMain("src/Program.cs", 1, "Program"),
            new[] { new HostedService("BackgroundService", "src/Program.cs", 20) }
        );

        return new ProjectIndex(
            meta,
            new[] { project },
            new[] { tree },
            new[] { di },
            new[] { entrypointsSection },
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphsSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );
    }

    private static ProjectIndex CreateProjectIndexWithFiles(string timestamp, params string[] filePaths)
    {
        var meta = new Meta("1.0.0", "1.1", DateTime.Parse(timestamp));

        var files = filePaths.Select(path =>
            new FileEntry(path, Path.GetExtension(path).TrimStart('.').ToLowerInvariant(), 10, new string('a', 64))).ToArray();

        var tree = new TreeSection(files);

        return new ProjectIndex(
            meta,
            Array.Empty<ProjectSection>(),
            new[] { tree },
            Array.Empty<DISection>(),
            Array.Empty<EntrypointsSection>(),
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphsSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );
    }

    private static string FindSchemaUpwards(string schemaFile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8; i++) // up to 8 levels up
        {
            var candidate = Path.Combine(dir.FullName, "schema", schemaFile);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
            if (dir == null) break;
        }
        throw new FileNotFoundException($"Schema file not found in any parent directory: {schemaFile}");
    }
}
