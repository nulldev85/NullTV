namespace NullTV.Core;

public static class GuideGeometry
{
    public const int WindowMinutes=360;
    public const double PixelsPerMinute=4;
    public static double Position(DateTimeOffset time,DateTimeOffset window)=>(time-window).TotalMinutes*PixelsPerMinute;
    public static double ElapsedWidth(DateTimeOffset now,DateTimeOffset window,double trackLeft,double trackWidth)=>Math.Clamp(Position(now,window)-trackLeft,0,trackWidth);
    public static (double Left,double Width)? Segment(DateTimeOffset start,DateTimeOffset end,DateTimeOffset window,int minutes=WindowMinutes)
    {
        if(end<=start || minutes<=0) return null;
        var left=Math.Clamp((start-window).TotalMinutes,0,minutes);
        var right=Math.Clamp((end-window).TotalMinutes,0,minutes);
        return right<=left ? null : (left*PixelsPerMinute,(right-left)*PixelsPerMinute);
    }
}
