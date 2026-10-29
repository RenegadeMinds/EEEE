using System;
using System.IO;

namespace MarkdownMidget;

/// <summary>
/// Who this app is. MMEEEE ("Markdown Midget: Epics &amp; Epochs Encyclopedia Editor")
/// is a fork of Markdown Midget by Funcular Labs; it uses its own name, data
/// folder, Windows registrations and update feed so it never collides with an
/// installed Markdown Midget.
/// </summary>
public static class AppIdentity
{
    /// <summary>The name shown in titles, dialogs and Windows "Open with".</summary>
    public const string Name = "MMEEEE";

    /// <summary>The folder under %LOCALAPPDATA% for settings, recent files, backups, themes ...</summary>
    public const string DataFolderName = "EEEE";

    /// <summary>The GitHub repository releases and updates come from.</summary>
    public const string Repository = "RenegadeMinds/EEEE";
    public const string RepositoryUrl = "https://github.com/" + Repository;

    /// <summary>%LOCALAPPDATA%\EEEE</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
}
