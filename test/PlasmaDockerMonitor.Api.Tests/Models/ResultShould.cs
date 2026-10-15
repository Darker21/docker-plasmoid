using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Models;

namespace PlasmaDockerMonitor.Api.Tests.Models;

public class ResultShould
{
    [Fact]
    public void ReturnSuccessfulResultWithNoError_WhenOk()
    {
        var result = Result.Ok();

        Assert.True(result.Success);
        Assert.Null(result.ErrorKind);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ReturnUnsuccessfulResultWithErrorDetails_WhenFail()
    {
        var result = Result.Fail(ResultErrorKind.DaemonError, "boom");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.DaemonError, result.ErrorKind);
        Assert.Equal("boom", result.ErrorMessage);
    }
}

public class ResultOfTShould
{
    private sealed record Payload(string Value);

    [Fact]
    public void ReturnSuccessfulResultWithData_WhenOk()
    {
        var payload = new Payload("hello");
        var result = Result<Payload>.Ok(payload);

        Assert.True(result.Success);
        Assert.Same(payload, result.Data);
        Assert.Null(result.ErrorKind);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ReturnUnsuccessfulResultWithErrorDetailsAndNoData_WhenFail()
    {
        var result = Result<Payload>.Fail(ResultErrorKind.NotFound, "missing");

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
        Assert.Equal("missing", result.ErrorMessage);
    }

    [Fact]
    public void BeAssignableToBaseResult_BecauseItInheritsFromResult()
    {
        Result<Payload> result = Result<Payload>.Ok(new Payload("x"));

        Result baseResult = result;

        Assert.True(baseResult.Success);
    }
}
