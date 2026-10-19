using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NullTV.Core;

public sealed record StreamObservation(int Width,int Height,double FrameRate,string Transfer,string UrlHash,DateTimeOffset MeasuredUtc)
{
    public bool IsValid=>Width is >0 and <=32768 && Height is >0 and <=32768 && double.IsFinite(FrameRate) && FrameRate is >=0 and <=1000;
    public bool Matches(MediaItem item,DateTimeOffset now)=>IsValid && UrlHash==ChannelQuality.Fingerprint(item.Url) && MeasuredUtc<=now.AddMinutes(5) && MeasuredUtc>=now.AddDays(-30);
}
public sealed record QualityLabel(string Text,string Kind,string Description);
public static class ChannelQuality
{
    public static string Fingerprint(string url)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
    public static IReadOnlyList<QualityLabel> Labels(MediaItem item,StreamObservation? measured,DateTimeOffset now)
    {
        var result=new List<QualityLabel>();
        if(measured?.Matches(item,now)==true)
        {
            var resolution=measured.Height>=4320 ? "8K" : measured.Height>=2160 ? "UHD" : measured.Height>=1080 ? "FHD" : measured.Height>=720 ? "HD" : "SD";
            var detail=$"Measured {measured.Width} × {measured.Height} · {measured.MeasuredUtc.LocalDateTime:g}";
            result.Add(new(resolution,"resolution",detail));
            if(measured.FrameRate>0) result.Add(new(measured.FrameRate.ToString("0.##",CultureInfo.InvariantCulture)+" FPS","fps",detail));
            if(measured.Transfer is "pq" or "smpte2084" or "hlg" or "arib-std-b67") result.Add(new(measured.Transfer is "hlg" or "arib-std-b67" ? "HLG" : "HDR","hdr",detail));
            return result;
        }
        // A provider's name is a declaration, never a measured stream result.
        foreach(var (pattern,label) in new[]{("8K|4320P","8K"),("4K|UHD|2160P","UHD"),("FHD|1080[PI]?","FHD"),("HD|720P","HD"),("SD|480P|576P","SD")})
            if(Regex.IsMatch(item.Name,@"(?<![\p{L}\p{N}])(?:"+pattern+@")(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)) { result.Add(new(label,"resolution","Provider label · stream resolution has not been measured")); break; }
        var fps=Regex.Match(item.Name,@"(?<![\d.])(\d{2,3}(?:\.\d+)?)\s*(?:FPS|HZ)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(fps.Success && double.TryParse(fps.Groups[1].Value,CultureInfo.InvariantCulture,out var value) && value is >0 and <=240) result.Add(new(value.ToString("0.##",CultureInfo.InvariantCulture)+" FPS","fps","Provider label · frame rate has not been measured"));
        return result;
    }
}
