namespace Unilyze.Tests.Serve;

public sealed class ServeChangeDetectionTests : IDisposable
{
    readonly string _root;

    public ServeChangeDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"unilyze-serve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("Assets/Scripts/Foo.cs", true)]
    [InlineData("App.csproj", true)]
    [InlineData("App.sln", true)]
    [InlineData("Assets/Asm.asmdef", true)]
    [InlineData("Assets/Foo.cs.meta", true)]
    [InlineData("ProjectSettings/ProjectVersion.txt", true)]
    [InlineData("Library/ScriptAssemblies/Assembly-CSharp.dll", true)]
    [InlineData("Library/Bee/artifacts.dll", false)]
    [InlineData("obj/Debug/foo.cs", false)]
    [InlineData("bin/Debug/App.dll", false)]
    [InlineData(".git/HEAD", false)]
    [InlineData(".unilyze/cache/snap.json", false)]
    [InlineData(".unilyze/triage.json", true)]
    [InlineData(".unilyze/baseline.json", false)]
    [InlineData(".unilyze.json", true)]
    [InlineData("README.md", false)]
    [InlineData("Assets/Notes.txt", false)]
    public void IsRelevant_ClassifiesAnalysisInputs(string relativePath, bool expected)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(expected, ServeInputFilter.IsRelevant(full, _root));
    }

    [Fact]
    public void Fingerprint_StableWhenNothingChanges()
    {
        File.WriteAllText(Path.Combine(_root, "A.cs"), "class A {}");
        var first = ServeInputFingerprint.Compute(_root);
        var second = ServeInputFingerprint.Compute(_root);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeStamps_KeysRelevantFilesByRelativePath()
    {
        File.WriteAllText(Path.Combine(_root, "A.cs"), "class A {}");
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(Path.Combine(_root, "obj", "G.cs"), "class G {}");

        var stamps = ServeInputFingerprint.ComputeStamps(_root);

        Assert.Contains("A.cs", stamps.Keys);
        Assert.DoesNotContain("obj/G.cs", stamps.Keys); // excluded directory
    }

    [Fact]
    public void ComputeStamps_StampChangesWhenFileEdited()
    {
        var file = Path.Combine(_root, "A.cs");
        File.WriteAllText(file, "class A {}");
        var before = ServeInputFingerprint.ComputeStamps(_root)["A.cs"];

        File.WriteAllText(file, "class A { public int X; }");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));

        Assert.NotEqual(before, ServeInputFingerprint.ComputeStamps(_root)["A.cs"]);
    }

    [Fact]
    public void ChangedFiles_FirstBuild_ReportsNothing()
    {
        var current = ServeInputFingerprint.ComputeStamps(_root);
        var display = new Dictionary<string, string> { ["f0"] = "A.cs" };

        Assert.Empty(ServeChangedFiles.Detect(null, current, display));
    }

    [Fact]
    public void ChangedFiles_MapsEditedSourceToFileId()
    {
        var previous = new Dictionary<string, string>
        {
            ["Assets/Scripts/Foo.cs"] = "100|1",
            ["Assets/Scripts/Bar.cs"] = "200|1",
        };
        var current = new Dictionary<string, string>
        {
            ["Assets/Scripts/Foo.cs"] = "140|2", // edited
            ["Assets/Scripts/Bar.cs"] = "200|1", // unchanged
        };
        var display = new Dictionary<string, string>
        {
            ["f0"] = "Assets/Scripts/Foo.cs",
            ["f1"] = "Assets/Scripts/Bar.cs",
        };

        var changed = ServeChangedFiles.Detect(previous, current, display);

        Assert.Equal(["f0"], changed);
    }

    [Fact]
    public void ChangedFiles_IgnoresChangedInputsWithoutAFileId()
    {
        // A .meta / .csproj edit changes inputs but maps to no analyzed block.
        var previous = new Dictionary<string, string> { ["App.csproj"] = "1|1" };
        var current = new Dictionary<string, string> { ["App.csproj"] = "2|2" };
        var display = new Dictionary<string, string> { ["f0"] = "Assets/Scripts/Foo.cs" };

        Assert.Empty(ServeChangedFiles.Detect(previous, current, display));
    }

    [Fact]
    public void Fingerprint_ChangesWhenSourceEdited()
    {
        var file = Path.Combine(_root, "A.cs");
        File.WriteAllText(file, "class A {}");
        var before = ServeInputFingerprint.Compute(_root);

        // Grow the file and bump its mtime so size/time-based fingerprinting diverges
        // even on coarse-grained filesystems.
        File.WriteAllText(file, "class A { public int X; }");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));

        Assert.NotEqual(before, ServeInputFingerprint.Compute(_root));
    }

    [Fact]
    public void Fingerprint_ChangesWhenCsprojAddedOrRemoved()
    {
        File.WriteAllText(Path.Combine(_root, "A.cs"), "class A {}");
        var before = ServeInputFingerprint.Compute(_root);

        var csproj = Path.Combine(_root, "App.csproj");
        File.WriteAllText(csproj, "<Project/>");
        var withCsproj = ServeInputFingerprint.Compute(_root);
        Assert.NotEqual(before, withCsproj);

        File.Delete(csproj);
        Assert.Equal(before, ServeInputFingerprint.Compute(_root));
    }

    [Fact]
    public void Fingerprint_IgnoresExcludedDirectories()
    {
        File.WriteAllText(Path.Combine(_root, "A.cs"), "class A {}");
        var before = ServeInputFingerprint.Compute(_root);

        var objDir = Path.Combine(_root, "obj", "Debug");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "Generated.cs"), "class G {}");

        Assert.Equal(before, ServeInputFingerprint.Compute(_root));
    }

    [Fact]
    public void Fingerprint_ChangesWhenExplicitExternalInputChanges()
    {
        var external = Path.Combine(Path.GetTempPath(), $"unilyze-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(external, "{}");
            var before = ServeInputFingerprint.Compute(_root, [external]);

            File.WriteAllText(external, "{\"profile\":\"strict\"}");
            File.SetLastWriteTimeUtc(external, DateTime.UtcNow.AddSeconds(5));

            Assert.NotEqual(before, ServeInputFingerprint.Compute(_root, [external]));
        }
        finally
        {
            File.Delete(external);
        }
    }

    [Fact]
    public void Watcher_SourceEdit_IsConsumedOnceAcrossReconcile()
    {
        var source = Path.Combine(_root, "A.cs");
        File.WriteAllText(source, "class A {}");
        var count = 0;
        using var changed = new ManualResetEventSlim();
        using var watcher = new ServeChangeWatcher(
            _root,
            () =>
            {
                Interlocked.Increment(ref count);
                changed.Set();
                return count;
            },
            reconcileInterval: TimeSpan.FromMilliseconds(100));

        watcher.Start();
        // A single filesystem mutation is one fingerprint transition. Editing content and
        // bumping mtime as two operations can be observed as two transitions under CI load and
        // fire twice (each firing correct for its own transition). Use one mtime bump, then
        // assert the single transition is consumed exactly once even after several reconcile
        // cycles elapse — a converged count is deterministic where a tight negative Wait is not.
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(5));

        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(TimeSpan.FromMilliseconds(600));
        Assert.Equal(1, Volatile.Read(ref count));
    }
}
