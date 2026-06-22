using System.Runtime.InteropServices;
using System.Text;

namespace TanMenu.Core.Infrastructure;

/// <summary>
/// Detects whether the process is running with MSIX package identity.
/// </summary>
public static class PackageRuntime
{
    private const int ErrorSuccess = 0;
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("api-ms-win-appmodel-runtime-l1-1-1.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    private static readonly Lazy<bool> _hasIdentity = new(DetectIdentity);

    public static bool HasPackageIdentity => _hasIdentity.Value;

    private static bool DetectIdentity()
    {
        try
        {
            int length = 0;
            int rc = GetCurrentPackageFullName(ref length, null);
            return rc != AppModelErrorNoPackage;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
