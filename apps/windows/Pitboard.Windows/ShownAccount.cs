using System.Collections.ObjectModel;
using System.ComponentModel;
using Pitboard.Core;

namespace Pitboard.Windows;

public sealed partial class ShownAccount(AccountItem item) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = item.Id;

    public AccountItem Item { get; private set; } = item;

    public void Show(AccountItem item)
    {
        if (Structural.Equal(Item, item))
        {
            return;
        }

        Item = item;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Item)));
    }

    // UI Automation names a list's item by its text, and Narrator reads the row as one thing.
    public override string ToString() => Item.Spoken;
}

public sealed partial class ShownSection(string key, string? heading) : ObservableCollection<ShownAccount>
{
    public string Key { get; } = key;

    public string? Heading { get; private set; } = heading;

    public void Show(AccountSection section)
    {
        if (Heading != section.Heading)
        {
            Heading = section.Heading;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Heading)));
        }

        Keyed.Sync(
            this, section.Accounts, item => item.Id, shown => shown.Key, item => new ShownAccount(item),
            (shown, item) => shown.Show(item));
    }
}
