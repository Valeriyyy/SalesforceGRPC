using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers;
public class SObjectCreateHandler {

    public class Handler : IRequestHandler<CreateCommand> {
        private readonly QueryFactory _db;
        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(CreateCommand request, CancellationToken cancellationToken) {
            await Console.Out.WriteLineAsync("Handling record creation");

        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }
    }
}
