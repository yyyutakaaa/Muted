using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

internal sealed class HotkeyBindingViewModel : ObservableObject
{
    private readonly Action _changed;
    private int _virtualKey;
    private HotkeyModifiers _modifiers;
    private bool _enabled;

    public HotkeyBindingViewModel(HotkeyBinding binding, Action changed)
    {
        _changed = changed;
        Action = binding.Action;
        _virtualKey = binding.VirtualKey;
        _modifiers = binding.Modifiers;
        _enabled = binding.Enabled;
    }

    public HotkeyAction Action { get; }

    public string Title => HotkeyBinding.DescribeAction(Action);

    public string Hint => HotkeyBinding.DescribeHint(Action);

    public int VirtualKey
    {
        get => _virtualKey;
        set
        {
            if (!SetProperty(ref _virtualKey, value))
            {
                return;
            }

            // Assigning a key should just work; clearing one should switch it off.
            _enabled = value > 0;
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(IsAssigned));
            _changed();
        }
    }

    public HotkeyModifiers Modifiers
    {
        get => _modifiers;
        set
        {
            if (SetProperty(ref _modifiers, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                _changed();
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                _changed();
            }
        }
    }

    public bool IsAssigned => _virtualKey > 0;

    public string DisplayText => HotkeyText.Describe(_modifiers, _virtualKey);

    public void Clear()
    {
        VirtualKey = 0;
        Modifiers = HotkeyModifiers.None;
    }

    public HotkeyBinding ToBinding() => new HotkeyBinding
    {
        Action = Action,
        VirtualKey = _virtualKey,
        Modifiers = _modifiers,
        Enabled = _enabled
    }.Normalize();
}
