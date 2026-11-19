using DoodleNook.Engine.Documents;

namespace DoodleNook.Engine.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Engine_depends_only_on_the_dotnet_base_library()
    {
        var refs = typeof(Document).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        var foreign = refs.Where(n => !(n.StartsWith("System") || n == "netstandard" || n == "mscorlib" || n.StartsWith("Microsoft.Win32.Primitives"))).ToArray();
        Assert.True(foreign.Length == 0, "Engine must not reference UI or rendering libraries, found: " + string.Join(", ", foreign));
    }

    [Fact]
    public void Engine_types_do_not_mention_ui_frameworks()
    {
        var names = typeof(Document).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(f => f.FieldType.FullName ?? ""))
            .ToArray();
        Assert.DoesNotContain(names, n => n.Contains("Avalonia") || n.Contains("SkiaSharp") || n.Contains("System.Windows"));
    }
}
