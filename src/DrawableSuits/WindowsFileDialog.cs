using System.Runtime.InteropServices;
using System.Text;

namespace DrawableSuits;

internal static class WindowsFileDialog
{
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnNoChangeDir = 0x00000008;

    public static bool TryOpenImage(out string path)
    {
        path = null;
        var ofn = new OpenFileName();
        ofn.structSize = Marshal.SizeOf(typeof(OpenFileName));
        ofn.file = new StringBuilder(4096);
        ofn.maxFile = ofn.file.Capacity;
        ofn.filter = "Image Files\0*.png;*.jpg;*.jpeg\0PNG Files\0*.png\0JPEG Files\0*.jpg;*.jpeg\0All Files\0*.*\0";
        ofn.title = "Import DrawableSuits Decal";
        ofn.flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir;

        if (!GetOpenFileName(ofn))
        {
            return false;
        }

        path = ofn.file.ToString();
        return true;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class OpenFileName
    {
        public int structSize;
        public System.IntPtr dlgOwner = System.IntPtr.Zero;
        public System.IntPtr instance = System.IntPtr.Zero;
        public string filter;
        public string customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 0;
        public StringBuilder file;
        public int maxFile;
        public string fileTitle = null;
        public int maxFileTitle = 0;
        public string initialDir = null;
        public string title;
        public int flags;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public string defExt = null;
        public System.IntPtr custData = System.IntPtr.Zero;
        public System.IntPtr hook = System.IntPtr.Zero;
        public string templateName = null;
        public System.IntPtr reservedPtr = System.IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }
}
