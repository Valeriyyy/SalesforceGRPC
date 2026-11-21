namespace StarPush.Tests;

/// <summary>
/// Covers reading the org id back out of the token response, which is the application's only source for it.
/// </summary>
public class AuthTokenOrgIdTests {
    [Fact]
    public void TheOrgIdComesFromTheIdentityUrl() {
        var token = new Salesforce.AuthToken {
            Id = "https://login.salesforce.com/id/00D000000000001AAA/005000000000001AAA"
        };

        Assert.Equal("00D000000000001AAA", token.OrgId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://login.salesforce.com/something/else")]
    public void AnIdentityUrlThatIsNotTheExpectedShape_YieldsNoOrgIdRatherThanAGuess(string? id) {
        Assert.Null(new Salesforce.AuthToken { Id = id }.OrgId);
    }
}
