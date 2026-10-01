using Application.Connections;
using DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SalesforceGrpc.Controllers;
using SalesforceGrpc.ViewModels;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers where the user's browser lands after approving in Salesforce: always back on the Org Connection
/// page, with what happened carried there as a one-time notice.
/// </summary>
public class BootstrapCallbackTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IOrgConnectionService _connections = Substitute.For<IOrgConnectionService>();

    private OrgConnectionController NewController() => new(_connections, NullLogger<OrgConnectionController>.Instance) {
        TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>())
    };

    private static OrgConnectionNotice? LandsOnThePage(IActionResult result, OrgConnectionController controller) {
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/org-connection", redirect.Url);
        return OrgConnectionNotice.Take(controller.TempData);
    }

    [Fact]
    public async Task AnApprovalThatConnects_LandsOnThePageWithASuccessNotice() {
        _connections.CompleteBootstrapAsync("code", "state", Arg.Any<CancellationToken>())
            .Returns(new OrgConnectionDTO { Exists = true, ConnectionState = "Connected", OrgUrl = "https://example.my.salesforce.com" });
        var controller = NewController();

        var notice = LandsOnThePage(await controller.BootstrapCallback("code", "state", null, null, Ct), controller);

        Assert.Equal(NoticeKind.Success, notice?.Kind);
        Assert.Contains("https://example.my.salesforce.com", notice!.Message);
    }

    /// <summary>The page shows an approved connection that Salesforce still refuses from the connection itself.</summary>
    [Fact]
    public async Task AnApprovalThatDoesNotWorkYet_LandsOnThePageWithoutANotice() {
        _connections.CompleteBootstrapAsync("code", "state", Arg.Any<CancellationToken>())
            .Returns(new OrgConnectionDTO { Exists = true, ConnectionState = "Failed", IsApproved = true });
        var controller = NewController();

        Assert.Null(LandsOnThePage(await controller.BootstrapCallback("code", "state", null, null, Ct), controller));
    }

    [Fact]
    public async Task ARefusedApproval_LandsOnThePageSayingSalesforceDidNotApprove() {
        var controller = NewController();

        var notice = LandsOnThePage(
            await controller.BootstrapCallback(null, "state", "access_denied", "end-user denied authorization", Ct), controller);

        Assert.Equal(NoticeKind.Error, notice?.Kind);
        Assert.Contains("access_denied", notice!.Message);
        Assert.Contains("end-user denied authorization", notice.Message);
        await _connections.DidNotReceiveWithAnyArgs().CompleteBootstrapAsync(default!, default!, default);
    }

    [Fact]
    public async Task ACallbackThisApplicationNeverStarted_LandsOnThePageWithTheRefusal() {
        _connections.CompleteBootstrapAsync("code", "forged", Arg.Any<CancellationToken>())
            .ThrowsAsync(new System.ComponentModel.DataAnnotations.ValidationException("does not match a Bootstrap this application started"));
        var controller = NewController();

        var notice = LandsOnThePage(await controller.BootstrapCallback("code", "forged", null, null, Ct), controller);

        Assert.Equal(OrgConnectionNotice.Failure("does not match a Bootstrap this application started"), notice);
    }

    [Fact]
    public async Task AFailedCodeExchange_LandsOnThePageWithSalesforcesErrorInBothHalves() {
        var error = Salesforce.Auth.OAuthErrorTranslator.Translate(
            """{"error":"redirect_uri_mismatch","error_description":"redirect_uri must match configuration"}""");
        _connections.CompleteBootstrapAsync("code", "state", Arg.Any<CancellationToken>())
            .ThrowsAsync(new Salesforce.Auth.SalesforceOAuthException(error));
        var controller = NewController();

        var notice = LandsOnThePage(await controller.BootstrapCallback("code", "state", null, null, Ct), controller);

        Assert.Equal(NoticeKind.Error, notice?.Kind);
        Assert.Equal("redirect_uri_mismatch", notice!.Error?.Error);
        Assert.Equal("redirect_uri must match configuration", notice.Error?.ErrorDescription);
        Assert.Equal(error.RawResponse, notice.Error?.RawResponse);
    }

    [Fact]
    public async Task AnApprovalFromAnotherOrg_LandsOnThePageNamingBothOrgs() {
        _connections.CompleteBootstrapAsync("code", "state", Arg.Any<CancellationToken>())
            .ThrowsAsync(new OrgMismatchException("00D000000000001AAA", "00D000000000002BBB"));
        var controller = NewController();

        var notice = LandsOnThePage(await controller.BootstrapCallback("code", "state", null, null, Ct), controller);

        Assert.Equal(new OrgMismatchView("00D000000000001AAA", "00D000000000002BBB"), notice?.OrgMismatch);
    }

    /// <summary>
    /// Connect is a plain link to this endpoint, so it has to redirect. It once answered 200 with the URL as
    /// JSON, because the shared error handling hid the value from the action.
    /// </summary>
    [Fact]
    public async Task StartingTheBootstrapFromALink_RedirectsToSalesforce() {
        _connections.StartBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(new BootstrapStartDTO { AuthorizeUrl = "https://login.salesforce.com/services/oauth2/authorize?state=s" });

        var result = await NewController().RedirectToBootstrap(Ct);

        Assert.Equal("https://login.salesforce.com/services/oauth2/authorize?state=s", Assert.IsType<RedirectResult>(result).Url);
    }

    /// <summary>The browser followed a link here, so a refusal lands on the page rather than as a JSON body.</summary>
    [Fact]
    public async Task ABootstrapThatCannotStart_LandsOnThePageWithTheReason() {
        _connections.StartBootstrapAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new System.ComponentModel.DataAnnotations.ValidationException("The Consumer Secret is needed"));
        var controller = NewController();

        var notice = LandsOnThePage(await controller.RedirectToBootstrap(Ct), controller);

        Assert.Equal(OrgConnectionNotice.Failure("The Consumer Secret is needed"), notice);
    }
}
