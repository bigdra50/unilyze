namespace Unilyze.History;

internal static class HotspotCommitParser
{
    const char RecordDelimiter = '\x01';
    const char FieldDelimiter = '\x1f';

    internal static IReadOnlyList<GitCommitRecord> ParseCommitLog(string gitLogOutput)
    {
        if (string.IsNullOrWhiteSpace(gitLogOutput))
            return [];

        var commits = new List<GitCommitRecord>();
        string? currentHash = null;
        string? currentAuthor = null;
        string? currentEmail = null;
        long currentTimestamp = 0;
        var currentFiles = new List<string>();

        void FlushCommit()
        {
            if (currentHash is null)
                return;
            commits.Add(new GitCommitRecord(
                currentHash,
                currentAuthor ?? "",
                currentEmail ?? "",
                currentTimestamp,
                currentFiles.ToList()));
            currentHash = null;
            currentAuthor = null;
            currentEmail = null;
            currentFiles.Clear();
        }

        foreach (var rawLine in SplitLines(gitLogOutput))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == RecordDelimiter)
            {
                FlushCommit();
                var payload = line[1..];
                var fields = payload.Split(FieldDelimiter);
                if (fields.Length < 4)
                    continue;
                currentHash = fields[0];
                currentAuthor = fields[1];
                currentEmail = fields[2];
                _ = long.TryParse(fields[3], out currentTimestamp);
                continue;
            }

            if (currentHash is not null)
                currentFiles.Add(line);
        }

        FlushCommit();
        return commits;
    }

    internal static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n' && text[i] != '\r')
                continue;

            if (i > start)
                yield return text[start..i];

            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;

            start = i + 1;
        }

        if (start < text.Length)
            yield return text[start..];
    }

    internal static IReadOnlyList<FileChangeFrequency> ParseLegacyNameOnlyLog(string gitLogOutput)
    {
        if (string.IsNullOrWhiteSpace(gitLogOutput))
            return [];

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(gitLogOutput))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            counts[trimmed] = counts.GetValueOrDefault(trimmed) + 1;
        }

        return counts
            .Select(kv => new FileChangeFrequency(kv.Key, kv.Value))
            .OrderByDescending(f => f.ChangeCount)
            .ToList();
    }
}
