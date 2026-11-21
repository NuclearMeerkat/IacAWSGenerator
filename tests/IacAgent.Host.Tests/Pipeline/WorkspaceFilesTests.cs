using IacAgent.Core;
using IacAgent.Host.Pipeline;

namespace IacAgent.Host.Tests.Pipeline;

public sealed class WorkspaceFilesTests
{
    [Fact]
    public void Read_FileInsideWorkspace_ReturnsDiskContent()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "main.tf"), "formatted\n");
            var files = WorkspaceFiles.Read(directory, [new GeneratedFile("main.tf", "original")]);

            Assert.Equal("formatted\n", files[0].Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Read_PathOutsideWorkspace_KeepsOriginalContent()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var files = WorkspaceFiles.Read(directory, [new GeneratedFile("../secret.tf", "original")]);

            Assert.Equal("original", files[0].Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
