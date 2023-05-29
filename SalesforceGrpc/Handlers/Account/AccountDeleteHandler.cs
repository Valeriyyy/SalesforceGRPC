//using com.sforce.eventbus;
using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers.Account;
public class AccountDeleteHandler {
    public class Handler : IRequestHandler<AccountDeleteCommand> {
        private readonly QueryFactory _db;

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(AccountDeleteCommand request, CancellationToken cancellationToken) {
            /*var deleteEvent = request.ChangeEvent as AccountChangeEvent;
            var recordIds = deleteEvent.ChangeEventHeader.recordIds;
            await _db.Query("salesforce.account")
                .WhereIn("sf_id", recordIds)
                .UpdateAsync(new { is_deleted = true, deleted_date = DateTime.Now });*/
        }
    }
}
