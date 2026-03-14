using System.Reflection;

namespace Unilyze;

sealed record TargetConfig(string Id, string DisplayName, string ProjectDir, string SkillFileName = "SKILL.md");

static class SkillInstaller
{
    static readonly TargetConfig[] Targets =
    [
        new("claude", "Claude Code", ".claude"),
        new("codex", "Codex CLI", ".codex"),
        new("cursor", "Cursor", ".cursor"),
        new("gemini", "Gemini CLI", ".gemini"),
        new("windsurf", "Windsurf", ".windsurf"),
    ];

    const string ResourcePrefix = "Skills/";
    const string SkillFileName = "SKILL.md";

    public static int Run(string[] args)
    {
        if (args.Length < 2)
            return PrintUsage();

        var subcommand = args[1];
        var targetIds = ParseTargetFlags(args);
        var global = args.Any(a => a is "-g" or "--global");

        return subcommand switch
        {
            "install" => Install(targetIds, global),
            "uninstall" => Uninstall(targetIds, global),
            "list" => List(targetIds, global),
            _ => PrintUsage(),
        };
    }

    static int Install(List<string> targetIds, bool global)
    {
        var targets = ResolveTargets(targetIds);
        if (targets.Count == 0)
        {
            PrintTargetGuidance("install");
            return 1;
        }

        var skills = LoadEmbeddedSkills();
        Console.Error.WriteLine($"Installing unilyze skills ({(global ? "global" : "project")})...\n");

        foreach (var target in targets)
        {
            var baseDir = GetSkillsDir(target, global);
            int installed = 0, updated = 0, skipped = 0;

            foreach (var (name, files) in skills)
            {
                var skillDir = Path.Combine(baseDir, name);
                var skillPath = Path.Combine(skillDir, target.SkillFileName);

                if (!File.Exists(skillPath))
                {
                    WriteSkillFiles(skillDir, target.SkillFileName, files);
                    installed++;
                }
                else if (IsOutdated(skillDir, target.SkillFileName, files))
                {
                    WriteSkillFiles(skillDir, target.SkillFileName, files);
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }

            Console.Error.WriteLine($"{target.DisplayName}:");
            Console.Error.WriteLine($"  Installed: {installed}");
            Console.Error.WriteLine($"  Updated:   {updated}");
            Console.Error.WriteLine($"  Skipped:   {skipped}");
            Console.Error.WriteLine($"  Location:  {baseDir}\n");
        }

        return 0;
    }

    static int Uninstall(List<string> targetIds, bool global)
    {
        var targets = ResolveTargets(targetIds);
        if (targets.Count == 0)
        {
            PrintTargetGuidance("uninstall");
            return 1;
        }

        var skills = LoadEmbeddedSkills();
        Console.Error.WriteLine($"Uninstalling unilyze skills ({(global ? "global" : "project")})...\n");

        foreach (var target in targets)
        {
            var baseDir = GetSkillsDir(target, global);
            int removed = 0, notFound = 0;

            foreach (var (name, _) in skills)
            {
                var skillDir = Path.Combine(baseDir, name);
                if (Directory.Exists(skillDir))
                {
                    Directory.Delete(skillDir, recursive: true);
                    removed++;
                }
                else
                {
                    notFound++;
                }
            }

            Console.Error.WriteLine($"{target.DisplayName}:");
            Console.Error.WriteLine($"  Removed:   {removed}");
            Console.Error.WriteLine($"  Not found: {notFound}");
            Console.Error.WriteLine($"  Location:  {baseDir}\n");
        }

        return 0;
    }

    static int List(List<string> targetIds, bool global)
    {
        var targets = ResolveTargets(targetIds);
        if (targets.Count == 0)
            targets = Targets.ToList();

        var skills = LoadEmbeddedSkills();
        Console.Error.WriteLine("unilyze Skills Status:\n");

        foreach (var target in targets)
        {
            var baseDir = GetSkillsDir(target, global);
            Console.Error.WriteLine($"{target.DisplayName} ({(global ? "Global" : "Project")}):");
            Console.Error.WriteLine($"Location: {baseDir}");
            Console.Error.WriteLine(new string('=', 50));

            foreach (var (name, files) in skills)
            {
                var skillDir = Path.Combine(baseDir, name);
                var skillPath = Path.Combine(skillDir, target.SkillFileName);

                string icon, status;
                if (!File.Exists(skillPath))
                {
                    icon = "\u001b[31m✗\u001b[0m";
                    status = "not installed";
                }
                else if (IsOutdated(skillDir, target.SkillFileName, files))
                {
                    icon = "\u001b[33m↑\u001b[0m";
                    status = "outdated";
                }
                else
                {
                    icon = "\u001b[32m✓\u001b[0m";
                    status = "installed";
                }

                Console.Error.WriteLine($"  {icon} {name} ({status})");
            }

            Console.Error.WriteLine();
        }

        Console.Error.WriteLine($"Total: {skills.Count} skills");
        return 0;
    }

    static Dictionary<string, Dictionary<string, byte[]>> LoadEmbeddedSkills()
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Resource names: "Skills/<skillName>/SKILL.md", "Skills/<skillName>/references/foo.md"
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal));

        var skills = new Dictionary<string, Dictionary<string, byte[]>>();

        foreach (var resourceName in resourceNames)
        {
            // Strip "Skills/" prefix -> "<skillName>/SKILL.md" or "<skillName>/references/foo.md"
            var relative = resourceName[ResourcePrefix.Length..];
            var slashIndex = relative.IndexOf('/');
            if (slashIndex < 0) continue;

            var skillName = relative[..slashIndex];
            var filePath = relative[(slashIndex + 1)..];

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            if (!skills.ContainsKey(skillName))
                skills[skillName] = new Dictionary<string, byte[]>();

            skills[skillName][filePath] = ms.ToArray();
        }

        return skills;
    }

    static void WriteSkillFiles(string skillDir, string skillFileName, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(skillDir);
        foreach (var (filePath, content) in files)
        {
            var targetPath = filePath == SkillFileName ? skillFileName : filePath;
            var fullPath = Path.Combine(skillDir, targetPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(fullPath, content);
        }
    }

    static bool IsOutdated(string skillDir, string skillFileName, Dictionary<string, byte[]> files)
    {
        foreach (var (filePath, expected) in files)
        {
            var targetPath = filePath == SkillFileName ? skillFileName : filePath;
            var fullPath = Path.Combine(skillDir, targetPath);
            if (!File.Exists(fullPath)) return true;

            var installed = File.ReadAllBytes(fullPath);
            if (!installed.AsSpan().SequenceEqual(expected)) return true;
        }
        return false;
    }

    static string GetSkillsDir(TargetConfig target, bool global)
    {
        var root = global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Directory.GetCurrentDirectory();
        return Path.Combine(root, target.ProjectDir, "skills");
    }

    static List<TargetConfig> ResolveTargets(List<string> ids)
    {
        return Targets.Where(t => ids.Contains(t.Id)).ToList();
    }

    static List<string> ParseTargetFlags(string[] args)
    {
        var ids = new List<string>();
        foreach (var arg in args)
        {
            var id = arg switch
            {
                "--claude" => "claude",
                "--codex" => "codex",
                "--cursor" => "cursor",
                "--gemini" => "gemini",
                "--windsurf" => "windsurf",
                _ => null,
            };
            if (id is not null)
                ids.Add(id);
        }
        return ids;
    }

    static int PrintUsage()
    {
        Console.Error.WriteLine("""
unilyze skills - Manage unilyze skills for AI coding tools

Subcommands:
  install     Install skills to target tool
  uninstall   Uninstall skills from target tool
  list        List skill installation status

Targets:
  --claude    Claude Code (.claude/skills/)
  --codex     Codex CLI (.codex/skills/)
  --cursor    Cursor (.cursor/skills/)
  --gemini    Gemini CLI (.gemini/skills/)
  --windsurf  Windsurf (.windsurf/skills/)

Options:
  -g, --global  Use global (~/) location instead of project

Examples:
  unilyze skills install --claude
  unilyze skills install --claude --codex
  unilyze skills install --claude --global
  unilyze skills uninstall --claude
  unilyze skills list
""");
        return 0;
    }

    static void PrintTargetGuidance(string command)
    {
        Console.Error.WriteLine($"\nPlease specify at least one target for '{command}':\n");
        Console.Error.WriteLine("  --claude   Claude Code (.claude/skills/)");
        Console.Error.WriteLine("  --codex    Codex CLI (.codex/skills/)");
        Console.Error.WriteLine("  --cursor   Cursor (.cursor/skills/)");
        Console.Error.WriteLine("  --gemini   Gemini CLI (.gemini/skills/)");
        Console.Error.WriteLine("  --windsurf Windsurf (.windsurf/skills/)\n");
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine($"  unilyze skills {command} --claude");
        Console.Error.WriteLine($"  unilyze skills {command} --claude --codex");
    }
}
