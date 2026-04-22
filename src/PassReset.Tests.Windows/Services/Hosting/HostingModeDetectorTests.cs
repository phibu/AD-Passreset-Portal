using PassReset.Web.Services.Hosting;

namespace PassReset.Tests.Windows.Services.Hosting;

public sealed class HostingModeDetectorTests
{
    [Fact]
    public void Detect_WithIisEnvironmentVariable_ReturnsIis()
    {
        // ASP.NET Core Module sets ASPNETCORE_IIS_HTTPAUTH when hosted under IIS.
        var detector = new HostingModeDetector(
            isWindowsService: () => false,
            getEnv: name => name == "ASPNETCORE_IIS_HTTPAUTH" ? "windows;" : null);

        Assert.Equal(HostingMode.Iis, detector.Detect());
    }

    [Fact]
    public void Detect_AsWindowsService_ReturnsService()
    {
        var detector = new HostingModeDetector(
            isWindowsService: () => true,
            getEnv: _ => null);

        Assert.Equal(HostingMode.Service, detector.Detect());
    }

    [Fact]
    public void Detect_Default_ReturnsConsole()
    {
        var detector = new HostingModeDetector(
            isWindowsService: () => false,
            getEnv: _ => null);

        Assert.Equal(HostingMode.Console, detector.Detect());
    }

    [Fact]
    public void Detect_IisEnvVarPresentButEmpty_ReturnsConsole()
    {
        // Defensive: a blank string should not count as IIS.
        var detector = new HostingModeDetector(
            isWindowsService: () => false,
            getEnv: name => name == "ASPNETCORE_IIS_HTTPAUTH" ? "" : null);

        Assert.Equal(HostingMode.Console, detector.Detect());
    }
}
