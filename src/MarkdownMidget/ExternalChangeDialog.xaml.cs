using System.Windows;

namespace MarkdownMidget;

public enum ExternalChangeChoice { Cancel, Reload, SaveAs, Keep }

/// <summary>
/// Prompts the user when an open document has been modified outside the editor.
/// The host has already made a timestamped backup before showing this dialog.
/// </summary>
public partial class ExternalChangeDialog : Window
{
    public ExternalChangeChoice Choice { get; private set; } = ExternalChangeChoice.Cancel;

    public ExternalChangeDialog(string fileName, string backupPath)
    {
        InitializeComponent();
        HeaderText.Text = $"‘{fileName}’ was modified outside MMEEEE.";
        BackupPathText.Text = backupPath;
    }

    private void Reload_Click(object sender, RoutedEventArgs e) { Choice = ExternalChangeChoice.Reload; DialogResult = true; }
    private void SaveAs_Click(object sender, RoutedEventArgs e) { Choice = ExternalChangeChoice.SaveAs; DialogResult = true; }
    private void Keep_Click(object sender, RoutedEventArgs e) { Choice = ExternalChangeChoice.Keep; DialogResult = true; }
}
