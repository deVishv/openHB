using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Settings;

namespace OpenHisabKitab.Client.Model.Tests.Formatting;

public sealed class DateTextTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("dd-mm-yyyy", "05-04-2026")]
    [InlineData("dd/mm/yyyy", "05/04/2026")]
    [InlineData("yyyy-mm-dd", "2026-04-05")]
    public void Each_date_format(string format, string text) =>
        Assert.Equal(text, DateText.Format(new DateOnly(2026, 4, 5), format));

    [Fact]
    public void Client_state_round_trips_and_a_broken_file_gives_a_fresh_one()
    {
        var files = new MachineFiles(new OpenHisabKitab.Client.Model.Connection.ClientPaths(folder));
        Assert.Null(files.LoadState().LastHost);
        files.SaveState(new ClientState("pc-1:47250", new Dictionary<string, int> { ["pc-1:47250"] = 3 }, new WindowPlacement(10, 20, 1024, 768, true)));
        var state = files.LoadState();
        Assert.Equal(("pc-1:47250", 3, true), (state.LastHost, state.LastCompanyByHost["pc-1:47250"], state.Window!.Maximised));
        File.WriteAllText(Path.Combine(folder, "state.json"), "{ nope");
        Assert.Empty(files.LoadState().LastCompanyByHost);
    }
}
