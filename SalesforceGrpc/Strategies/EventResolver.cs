using com.sforce.eventbus;

namespace SalesforceGrpc.Strategies;

public class EventResolver {
    private readonly IEnumerable<IEventStrategy> _strategies;

    public EventResolver(IEnumerable<IEventStrategy> strategies) {
        _strategies = strategies;
    }
    
    public IEventStrategy Resolve(ChangeType eventType) {
        var strategy = _strategies.FirstOrDefault(s => s.ChangeType == eventType);
        if (strategy == null) {
            throw new ArgumentException($"Unsupported event type: {eventType}");
        }
        
        return strategy;
    }
}