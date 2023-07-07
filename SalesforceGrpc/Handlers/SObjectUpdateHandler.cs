using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers;
public class SObjectUpdateHandler {
    public class Handler : IRequestHandler<UpdateCommand> {
        private readonly QueryFactory _db;
        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(UpdateCommand request, CancellationToken cancellationToken) {
            await Console.Out.WriteLineAsync("Records have been updated");
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }
    }
}
