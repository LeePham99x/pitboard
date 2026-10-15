namespace Pitboard.Windows;

// A row the model still lists stays the same object, so its selection, focus and timers survive a snapshot.
public static class Keyed
{
    public static void Sync<TItem, TShown>(
        IList<TShown> shown,
        IReadOnlyList<TItem> items,
        Func<TItem, string> keyOf,
        Func<TShown, string> shownKey,
        Func<TItem, TShown> make,
        Action<TShown, TItem> update)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var key = keyOf(item);
            var found = -1;
            for (var at = index; at < shown.Count; at++)
            {
                if (string.Equals(shownKey(shown[at]), key, StringComparison.Ordinal))
                {
                    found = at;
                    break;
                }
            }

            if (found < 0)
            {
                shown.Insert(index, make(item));
                continue;
            }

            if (found != index)
            {
                var moved = shown[found];
                shown.RemoveAt(found);
                shown.Insert(index, moved);
            }

            update(shown[index], item);
        }

        while (shown.Count > items.Count)
        {
            shown.RemoveAt(shown.Count - 1);
        }
    }
}
