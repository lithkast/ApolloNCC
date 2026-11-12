using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ApolloNCC.Services;

/// <summary>Sends chat on the player's behalf. Only ever called from a button the user pressed.</summary>
public static unsafe class GameChat
{
    private const int MaxBytes = 500;

    /// <summary>Sends a /tell. Must run on the framework thread.</summary>
    public static void SendTell(string name, string world, string message)
    {
        if (!IsSafeName(name) || !IsSafeName(world))
            throw new ArgumentException("That character name or world is not valid.");

        Execute($"/tell {name}@{world} {message}");
    }

    /// <summary>
    /// Sends one line as if typed into the chat box: a command such as "/y ..." or "/em ...". Must run on the
    /// framework thread. The text comes from the web app (macros kept by the owners and managers), never from patrons.
    /// </summary>
    public static void SendLine(string line) => Execute(line);

    private static bool IsSafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
            return false;
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetter(c) || c is ' ' or '\'' or '-'))
                return false;
        }

        return true;
    }

    private static void Execute(string line)
    {
        // One line only, so nothing in the text can turn into a second command.
        var clean = line.ReplaceLineEndings(" ").Trim();
        if (Encoding.UTF8.GetByteCount(clean) > MaxBytes)
            throw new ArgumentException("That message is too long.");

        var ui = UIModule.Instance();
        if (ui == null)
            throw new InvalidOperationException("The game is not ready to send chat.");

        var text = Utf8String.FromString(clean);
        try
        {
            ui->ProcessChatBoxEntry(text);
        }
        finally
        {
            text->Dtor(true);
        }
    }
}
