using System.IO;

namespace Viciotek.Infrastructure.Detection;

public static class DriveHelper
{
    public static IEnumerable<string> GetFixedDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new[] { @"C:\".TrimEnd('\\') };
        }
    }
}
