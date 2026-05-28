namespace SecureChat.Desktop.Services.Calls;

public sealed class PassiveVideoSource : EncodedVideoSourceBase
{
    private bool _paused;

    public override Task PauseVideo()
    {
        _paused = true;
        return Task.CompletedTask;
    }

    public override Task ResumeVideo()
    {
        _paused = false;
        return Task.CompletedTask;
    }

    public override Task StartVideo() => Task.CompletedTask;

    public override Task CloseVideo() => Task.CompletedTask;

    public override bool IsVideoSourcePaused() => _paused;
}