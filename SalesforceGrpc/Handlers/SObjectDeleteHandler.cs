//using com.sforce.eventbus;
using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers;
public class SObjectDeleteHandler {
    public class Handler : IRequestHandler<DeleteCommand> {
        private readonly QueryFactory _db;

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(DeleteCommand request, CancellationToken cancellationToken) {
            /*var recordIds = request.RecordIds;
            await _db.Query(request.Table)
                .WhereIn("sf_id", recordIds)
                .UpdateAsync(new { is_deleted = true, deleted_date = DateTime.Now });*/
        }
    }
}
