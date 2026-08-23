using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace NexaPlay.Owner;

internal sealed class DesktopShortcutService
{
    private const string ShortcutName = "NexaPlay Creator Studio.lnk";

    public bool TryCreateOrUpdate()
    {
        try { CreateOrUpdate(); return true; }
        catch { return false; }
    }

    public string CreateOrUpdate()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Creator Studio could not determine its executable path.");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new DirectoryNotFoundException("Windows could not find the desktop folder.");

        var executablePath = executable!;
        var shortcutPath = Path.Combine(desktop, ShortcutName);
        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
        var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
        try
        {
            shellLink.SetPath(executablePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory);
            shellLink.SetDescription("Open NexaPlay Creator Studio");
            shellLink.SetIconLocation(executablePath, 0);
            shellLink.SetShowCmd(1);
            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally { Marshal.FinalReleaseComObject(shellLink); }
        return shortcutPath;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
