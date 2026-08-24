using System.Reflection;
using RepoPulse.Core.Navigation;

namespace RepoPulse.UnitTests;

public class AppRoutesTests
{
    [Fact]
    public void AllRouteAndQueryKeyConstants_AreUnique()
    {
        var values = typeof(AppRoutes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(values);
        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllRouteAndQueryKeyConstants_AreNonEmpty()
    {
        var values = typeof(AppRoutes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.All(values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }
}
