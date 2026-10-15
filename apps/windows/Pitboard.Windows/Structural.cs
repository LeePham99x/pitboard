using System.Collections;
using System.Reflection;

namespace Pitboard.Windows;

// A record compares its arrays by reference, so two snapshots read alike are never Equal.
public static class Structural
{
    public static bool Equal(object? first, object? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null || second is null || first.GetType() != second.GetType())
        {
            return false;
        }

        var type = first.GetType();
        if (type.IsPrimitive || type.IsEnum || first is string or decimal)
        {
            return first.Equals(second);
        }

        if (first is IDictionary left && second is IDictionary right)
        {
            return left.Count == right.Count && left.Keys.Cast<object>()
                .All(key => right.Contains(key) && Equal(left[key], right[key]));
        }

        if (first is IEnumerable items && second is IEnumerable others)
        {
            var one = items.GetEnumerator();
            var other = others.GetEnumerator();
            while (true)
            {
                var more = one.MoveNext();
                if (more != other.MoveNext())
                {
                    return false;
                }

                if (!more)
                {
                    return true;
                }

                if (!Equal(one.Current, other.Current))
                {
                    return false;
                }
            }
        }

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .All(property => Equal(property.GetValue(first), property.GetValue(second)));
    }
}
