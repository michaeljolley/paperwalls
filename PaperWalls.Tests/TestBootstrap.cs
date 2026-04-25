using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PaperWalls.Tests;

internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // For WindowsAppSDK 2.0+ preview, we need to initialize the DDLM bootstrap
        // This allows non-MSIX packaged apps to use WinRT types
        try
        {
            // Initialize using the versioned API
            var result = NativeMethods.MddBootstrapInitialize2(
                majorMinorVersion: 0x00020000,  // 2.0
                versionTag: null,
                minVersion: new PackageVersion());
            
            if (result != 0)
            {
                Console.WriteLine($"Warning: Bootstrap.Initialize2 returned {result:X}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to initialize Windows App SDK bootstrap: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PackageVersion
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;
    }

    private static class NativeMethods
    {
        [DllImport("Microsoft.WindowsAppRuntime.Bootstrap.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MddBootstrapInitialize2(
            uint majorMinorVersion,
            [MarshalAs(UnmanagedType.LPWStr)] string? versionTag,
            PackageVersion minVersion);
    }
}
