using System.Runtime.InteropServices;

namespace ChromaDotNet.Local;

internal static class Platform
{
	private const int ExecuteOk = 1;

	public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	// Whether the open of a file with FileShare.None failed because another process holds it. Windows: ERROR_SHARING_VIOLATION or
	// ERROR_LOCK_VIOLATION. Linux and macOS: flock fails with EWOULDBLOCK, which .NET puts in HResult as it is, 11 on Linux and 35 on
	// macOS.
	public static bool IsHeldByAnotherProcess(IOException e)
	{
		if (e.GetType() != typeof(IOException))
			return false;
		if (IsWindows)
			return (e.HResult & 0xFFFF) is 32 or 33;
		return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? e.HResult == 35 : e.HResult == 11;
	}

	// Whether this process can execute the file, as the system decides it: for the owner, the group or the others, whichever applies.
	// On Linux NuGet extracts the executables of a package with the mode 744: only the user that restored can run them, and in a
	// container published as root and started as another user, as the Dockerfiles of .NET do, that is not this user.
	public static bool CanExecute(string path) => IsWindows || access(Utf8(path), ExecuteOk) == 0;

	// Adds the execute bit for the owner, the group and the others; false when this user cannot change the mode, as when another
	// user owns the file, and the file still cannot be executed.
	public static bool TryMakeExecutable(string path)
	{
		if (CanExecute(path))
			return true;
#if NET
		if (OperatingSystem.IsWindows())
			return true;
		try
		{
			File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
#else
		// mode_t is 32 bits on Linux and 16 on macOS: in both the value travels in a register, and 0755 fits.
		if (chmod(Utf8(path), 0x1ED) != 0)
			return false;
#endif
		return CanExecute(path);
	}

	// The path in UTF-8, ended by a zero byte, for libc.
	private static byte[] Utf8(string path) => System.Text.Encoding.UTF8.GetBytes(path + "\0");

	[DllImport("libc", SetLastError = true)]
	private static extern int access(byte[] path, int mode);

#if !NET
	[DllImport("libc", SetLastError = true)]
	private static extern int chmod(byte[] path, int mode);
#endif
}
