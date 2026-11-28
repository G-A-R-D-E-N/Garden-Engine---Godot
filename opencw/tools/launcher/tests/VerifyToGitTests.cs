using System.Net;
using LibGit2Sharp;
using Xunit;

namespace OpenCommonwealth.Launcher.Tests;

[CollectionDefinition("GitServiceStatics", DisableParallelization = true)]
public class GitServiceStaticsCollection { }

[Collection("GitServiceStatics")]
public class VerifyToGitTests : IDisposable
{
    sealed class FakeGitHub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string? auth = req.Headers.Authorization?.Parameter;
            string path = req.RequestUri!.AbsolutePath;
            if (auth != "fresh") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            if (path == "/user")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"login\":\"dev\"}") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    public VerifyToGitTests() => GitService.ResetTokenCache();
    public void Dispose() => GitService.ResetTokenCache();

    static UsernamePasswordCredentials Creds(GitService g) =>
        Assert.IsType<UsernamePasswordCredentials>(
            g.Credentials("https://github.com/x/y.git", "", SupportedCredentialTypes.UsernamePassword));

    [Fact]
    public void StaleStoredTokenIsReplacedByTheGhTokenForGitCallbacks()
    {
        var (state, _) = GitService.VerifyOrg(() => "stale", () => "fresh", new FakeGitHub());

        Assert.Equal(GitService.OrgState.Member, state);
        using var t = new TempDir();
        var c = Creds(new GitService(t.Path, "https://github.com/x/y.git"));
        Assert.Equal("fresh", c.Password);
        Assert.Equal("x-access-token", c.Username);
    }
}
