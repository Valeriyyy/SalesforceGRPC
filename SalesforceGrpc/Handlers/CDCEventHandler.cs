using Avro.Generic;
using Avro.IO;
using com.sforce.eventbus;
using MediatR;
using SalesforceGrpc.Extensions;
using System.Collections;

namespace SalesforceGrpc.Handlers;
public class CDCEventHandler {
    public class Handler : IRequestHandler<GenericCDCEventCommand> {
        private readonly IMediator _mediator;

        public Handler(IMediator mediator) {
            _mediator = mediator;
        }

        public async Task Handle(GenericCDCEventCommand request, CancellationToken cancellationToken) {
            using var accStream = new MemoryStream(request.AvroPayload);
            var decoder = new BinaryDecoder(accStream);
            var datumReader = new GenericDatumReader<GenericRecord>(request.AvroSchema, request.AvroSchema);
            var gr = datumReader.Read(null, decoder);
            var changeEventHeaderValid = gr.GetTypedValue<GenericRecord>("ChangeEventHeader", out var genericChangeEventHeader);
            var changeTypeFound = genericChangeEventHeader.GetTypedValue<dynamic>("changeType", out var changeType);
            var entityName = genericChangeEventHeader.GetValue(0).ToString();
            Console.WriteLine(entityName + " HAS BEEN " + changeType.Value);

            Enum.TryParse<ChangeType>(changeType.Value, out ChangeType changeTypeEnum);
            if (changeTypeEnum is ChangeType.CREATE) {
                await _mediator.Send(new CreateCommand { ChangeEvent = gr, EntityName = entityName }, cancellationToken);
            } else if (changeTypeEnum is ChangeType.UPDATE) {
                await _mediator.Send(new UpdateCommand { ChangeEvent = gr, EntityName = entityName }, cancellationToken);
            } else if (changeTypeEnum is ChangeType.DELETE) {
                var changedFields = genericChangeEventHeader.GetValue(11) as IList;
                await _mediator.Send(new DeleteCommand { RecordIds = changedFields, EntityName = entityName }, cancellationToken);
            } else if (changeTypeEnum is ChangeType.UNDELETE) {
                var changedFields = genericChangeEventHeader.GetValue(11) as IList;
                await _mediator.Send(new UndeleteCommand { RecordIds = changedFields, EntityName = entityName }, cancellationToken);
            }
        }
    }
}
