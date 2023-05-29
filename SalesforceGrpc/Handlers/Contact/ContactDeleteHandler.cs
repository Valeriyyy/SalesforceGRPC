//using com.sforce.eventbus;
using MediatR;
using SqlKata.Execution;

namespace SalesforceGrpc.Handlers.Contact;
public class ContactDeleteHandler {
    public class Handler : IRequestHandler<ContactDeleteCommand> {
        private readonly QueryFactory _db;

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(ContactDeleteCommand request, CancellationToken cancellationToken) {
            /*var deleteEvent = request.ChangeEvent as ContactChangeEvent;
            var recordIds = deleteEvent.ChangeEventHeader.recordIds;
            await _db.Query("salesforce.contacts")
                .WhereIn("sf_id", recordIds)
                .UpdateAsync(new { is_deleted = true, deleted_date = DateTime.Now });*/
        }
    }
}
