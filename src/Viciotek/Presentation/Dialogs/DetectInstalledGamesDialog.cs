using System.Windows;
using Viciotek.Application;
namespace Viciotek.Presentation.Dialogs;
public partial class DetectInstalledGamesDialog : ViciotekDialogWindow
{
    public sealed class Choice(DetectedGame game)
    {
        public DetectedGame Game { get; } = game;
        public string Label => $"{Game.Name} — {Game.Platform}";
        public bool IsSelected { get; set; } = game.HighConfidence;
    }
    public IReadOnlyList<Choice> Choices { get; }
    public IReadOnlyList<DetectedGame> Selected { get; private set; } = [];
    public DetectInstalledGamesDialog(IReadOnlyList<DetectedGame> games)
    {
        InitializeComponent(); Choices = games.Select(game => new Choice(game)).ToArray(); DataContext = this;
        if (games.Count == 0) Instruction.Text = L.Get("NoDetectedGames");
        UpdateAction();
    }
    private void UpdateAction() => ImportButton.IsEnabled = Choices.Any(choice => choice.IsSelected);
    private void Choice_Click(object sender, RoutedEventArgs e) => UpdateAction();
    private void Import_Click(object sender, RoutedEventArgs e)
    { Selected = Choices.Where(choice => choice.IsSelected).Select(choice => choice.Game).ToList(); if (Selected.Count > 0) DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
