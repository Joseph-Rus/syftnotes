namespace StudyStash.Core.Tests.E2E;

/// <summary>A test that needs a real Chrome: skipped unless STUDYSTASH_E2E_CHROME names one
/// (engine/tests/extension-e2e.sh downloads Chrome for Testing and sets it).</summary>
public sealed class ChromeFactAttribute : FactAttribute
{
    public ChromeFactAttribute()
    {
        if (ChromeRunner.Binary.Length == 0) Skip = "set STUDYSTASH_E2E_CHROME (engine/tests/extension-e2e.sh)";
    }
}
