using Avro;
using Avro.IO;
using Avro.Reflect;
//using com.sforce.eventbus;
using MediatR;

namespace SalesforceGrpc.Handlers.Contact;
public class ContactChangeEventHandler {
    public class Handler : IRequestHandler<ContactCDCEventCommand> {
        private readonly IMediator _mediator;
        private const string _tableName = "salesforce.contacts";

        public Handler(IMediator mediator) {
            _mediator = mediator;
        }

        public async Task Handle(ContactCDCEventCommand request, CancellationToken cancellationToken) {
            /*var contactSchema = Schema.Parse(File.ReadAllText("./avro/ContactChangeEventGRPCSchema.avsc"));
            var nameSchema = Schema.Parse(File.ReadAllText("./avro/PersonNameSchema.avsc"));
            var addressSchema = Schema.Parse(File.ReadAllText("./avro/AddressSchema.avsc"));
            var cache = new ClassCache();
            cache.LoadClassCache(typeof(PersonName), nameSchema);
            cache.LoadClassCache(typeof(Address), addressSchema);
            var reader = new ReflectReader<ContactChangeEvent>(contactSchema, contactSchema, cache);
            using var contStream = new MemoryStream(request.AvroPayload);
            contStream.Seek(0, SeekOrigin.Begin);
            var contDecoder = new BinaryDecoder(contStream);
            var contEvent = reader.Read(contDecoder);

            var changeType = contEvent.ChangeEventHeader.changeType;
            if (changeType is ChangeType.CREATE) {
                await _mediator.Send(new ContactCreateCommand { Name = "Creating Contact", ChangeEvent = contEvent }, cancellationToken);
            } else if (changeType is ChangeType.UPDATE) {
                await _mediator.Send(new ContactUpdateCommand { Name = "Updating Contact", ChangeEvent = contEvent }, cancellationToken);
            } else if (changeType is ChangeType.DELETE) {
                //await _mediator.Send(new ContactDeleteCommand { Name = "Deleting Contact", ChangeEvent = contEvent }, cancellationToken);
                await _mediator.Send(new DeleteCommand { RecordIds = contEvent.ChangeEventHeader.recordIds.ToList(), Table = _tableName }, cancellationToken);
            } else if (changeType is ChangeType.UNDELETE) {
                //await _mediator.Send(new ContactUndeleteCommand { Name = "Undeleting Contact", ChangeEvent = contEvent }, cancellationToken);
                await _mediator.Send(new UndeleteCommand { RecordIds = contEvent.ChangeEventHeader.recordIds.ToList(), Table = _tableName }, cancellationToken);
            }*/
        }
    }
}
