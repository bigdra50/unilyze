namespace Unilyze.Tests.Dup;

public sealed class CloneDetectorTests
{
    static FileTokenSequence MakeSequence(string path, int tokenCount, string tokenText = "tok")
    {
        var tokens = Enumerable.Range(1, tokenCount)
            .Select(i => new NormalizedToken(tokenText, i, i))
            .ToList();
        return new FileTokenSequence(path, tokens, tokenCount);
    }

    static FileTokenSequence MakeDistinctSequence(string path, IReadOnlyList<string> tokenTexts)
    {
        var tokens = tokenTexts
            .Select((text, index) => new NormalizedToken(text, index + 1, index + 1))
            .ToList();
        return new FileTokenSequence(path, tokens, tokens.Count);
    }

    [Fact]
    public void Detect_BelowMinTokens_NotReported()
    {
        var files = new[]
        {
            MakeSequence("/a.cs", 99),
            MakeSequence("/b.cs", 99),
        };

        var classes = CloneDetector.Detect(files, minTokens: 100);
        Assert.Empty(classes);
    }

    [Fact]
    public void Detect_ExactlyMinTokens_Reported()
    {
        var files = new[]
        {
            MakeSequence("/a.cs", 100),
            MakeSequence("/b.cs", 100),
        };

        var classes = CloneDetector.Detect(files, minTokens: 100);
        Assert.Single(classes);
        Assert.Equal(2, classes[0].Occurrences.Count);
        Assert.Equal(100, classes[0].TokenCount);
    }

    [Fact]
    public void Detect_HashCollision_RequiresSequenceVerification()
    {
        var sharedPrefix = Enumerable.Repeat("same", 50).ToList();
        var leftTail = Enumerable.Repeat("left", 50);
        var rightTail = Enumerable.Repeat("right", 50);
        var files = new[]
        {
            MakeDistinctSequence("/a.cs", sharedPrefix.Concat(leftTail).ToList()),
            MakeDistinctSequence("/b.cs", sharedPrefix.Concat(rightTail).ToList()),
        };

        var classes = CloneDetector.Detect(files, minTokens: 100);
        Assert.Empty(classes);
    }

    [Fact]
    public void Detect_SameFileNonOverlapping_Allowed()
    {
        var block = Enumerable.Repeat("block", 100).ToList();
        var spacer = Enumerable.Repeat("gap", 20);
        var tokens = block.Concat(spacer).Concat(block)
            .Select((text, index) => new NormalizedToken(text, index + 1, index + 1))
            .ToList();
        var files = new FileTokenSequence[] { new("/a.cs", tokens, tokens.Count) };

        var classes = CloneDetector.Detect(files, minTokens: 100);
        Assert.Single(classes);
        Assert.Equal(2, classes[0].Occurrences.Count);
        Assert.All(classes[0].Occurrences, o => Assert.Equal("/a.cs", o.File));
    }
}
