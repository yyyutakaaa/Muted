using System.ComponentModel;
using System.Globalization;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>
/// Small read-outs and choice lists the main window layout needs. Nothing here adds
/// audio behaviour: every value is a different view on a setting or a metric that
/// already exists.
/// </summary>
internal sealed partial class MainViewModel
{
    private const string HighPassOff = "Off";
    private string _processingLoadText = "Load –";
    private string _bufferText = "Buffer –";

    /// <summary>The high-pass filter as one dropdown: off, or one of the usual cut-offs.</summary>
    public IReadOnlyList<string> HighPassChoices { get; } =
        [HighPassOff, "60 Hz", "80 Hz", "100 Hz", "120 Hz", "150 Hz"];

    public string HighPassChoice
    {
        get
        {
            if (!HighPassEnabled)
            {
                return HighPassOff;
            }

            // A cut-off set elsewhere with the fine slider shows as the nearest choice.
            return HighPassChoices
                .Skip(1)
                .OrderBy(choice => Math.Abs(ParseHertz(choice) - HighPassFrequency))
                .First();
        }
        set
        {
            if (string.IsNullOrEmpty(value) || value == HighPassChoice)
            {
                return;
            }

            if (value == HighPassOff)
            {
                HighPassEnabled = false;
                return;
            }

            HighPassFrequency = ParseHertz(value);
            HighPassEnabled = true;
        }
    }

    /// <summary>Processing load of the realtime thread, the closest thing Muted measures to CPU use.</summary>
    public string ProcessingLoadText
    {
        get => _processingLoadText;
        private set => SetProperty(ref _processingLoadText, value);
    }

    public string BufferText
    {
        get => _bufferText;
        private set => SetProperty(ref _bufferText, value);
    }

    public string MuteHotkeyText => HotkeyTextFor(HotkeyAction.ToggleMute);

    public string PushToTalkHotkeyText => HotkeyTextFor(HotkeyAction.PushToTalk) is var text && text == "Not set"
        ? "No key set yet"
        : "Hold " + HotkeyTextFor(HotkeyAction.PushToTalk);

    private void InitializeLayout()
    {
        PropertyChanged += OnLayoutSourceChanged;
        PropertyChanged += RedesignSourceChanged;
    }

    private void OnLayoutSourceChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(HighPassEnabled):
            case nameof(HighPassFrequency):
                OnPropertyChanged(nameof(HighPassChoice));
                break;
            case nameof(IsPushToTalkArmed):
                OnPropertyChanged(nameof(MuteHotkeyText));
                OnPropertyChanged(nameof(PushToTalkHotkeyText));
                break;
        }
    }

    private void UpdateLayoutMetrics(double bufferedMilliseconds, float processingLoad, bool running)
    {
        ProcessingLoadText = running
            ? string.Create(CultureInfo.InvariantCulture, $"Load {processingLoad * 100:0}%")
            : "Load –";
        BufferText = running
            ? string.Create(CultureInfo.InvariantCulture, $"Buffer {bufferedMilliseconds:0} ms")
            : "Buffer –";
    }

    private string HotkeyTextFor(HotkeyAction action) =>
        Hotkeys.FirstOrDefault(hotkey => hotkey.Action == action) is { IsAssigned: true } hotkey
            ? hotkey.DisplayText
            : "Not set";

    private static double ParseHertz(string choice) =>
        double.Parse(choice.Replace(" Hz", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
}
