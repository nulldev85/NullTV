namespace NullTV.Core;

public sealed record UpdateJob(int ParentProcessId,long ParentStartTimeUtcTicks,string InstallDirectory,string DataDirectory,string CurrentVersion);
public sealed record UpdateResult(bool Success,string Version,int? InstallerCode,bool RebootRequired,string Message,DateTimeOffset Time);
