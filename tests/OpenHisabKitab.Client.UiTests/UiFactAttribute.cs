namespace OpenHisabKitab.Client.UiTests;

/// <summary>A UI smoke test: it needs a desktop, so it runs only when OHK_UI_TESTS is 1.</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OHK_UI_TESTS") != "1") Skip = "Set OHK_UI_TESTS=1 to run UI tests.";
    }
}
