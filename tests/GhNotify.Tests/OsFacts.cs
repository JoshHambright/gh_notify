namespace GhNotify.Tests;

/// <summary>A test that needs Windows (cmd.exe, .cmd scripts, findstr); skipped elsewhere, e.g. on the macOS CI job.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows only"; }
}

/// <inheritdoc cref="WindowsFactAttribute"/>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows only"; }
}

/// <summary>A test that needs a Unix shell (/bin/sh); skipped on Windows.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "Unix only"; }
}
