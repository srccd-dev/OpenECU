using AwesomeAssertions;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class ConsoleViewModelTests
{
    [Fact]
    public void Rx_and_tx_append_lines_with_direction_and_hex()
    {
        var vm = new ConsoleViewModel();
        vm.OnTx(new byte[] { 0x68, 0x6A });
        vm.OnRx(new byte[] { 0x48, 0x6B });

        vm.Lines.Should().HaveCount(2);
        vm.Lines[0].Should().Contain("TX").And.Contain("686A");
        vm.Lines[1].Should().Contain("RX").And.Contain("486B");
    }

    [Fact]
    public void Paused_stops_appending()
    {
        var vm = new ConsoleViewModel { Paused = true };
        vm.OnRx(new byte[] { 0x01 });
        vm.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var vm = new ConsoleViewModel();
        vm.OnRx(new byte[] { 0x01 });
        vm.ClearCommand.Execute(null);
        vm.Lines.Should().BeEmpty();
    }
}
