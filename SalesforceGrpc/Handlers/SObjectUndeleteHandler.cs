//using com.sforce.eventbus;
using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers;
public class SObjectUndeleteHandler {
    public class Handler : IRequestHandler<UndeleteCommand> {
        private readonly QueryFactory _db;

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(UndeleteCommand request, CancellationToken cancellationToken) {
            await Console.Out.WriteLineAsync("Records have been undeleted");
            /*var recordIds = request.RecordIds;
            await _db.Query(request.Table)
                .WhereIn("sf_id", recordIds)
                .UpdateAsync(new { is_deleted = false, deleted_date = (DateTime?)null }, null, null, cancellationToken);*/
        }
    }
}
