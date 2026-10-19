using NullTV.Core;

internal static class PlaybackOverlayTests
{
    public static void Register(Action<string,Func<Task>> test,Action<bool,string> check)
    {
        test("Idle cursor fades player controls without leaving the video",()=> {
            var activity=new PlaybackOverlayActivity();
            activity.ObservePointer(TimeSpan.Zero,100,100,true);
            for(int n=1;n<=34;n++) activity.ObservePointer(TimeSpan.FromMilliseconds(n*100),100,100,true);
            check(activity.Opacity(TimeSpan.FromMilliseconds(3100)) is >0 and <1,"Controls must fade after the idle timeout");
            check(activity.Opacity(TimeSpan.FromMilliseconds(3400))==0,"A stationary cursor kept controls visible");
            return Task.CompletedTask;
        });
        test("Pointer movement reveals controls again; tiny jitter does not hold them open",()=> {
            var activity=new PlaybackOverlayActivity(); activity.ObservePointer(TimeSpan.Zero,50,50,true);
            activity.ObservePointer(TimeSpan.FromSeconds(2),51,50,true);
            check(activity.Opacity(TimeSpan.FromSeconds(4))==0,"One-pixel jitter renewed the timeout");
            activity.ObservePointer(TimeSpan.FromSeconds(4),55,50,true);
            check(activity.Opacity(TimeSpan.FromSeconds(4))==1,"Real movement failed to reveal controls");
            return Task.CompletedTask;
        });
        test("Keyboard navigation and dragging retain controls until interaction ends",()=> {
            var activity=new PlaybackOverlayActivity(); activity.Reveal(TimeSpan.Zero);
            check(activity.Opacity(TimeSpan.FromSeconds(20),interacting:true)==1,"An active interaction faded");
            check(activity.Opacity(TimeSpan.FromSeconds(22))==1,"Interaction did not renew the timeout");
            check(activity.Opacity(TimeSpan.FromSeconds(24))==0,"Controls remained after interaction ended");
            return Task.CompletedTask;
        });
        test("Reduced motion and explicit dismissal hide player chrome deterministically",()=> {
            var activity=new PlaybackOverlayActivity(); activity.Reveal(TimeSpan.Zero);
            check(activity.Opacity(TimeSpan.FromMilliseconds(3100),reducedMotion:true)==0,"Reduced motion still animated");
            activity.Reveal(TimeSpan.FromSeconds(4)); activity.Dismiss();
            check(activity.Opacity(TimeSpan.FromSeconds(4))==0,"Dismissal left controls active");
            return Task.CompletedTask;
        });
    }
}
