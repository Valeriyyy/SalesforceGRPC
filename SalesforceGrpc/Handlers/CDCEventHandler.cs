using MediatR;

namespace SalesforceGrpc.Handlers;
public class CDCEventHandler {
    public class Handler : IRequestHandler<EventWithId> {
        public async Task Handle(EventWithId request, CancellationToken cancellationToken) {
            Console.WriteLine("Processing event");
        }
    }
}
