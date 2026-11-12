using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ApolloNCC.Services;

/// <summary>
/// Plays a macro kept on the web app (a drink's preparation macro, the raffle announcement) the way the game plays
/// a user macro: one line at a time, honouring &lt;wait.N&gt; and /wait N. Started only by a button the user pressed.
/// </summary>
public sealed partial class MacroRunner
{
    // the game's own limit for a user macro
    private const int MaxLines = 15;

    // a breath between lines so the game never sees a burst of chat
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(120);

    private readonly Queue<string> lines = new();
    private DateTimeOffset next = DateTimeOffset.MinValue;

    public bool Running => this.lines.Count > 0;

    /// <summary>What went wrong with the last line, if anything.</summary>
    public string? Error { get; private set; }

    public void Play(IEnumerable<string> macro)
    {
        this.lines.Clear();
        this.Error = null;
        foreach (var line in macro)
        {
            if (this.lines.Count == MaxLines)
                break;
            if (!string.IsNullOrWhiteSpace(line))
                this.lines.Enqueue(line.Trim());
        }

        this.next = DateTimeOffset.MinValue;
    }

    public void Stop() => this.lines.Clear();

    /// <summary>Called every frame on the framework thread.</summary>
    public void Tick(DateTimeOffset now)
    {
        if (this.lines.Count == 0 || now < this.next)
            return;

        var line = this.lines.Dequeue();
        var wait = Gap;

        // "/wait 2" on its own line
        var alone = WaitLine().Match(line);
        if (alone.Success)
        {
            this.next = now + Seconds(alone.Groups[1].Value);
            return;
        }

        // "... <wait.2>" at the end of a line
        var tail = WaitTag().Match(line);
        if (tail.Success)
        {
            line = line[..tail.Index].TrimEnd();
            wait = Seconds(tail.Groups[1].Value);
        }

        try
        {
            if (line.Length > 0)
                GameChat.SendLine(line);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            this.Error = e.Message;
            this.lines.Clear();
        }

        this.next = now + wait;
    }

    private static TimeSpan Seconds(string text) =>
        double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var s) ? TimeSpan.FromSeconds(Math.Clamp(s, 0, 60)) : Gap;

    [GeneratedRegex(@"^/wait\s+(\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WaitLine();

    [GeneratedRegex(@"<wait\.(\d+(?:\.\d+)?)>\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WaitTag();
}
