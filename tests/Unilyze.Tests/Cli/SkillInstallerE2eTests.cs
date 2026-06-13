using System.Reflection;
using System.Text;

namespace Unilyze.Tests.Cli;

[Collection(WorkingDirectoryCollection.Name)]
public sealed class SkillInstallerE2eTests : IDisposable
{
    readonly WorkingDirectoryGate _cwdGate;
    readonly string _tempDir;
    readonly IDisposable _cwdScope;

    public SkillInstallerE2eTests(WorkingDirectoryGate cwdGate)
    {
        _cwdGate = cwdGate;
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cwdScope = _cwdGate.Enter(_tempDir);
    }

    public void Dispose()
    {
        _cwdScope.Dispose();
        if (!Directory.Exists(_tempDir))
            return;

        // Windows briefly holds the directory lock after the cwd scope restores;
        // retry instead of failing the test over temp-dir cleanup.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch (IOException)
            {
                return; // temp dir; the OS cleans it up eventually
            }
        }
    }

    [Fact]
    public void Install_ClaudeTarget_WritesSkillsLayoutAndIsIdempotent()
    {
        var expectedSkills = LoadEmbeddedSkillNames();

        using var stderrFirst = new StringWriter();
        var originalError = Console.Error;
        try
        {
            Console.SetError(stderrFirst);
            var firstExit = SkillInstaller.Run(["skills", "install", "--claude"]);
            Assert.Equal(0, firstExit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var skillsRoot = Path.Combine(_tempDir, ".claude", "skills");
        Assert.True(Directory.Exists(skillsRoot), $"Expected skills root: {skillsRoot}");

        var firstSnapshot = CaptureSkillFiles(skillsRoot, expectedSkills);
        Assert.Equal(expectedSkills.Count, firstSnapshot.Count);

        foreach (var (skillName, content) in firstSnapshot)
        {
            Assert.False(string.IsNullOrWhiteSpace(content), $"SKILL.md for {skillName} was empty");
            Assert.EndsWith("SKILL.md", skillName, StringComparison.OrdinalIgnoreCase);
        }

        using var stderrSecond = new StringWriter();
        try
        {
            Console.SetError(stderrSecond);
            var secondExit = SkillInstaller.Run(["skills", "install", "--claude"]);
            Assert.Equal(0, secondExit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var secondSnapshot = CaptureSkillFiles(skillsRoot, expectedSkills);
        Assert.Equal(firstSnapshot, secondSnapshot);

        var secondReport = stderrSecond.ToString();
        Assert.Contains("Skipped:", secondReport);
        Assert.DoesNotContain("Installed: " + expectedSkills.Count, secondReport);
    }

    static IReadOnlyList<string> LoadEmbeddedSkillNames()
    {
        const string prefix = "Skills/";
        return Assembly.GetAssembly(typeof(SkillInstaller))!
            .GetManifestResourceNames()
            // Windows-built assemblies carry backslashes from %(RecursiveDir); normalize like SkillInstaller does.
            .Select(name => name.Replace('\\', '/'))
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith("/SKILL.md", StringComparison.Ordinal))
            .Select(name =>
            {
                var relative = name[prefix.Length..];
                var slash = relative.IndexOf('/');
                return relative[..slash];
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    static Dictionary<string, string> CaptureSkillFiles(string skillsRoot, IReadOnlyList<string> skillNames)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skillName in skillNames)
        {
            var skillPath = Path.Combine(skillsRoot, skillName, "SKILL.md");
            Assert.True(File.Exists(skillPath), $"Missing skill file: {skillPath}");
            snapshot[skillPath] = File.ReadAllText(skillPath, Encoding.UTF8);
        }
        return snapshot;
    }
}
