using System.Windows;
using System.Windows.Controls;

namespace Muted.App.Controls;

/// <summary>
/// One setting, drawn the same way everywhere: a title, an optional line of
/// explanation, an optional "i" card, an optional live value, and the control itself.
/// A switch or button sits on the right; with <see cref="Stacked"/> the control (a
/// slider, a list) takes the full width below the text.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title));
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description));
    public static readonly DependencyProperty ValueTextProperty = Register(nameof(ValueText));
    public static readonly DependencyProperty TipWhatProperty = Register(nameof(TipWhat));
    public static readonly DependencyProperty TipBestProperty = Register(nameof(TipBest));
    public static readonly DependencyProperty TipExpectProperty = Register(nameof(TipExpect));

    public static readonly DependencyProperty StackedProperty = DependencyProperty.Register(
        nameof(Stacked),
        typeof(bool),
        typeof(SettingRow),
        new PropertyMetadata(false));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>A live read-out shown at the right of the title, such as "+3.0 dB".</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    /// <summary>What the setting does; setting it shows the "i" card.</summary>
    public string TipWhat
    {
        get => (string)GetValue(TipWhatProperty);
        set => SetValue(TipWhatProperty, value);
    }

    public string TipBest
    {
        get => (string)GetValue(TipBestProperty);
        set => SetValue(TipBestProperty, value);
    }

    public string TipExpect
    {
        get => (string)GetValue(TipExpectProperty);
        set => SetValue(TipExpectProperty, value);
    }

    /// <summary>True puts the control below the text, at full width.</summary>
    public bool Stacked
    {
        get => (bool)GetValue(StackedProperty);
        set => SetValue(StackedProperty, value);
    }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));
}
