namespace StarPush.Tests;

public class FakeConsumerEvent {
    public List<FakeEvent> Events { get; set; }
    public string LatestReplayId { get; set; }
    public string RpcId { get; set; }
    public int PendingNumRequested { get; set; }
}
