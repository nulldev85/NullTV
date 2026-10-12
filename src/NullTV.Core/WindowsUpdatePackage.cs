using System.Runtime.InteropServices;
using System.Text;

namespace NullTV.Core;

// Reads MSI metadata without invoking installation or any custom action.
public static class WindowsUpdatePackage
{
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiOpenDatabase(string path,IntPtr mode,out uint database);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiDatabaseOpenView(uint database,string query,out uint view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")] private static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiRecordGetString(uint record,uint field,StringBuilder buffer,ref uint length);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiGetSummaryInformation(uint database,string? path,uint count,out uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiSummaryInfoGetProperty(uint summary,uint property,out uint type,out int value,out System.Runtime.InteropServices.ComTypes.FILETIME time,StringBuilder buffer,ref uint length);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
    private static void Require(uint code) { if(code!=0) throw new InvalidDataException($"Windows could not read the update package ({code})."); }
    public static void Validate(string path,UpdateRelease release)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Require(MsiOpenDatabase(path,IntPtr.Zero,out var db));
        try
        {
            string Query(string query)
            {
                Require(MsiDatabaseOpenView(db,query,out var view));
                try { Require(MsiViewExecute(view,0)); Require(MsiViewFetch(view,out var record)); try { var text=new StringBuilder(512); uint length=512; Require(MsiRecordGetString(record,1,text,ref length)); return text.ToString(); } finally { MsiCloseHandle(record); } }
                finally { MsiCloseHandle(view); }
            }
            string Property(string name)=>Query($"SELECT `Value` FROM `Property` WHERE `Property`='{name}'");
            Require(MsiGetSummaryInformation(db,null,0,out var summary)); string template;
            try { var text=new StringBuilder(512); uint length=512; Require(MsiSummaryInfoGetProperty(summary,7,out _,out _,out _,text,ref length)); template=text.ToString(); }
            finally { MsiCloseHandle(summary); }
            if(Property("ProductName")!="NullTV" || Property("Manufacturer")!="NullTV contributors" || !Guid.TryParse(Property("UpgradeCode"),out var family) || family!=Guid.Parse(UpdateTrust.UpgradeCode) || Property("ProductVersion")!=release.Version || !template.Split(';')[0].Equals("Arm64",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This installer does not match the verified NullTV release. Nothing was installed.");
            int Sequence(string action)=>int.Parse(Query($"SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='{action}'"),System.Globalization.CultureInfo.InvariantCulture);
            if(Sequence("RemoveExistingProducts")<=Sequence("InstallInitialize") || Sequence("RemoveExistingProducts")>=Sequence("InstallFinalize")) throw new InvalidDataException("The update package does not provide transactional upgrade rollback.");
        }
        finally { MsiCloseHandle(db); }
    }
}
