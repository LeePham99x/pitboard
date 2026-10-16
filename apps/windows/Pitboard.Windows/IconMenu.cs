using Pitboard.Core;

namespace Pitboard.Windows;

public enum IconMark
{
    Help,
    Switch,
    Update,
    Info,
    Warning,
    Error,
}

public abstract record IconCommand
{
    private IconCommand()
    {
    }

    public sealed record Send(Intent Intent) : IconCommand;

    public sealed record OpenLink(string Url) : IconCommand;

    public sealed record ShowSettings : IconCommand;

    public sealed record CheckForUpdates : IconCommand;

    public sealed record Quit : IconCommand;
}

public abstract record IconMenuEntry
{
    private IconMenuEntry()
    {
    }

    // Detail is the line macOS shows under an item, here its tooltip and its help text.
    public sealed record Command(string Text, IconCommand? Does) : IconMenuEntry
    {
        public string? Detail { get; init; }

        public bool Enabled { get; init; } = true;

        public IconMark? Mark { get; init; }

        // What a click on the icon does, which a Windows context menu shows in bold.
        public bool Default { get; init; }
    }

    // Choosing it does what its row's button does; the one in use stays in use until another is chosen.
    public sealed record Account(string Text, string Group, bool InUse, IconCommand? Does, string? Detail) : IconMenuEntry
    {
        public bool Enabled => InUse || Does is not null;
    }

    public sealed record Label(string Text, bool Heading) : IconMenuEntry;

    public sealed record Separator : IconMenuEntry;
}

// The notification-area icon's menu, in the macOS menu's order. The site menus come with the account windows.
public static class IconMenu
{
    public const string HelpLink = "https://docs.usepitboard.com";

    public static IReadOnlyList<IconMenuEntry> Entries(AppModel model, IUpdates updates)
    {
        List<IconMenuEntry>[] groups =
        [
            Attention(model.MenuNotices, updates),
            .. Accounts(model),
            [
                new IconMenuEntry.Command("Add account…", new IconCommand.Send(new Intent.PresentSheet(new Sheet.Add(Provider: null)))),
                new IconMenuEntry.Command("Refresh", new IconCommand.Send(new Intent.Refresh(Asked: true)))
                {
                    Detail = model.UpdatedMenu,
                    Enabled = !model.Reading,
                },
            ],
            [
                new IconMenuEntry.Command("Open Pitboard", new IconCommand.Send(new Intent.ShowWindow(Pane: null))) { Default = true },
                new IconMenuEntry.Command("Settings", new IconCommand.ShowSettings()),
                .. updates.Available ? [new IconMenuEntry.Command("Check for updates", new IconCommand.CheckForUpdates())] : Array.Empty<IconMenuEntry>(),
            ],
            [
                new IconMenuEntry.Command("Pitboard help", new IconCommand.OpenLink(HelpLink)),
                new IconMenuEntry.Command("Exit", new IconCommand.Quit()),
            ],
        ];

        var entries = new List<IconMenuEntry>();
        foreach (var group in groups.Where(group => group.Count > 0))
        {
            if (entries.Count > 0)
            {
                entries.Add(new IconMenuEntry.Separator());
            }

            entries.AddRange(group);
        }

        return entries;
    }

    private static List<IconMenuEntry> Attention(MenuNotices notices, IUpdates updates)
    {
        var entries = new List<IconMenuEntry>();
        if (notices.Install is { } install)
        {
            entries.Add(Notice(install, IconMark.Help));
        }

        entries.AddRange(notices.Switches.Select(advice => Notice(advice, Mark(advice.Severity) ?? IconMark.Switch)));
        if (notices.Others is { } others)
        {
            entries.Add(Notice(others, Mark(others.Severity) ?? IconMark.Warning));
        }

        if (updates.Available && updates.Waiting is { } waiting)
        {
            entries.Add(new IconMenuEntry.Command($"Install Pitboard {waiting}…", new IconCommand.CheckForUpdates())
            {
                Mark = IconMark.Update,
            });
        }

        return entries;
    }

    private static IEnumerable<List<IconMenuEntry>> Accounts(AppModel model)
    {
        if (model.MenuAccountsNote is { } note)
        {
            return [[new IconMenuEntry.Label(note, Heading: false)]];
        }

        return model.Sections.Select(section =>
        {
            var entries = new List<IconMenuEntry>();
            if (section.Heading is { } heading)
            {
                entries.Add(new IconMenuEntry.Label(heading, Heading: true));
            }

            entries.AddRange(section.Accounts.Select(item => new IconMenuEntry.Account(
                item.MenuTitle,
                section.Id,
                item.InUse,
                item.Action is { } action ? new IconCommand.Send(action.Intent) : null,
                Lines(item.Summary, item.Help))));
            return entries;
        });
    }

    private static IconMenuEntry.Command Notice(MenuEntry entry, IconMark mark) =>
        new(
            entry.Title,
            entry.Intent is { } intent ? new IconCommand.Send(intent)
            : entry.Link is { } link ? new IconCommand.OpenLink(link)
            : null)
        {
            Detail = Lines(entry.Subtitle, entry.Help),
            Enabled = entry.Enabled,
            Mark = mark,
        };

    private static IconMark? Mark(Severity? severity) => severity switch
    {
        Severity.Info => IconMark.Info,
        Severity.Warning => IconMark.Warning,
        Severity.Error => IconMark.Error,
        _ => null,
    };

    private static string? Lines(params string?[] lines)
    {
        var said = lines.Where(line => !string.IsNullOrEmpty(line)).ToArray();
        return said.Length > 0 ? string.Join("\n", said) : null;
    }
}
