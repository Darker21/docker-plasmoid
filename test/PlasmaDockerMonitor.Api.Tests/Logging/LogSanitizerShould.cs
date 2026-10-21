using PlasmaDockerMonitor.Api.Logging;

namespace PlasmaDockerMonitor.Api.Tests.Logging;

public class LogSanitizerShould
{
    [Theory]
    [InlineData("my-stack\n[ERR] forged entry", "my-stack[ERR] forged entry")]
    [InlineData("my-stack\r\n[ERR] forged entry", "my-stack[ERR] forged entry")]
    [InlineData("\rmy-stack\r", "my-stack")]
    public void StripLineBreaks_WhenValueContainsThem(string value, string expected)
    {
        Assert.Equal(expected, value.SanitizeForLog());
    }

    [Theory]
    [InlineData("my-stack")]
    [InlineData("StartContainer:abc123 with\ttabs")]
    [InlineData("")]
    public void ReturnValueUnchanged_WhenItHasNoLineBreaks(string value)
    {
        Assert.Equal(value, value.SanitizeForLog());
    }
}
