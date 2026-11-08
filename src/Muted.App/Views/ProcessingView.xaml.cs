using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Muted.App.Views;

/// <summary>
/// The whole sound chain, every step once, in the order the sound goes through it. Each
/// card folds shut, and the cards you open or close stay that way while Muted runs.
/// </summary>
public partial class ProcessingView : System.Windows.Controls.UserControl
{
    private static readonly Dictionary<string, bool> Expanded = new();

    public ProcessingView()
    {
        InitializeComponent();
        foreach (var card in Cards())
        {
            if (Expanded.TryGetValue(card.Name, out var open))
            {
                card.IsExpanded = open;
            }

            card.Expanded += (_, _) => Expanded[card.Name] = true;
            card.Collapsed += (_, _) => Expanded[card.Name] = false;
        }
    }

    private IEnumerable<Expander> Cards() =>
        [NoiseCard, EchoCard, GateCard, LevelsCard, ShoutCard, WarmCard, BufferCard];

    /// <summary>How much noise is removed lives under Sound; this opens it there.</summary>
    private void OpenSound_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        DependencyObject? current = this;
        while (current is not null and not AdvancedView)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        (current as AdvancedView)?.ShowSection("SoundNav");
    }
}
