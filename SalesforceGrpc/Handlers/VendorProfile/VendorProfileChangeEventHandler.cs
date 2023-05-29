using Avro.IO;
using Avro.Reflect;
//using com.sforce.eventbus;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SalesforceGrpc.Handlers.VendorProfile;
public class VendorProfileChangeEventHandler {
    public class Handler : IRequestHandler<VendorProfileCDCEventCommand> {
        private readonly IMediator _mediator;
        private const string _tableName = "salesforce.vendor_profiles";

        public Handler(IMediator mediator) {
            _mediator = mediator;
        }

        public async Task Handle(VendorProfileCDCEventCommand request, CancellationToken cancellationToken) {
            /*var schemaName = "Vendor_Profile__ChangeEventGRPCSchema";
            var vpeSchema = Avro.Schema.Parse(File.ReadAllText($"./avro/{schemaName}.avsc"));
            var vpeReader = new ReflectReader<Vendor_Profile__ChangeEvent>(vpeSchema, vpeSchema);
            using var vpeStream = new MemoryStream(request.AvroPayload);
            vpeStream.Seek(0, SeekOrigin.Begin);
            var vpeDecoder = new BinaryDecoder(vpeStream);
            var vendorProfileEvent = vpeReader.Read(vpeDecoder);

            var changeType = vendorProfileEvent.ChangeEventHeader.changeType;
            if (changeType is ChangeType.CREATE) {
                //await _mediator.Send(new VendorProfileCreateCommand { Name = "Creating Account", ChangeEvent = accEvent }, cancellationToken);
            } else if (changeType is ChangeType.UPDATE) {
                //await _mediator.Send(new VendorProfileUpdateCommand { Name = "Updating Account", ChangeEvent = accEvent }, cancellationToken);
            } else if (changeType is ChangeType.DELETE) {
                await _mediator.Send(new DeleteCommand { RecordIds = vendorProfileEvent.ChangeEventHeader.recordIds.ToList(), Table = _tableName }, cancellationToken);
            } else if (changeType is ChangeType.UNDELETE) {
                await _mediator.Send(new UndeleteCommand { RecordIds = vendorProfileEvent.ChangeEventHeader.recordIds.ToList(), Table = _tableName }, cancellationToken);
            }*/
        }
    }
}
