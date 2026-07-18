using WinForge.Infrastructure;

namespace WinForge.Integration.Tests;

public sealed class WindowsReadOnlyIntegrationTests
{
    [Fact(Skip = "Run manually in an isolated VM after setting WINFORGE_RUN_INTEGRATION=1.")]
    public async Task Powercfg_list_can_be_read_without_mutation()
    {
        var result = await new SafeProcessRunner().RunAsync("powercfg.exe", ["/list"], TimeSpan.FromSeconds(10));
        Assert.Equal(0, result.ExitCode);
    }
}
