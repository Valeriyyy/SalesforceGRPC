//using com.sforce.eventbus;
using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers.Account;
public class AccountUndeleteHandler {
    public class Handler : IRequestHandler<AccountUndeleteCommand> {
        private readonly QueryFactory _db;

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(AccountUndeleteCommand request, CancellationToken cancellationToken) {
            /*var undeleteEvent = request.ChangeEvent as AccountChangeEvent;
            var recordIds = undeleteEvent.ChangeEventHeader.recordIds;
            await _db.Query("salesforce.account")
                .WhereIn("sf_id", recordIds)
                .UpdateAsync(new { is_deleted = false, deleted_date = (DateTime?)null }, null, null, cancellationToken);*/
        }
    }
}
