﻿using Avro.IO;
using Avro.Reflect;
using Avro;
//using com.sforce.eventbus;
using MediatR;
using Avro.Generic;
using com.sforce.eventbus;
using Avro.File;
using Newtonsoft.Json;
using System.Dynamic;
using SalesforceGrpc.Extensions;

namespace SalesforceGrpc.Handlers.Account;
public class AccountChangeEventHandler {
    public class Handler : IRequestHandler<AccountCDCEventCommand> {
        private readonly IMediator _mediator;

        public Handler(IMediator mediator) {
            _mediator = mediator;
        }

        public async Task Handle(AccountCDCEventCommand request, CancellationToken cancellationToken) {
            Console.WriteLine("Handling Account Change event");
            var accSchema = Schema.Parse(File.ReadAllText("./avro/AccountChangeEventGRPCSchema.avsc"));
            /*var nameSchema = Schema.Parse(File.ReadAllText("./avro/Switchable_PersonNameSchema.avsc"));
            var addressSchema = Schema.Parse(File.ReadAllText("./avro/AddressSchema.avsc"));
            var cehSchema = Schema.Parse(File.ReadAllText("./avro/ChangeEventHeaderSchema.avsc"));
            var cache = new ClassCache();
            cache.LoadClassCache(typeof(Switchable_PersonName), nameSchema);
            cache.LoadClassCache(typeof(Address), addressSchema);
            cache.LoadClassCache(typeof(ChangeEventHeader), cehSchema);*/
            using var accStream = new MemoryStream(request.AvroPayload);
            var decoder = new BinaryDecoder(accStream);
            var datumReader = new GenericDatumReader<GenericRecord>(accSchema, accSchema);
            var gr = datumReader.Read(null, decoder);
            var changeEventHeaderValid = gr.GetTypedValue<GenericRecord>("ChangeEventHeader", out var genericChangeEventHeader);
            var changeTypeFound = genericChangeEventHeader.GetTypedValue<dynamic>("changeType", out var changeType);
            Console.WriteLine(genericChangeEventHeader.GetValue(0).ToString() + " HAS BEEN " + changeType.Value);
            Console.WriteLine("Is the value 2 a generic record? " + gr.GetValue(2) is GenericRecord);
            System.Console.WriteLine("value 2 " + gr.GetValue(2)?.ToString());

            /*var changeType = accEvent.ChangeEventHeader.changeType;
            if (changeType is ChangeType.CREATE) {
                await _mediator.Send(new AccountCreateCommand { Name = "Creating Account", ChangeEvent = accEvent }, cancellationToken);
            } else if (changeType is ChangeType.UPDATE) {
                await _mediator.Send(new AccountUpdateCommand { Name = "Updating Account", ChangeEvent = accEvent }, cancellationToken);
            } else if (changeType is ChangeType.DELETE) {
                await _mediator.Send(new AccountDeleteCommand { Name = "Deleting Account", ChangeEvent = accEvent }, cancellationToken);
            } else if (changeType is ChangeType.UNDELETE) {
                await _mediator.Send(new AccountUndeleteCommand { Name = "Undeleting Account", ChangeEvent = accEvent }, cancellationToken);
            }*/
        }
    }
}
