using System.Runtime.InteropServices;

namespace PaperWalls.Interop;

internal static partial class PInvoke
{
	internal static partial class User32
	{
		[DllImport("user32.dll")]
		internal static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

		internal enum WindowShowStyle
		{
			SW_HIDE = 0,
			SW_SHOWNORMAL = 1,
			SW_SHOW = 5,
			SW_MINIMIZE = 6,
			SW_RESTORE = 9
		}
	}
}
