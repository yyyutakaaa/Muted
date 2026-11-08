using System.Windows;
using System.Windows.Automation;
using UserControl = System.Windows.Controls.UserControl;

namespace Muted.App.Controls;

/// <summary>
/// A small "i" next to a setting. Hover it (or tab to it) and a card explains, in plain
/// words, what the setting does, when it works best and what to expect from it.
/// </summary>
public partial class InfoTip : UserControl
{
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title));
    public static readonly DependencyProperty WhatProperty = Register(nameof(What));
    public static readonly DependencyProperty BestProperty = Register(nameof(Best));
    public static readonly DependencyProperty ExpectProperty = Register(nameof(Expect));

    public InfoTip()
    {
        InitializeComponent();

        // The card is a separate visual tree, so it reads the answers straight from here.
        Card.DataContext = this;
    }

    /// <summary>The setting this card is about.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What the setting does.</summary>
    public string What
    {
        get => (string)GetValue(WhatProperty);
        set => SetValue(WhatProperty, value);
    }

    /// <summary>When it is best used.</summary>
    public string Best
    {
        get => (string)GetValue(BestProperty);
        set => SetValue(BestProperty, value);
    }

    /// <summary>What to expect from it.</summary>
    public string Expect
    {
        get => (string)GetValue(ExpectProperty);
        set => SetValue(ExpectProperty, value);
    }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(
            name,
            typeof(string),
            typeof(InfoTip),
            new PropertyMetadata(string.Empty, (target, _) => ((InfoTip)target).UpdateAutomation()));

    // A screen reader cannot hover, so it reads the same three answers instead.
    private void UpdateAutomation()
    {
        AutomationProperties.SetName(this, $"More about {Title}");
        AutomationProperties.SetHelpText(this, $"What it does: {What} Best when: {Best} What to expect: {Expect}");
    }
}
