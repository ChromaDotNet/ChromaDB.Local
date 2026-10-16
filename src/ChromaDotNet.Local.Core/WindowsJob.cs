using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChromaDotNet.Local;

// One job object for the process, with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: Windows closes its handle when this process ends, in any
// way, and then ends the processes in it. The handle is never closed before.
internal static class WindowsJob
{
	private const int ExtendedLimitInformationClass = 9;
	private const uint JobObjectLimitKillOnJobClose = 0x2000;

	private static readonly Lazy<(IntPtr Handle, int Error)> Job = new(Create);

	public static bool TryAssign(Process process, out int error)
	{
		var job = Job.Value;
		if (job.Handle == IntPtr.Zero)
		{
			error = job.Error;
			return false;
		}
		if (AssignProcessToJobObject(job.Handle, process.Handle))
		{
			error = 0;
			return true;
		}
		error = Marshal.GetLastWin32Error();
		return false;
	}

	private static (IntPtr, int) Create()
	{
		var handle = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
		if (handle == IntPtr.Zero)
			return (IntPtr.Zero, Marshal.GetLastWin32Error());
		var info = new JobObjectExtendedLimitInformation();
		info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
		if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, ref info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
		{
			var error = Marshal.GetLastWin32Error();
			CloseHandle(handle);
			return (IntPtr.Zero, error);
		}
		return (handle, 0);
	}

	[DllImport("kernel32", SetLastError = true)]
	private static extern IntPtr CreateJobObjectW(IntPtr attributes, IntPtr name);

	[DllImport("kernel32", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

	[DllImport("kernel32", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

	[DllImport("kernel32", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}
}
