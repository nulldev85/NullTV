namespace NullTV.Core;

// Monotonic time keeps an idle cursor from holding the player chrome open.
public sealed class PlaybackOverlayActivity
{
    private TimeSpan until;
    private bool revealed,inside;
    private int x,y;
    public void Reveal(TimeSpan now) { revealed=true; until=now+TimeSpan.FromSeconds(3); }
    public void Dismiss() { revealed=false; inside=false; }
    public bool ObservePointer(TimeSpan now,int nextX,int nextY,bool overVideo)
    {
        var moved=overVideo && (!inside || Math.Abs(nextX-x)>=2 || Math.Abs(nextY-y)>=2);
        inside=overVideo;
        if(moved) { x=nextX; y=nextY; Reveal(now); }
        return moved;
    }
    public double Opacity(TimeSpan now,bool interacting=false,bool reducedMotion=false)
    {
        if(interacting) { Reveal(now); return 1; }
        if(!revealed) return 0;
        if(now<=until) return 1;
        return reducedMotion ? 0 : Math.Clamp(1-(now-until).TotalMilliseconds/240,0,1);
    }
}
