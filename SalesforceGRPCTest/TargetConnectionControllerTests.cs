using Application.Targets;
using DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SalesforceGrpc.Controllers;
using System.Text.Json;

namespace SalesforceGRPCTest;

/// <summary>
/// What the Target Connection API sends back when the service refuses, in the shapes the page reads.
/// </summary>
public class TargetConnectionControllerTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITargetConnectionService _service = Substitute.For<ITargetConnectionService>();

    private TargetConnectionController NewController() =>
        new(_service, NullLogger<TargetConnectionController>.Instance);

    private static JsonElement BodyOf(IActionResult? result) {
        var objectResult = Assert.IsType<ObjectResult>(result, exactMatch: false);
        return JsonSerializer.SerializeToElement(objectResult.Value);
    }

    /// <summary>
    /// The summary and the driver's own words travel separately, so the page can show the summary and put the
    /// searchable text behind a disclosure.
    /// </summary>
    [Fact]
    public async Task ARepointWhoseNewDatabaseFailsItsProof_IsABadRequest_CarryingTheSummaryAndTheRawText() {
        _service.RepointAsync(Arg.Any<RepointTargetConnectionDTO>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TargetConnectionProofFailedException("Could not be reached. Nothing has been destroyed.", "no route to host"));

        var response = await NewController().Repoint(new RepointTargetConnectionDTO(), Ct);

        var result = Assert.IsType<BadRequestObjectResult>(response.Result);
        var body = BodyOf(result);
        Assert.Equal("Could not be reached. Nothing has been destroyed.", body.GetProperty("error").GetString());
        Assert.Equal("no route to host", body.GetProperty("rawResponse").GetString());
    }

    [Fact]
    public async Task Verify_ProvesTheStoredConnection() {
        _service.VerifyAsync(Arg.Any<CancellationToken>()).Returns(new TargetConnectionDTO { Exists = true, ConnectionState = "Connected" });

        var response = await NewController().Verify(Ct);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal("Connected", Assert.IsType<TargetConnectionDTO>(ok.Value).ConnectionState);
    }
}
