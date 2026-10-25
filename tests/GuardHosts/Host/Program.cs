using System.Reflection;
using System.Runtime.Loader;

// Usage: GuardHostsHost <plugin.dll> <dataPath>. Loads the plugin in a load context of its own, as hosts of plugins do, starts its
// server, writes "ready <pid of the server>", and waits to be killed.
var plugin = Path.GetFullPath(args[0]);
var entry = new PluginContext(plugin).LoadFromAssemblyPath(plugin).GetType("GuardHostsPlugin.Entry")!;
var pid = (int)entry.GetMethod("Start")!.Invoke(null, [args[1]])!;
Console.WriteLine($"ready {pid}");
await Task.Delay(Timeout.Infinite);

internal sealed class PluginContext(string plugin) : AssemblyLoadContext
{
	private readonly AssemblyDependencyResolver _resolver = new(plugin);

	protected override Assembly? Load(AssemblyName name) => _resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;

	protected override IntPtr LoadUnmanagedDll(string name) =>
		_resolver.ResolveUnmanagedDllToPath(name) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
}
