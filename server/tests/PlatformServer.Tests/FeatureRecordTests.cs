using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using PlatformServer.Features.Notes;
using Xunit;

namespace PlatformServer.Tests;

/// <summary>
/// The records the features send and read are read-only (server/CLAUDE.md, "Architecture"): init properties and
/// read-only collections, so a record a service caches and hands every request can't be changed for one of them.
/// </summary>
public sealed class FeatureRecordTests
{
    [Theory]
    [MemberData(nameof(FeatureRecords))]
    public void FeatureRecord_AndEveryTypeInside_CannotBeChanged(Type recordType)
    {
        var problems = new List<string>();

        CheckRecord(recordType, [], problems);

        Assert.Empty(problems);
    }

    // Every record in a feature folder (a record has the compiler's <Clone>$ method), so a record added
    // later is covered without being listed.
    public static TheoryData<Type> FeatureRecords() =>
        new(typeof(Note).Assembly.GetTypes().Where(t =>
            t.Namespace?.StartsWith("PlatformServer.Features.", StringComparison.Ordinal) == true
            && t.GetMethod("<Clone>$") is not null));

    // Walks the record's public properties and every type they hold. A property must have no setter or an
    // init-only one, and a collection must be an IReadOnlyList<T> or IReadOnlyDictionary<TKey, TValue>:
    // List<T>, arrays and dictionaries can be changed in place. Properties added later are checked the same way.
    private static void CheckRecord(Type type, HashSet<Type> seen, List<string> problems)
    {
        if (!seen.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = $"{type.Name}.{property.Name}";

            if (property.SetMethod is { } setter && !IsInitOnly(setter))
            {
                problems.Add($"{name} has a setter; use init.");
            }

            CheckPropertyType(property.PropertyType, name, seen, problems);
        }
    }

    private static void CheckPropertyType(Type type, string name, HashSet<Type> seen, List<string> problems)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            CheckPropertyType(type.GetGenericArguments()[0], name, seen, problems);
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            CheckPropertyType(type.GetGenericArguments()[0], name, seen, problems);
            CheckPropertyType(type.GetGenericArguments()[1], name, seen, problems);
            return;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            problems.Add($"{name} is a {type.Name}; use IReadOnlyList<T>.");
            return;
        }

        CheckRecord(type, seen, problems);
    }

    private static bool IsInitOnly(MethodInfo setter)
        => setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}

