using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Saturn.Tools;
using Saturn.Tools.Core;
using Saturn.Tools.Objects;

namespace Saturn.Tests.Tools
{
    public class ExecuteCommandToolTests
    {
        // Approval is bypassed so the tool can run headlessly in CI.
        private static ExecuteCommandTool NewTool() =>
            new ExecuteCommandTool(new CommandExecutorConfig(), new CommandApprovalService(false));

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Emits a line immediately, then blocks for ~20s so timeout/kill paths can be exercised.
        private static string EarlyOutputThenHang =>
            IsWindows ? "echo start && ping -n 20 127.0.0.1" : "echo start && sleep 20";

        [Fact]
        public async Task Timeout_ReturnsPartialOutput_WithoutThrowing()
        {
            var tool = NewTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["command"] = EarlyOutputThenHang,
                ["timeout"] = 2
            });

            result.Success.Should().BeFalse();
            result.FormattedOutput.Should().Contain("TIMED OUT");
            result.FormattedOutput.Should().Contain("start");
        }

        [Fact]
        public async Task Background_StartsPollsAndCompletes()
        {
            var tool = NewTool();
            var getOutput = new GetCommandOutputTool();

            var start = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["command"] = "echo bg_marker",
                ["run_in_background"] = true
            });

            start.Success.Should().BeTrue();
            var data = (Dictionary<string, object>)start.RawData!;
            var commandId = (string)data["command_id"];
            commandId.Should().NotBeNullOrEmpty();

            // Poll until the process exits, accumulating output.
            var collected = "";
            var status = "running";
            for (var i = 0; i < 50 && status == "running"; i++)
            {
                await Task.Delay(100);
                var poll = await getOutput.ExecuteAsync(new Dictionary<string, object> { ["command_id"] = commandId });
                var pollData = (Dictionary<string, object>)poll.RawData!;
                collected += (string)pollData["stdout"];
                status = (string)pollData["status"];
            }

            status.Should().Be("exited");
            collected.Should().Contain("bg_marker");
        }

        [Fact]
        public async Task Kill_StopsRunningBackgroundCommand()
        {
            var tool = NewTool();
            var kill = new KillCommandTool();
            var getOutput = new GetCommandOutputTool();

            var start = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["command"] = EarlyOutputThenHang,
                ["run_in_background"] = true
            });

            start.Success.Should().BeTrue();
            var commandId = (string)((Dictionary<string, object>)start.RawData!)["command_id"];

            var killResult = await kill.ExecuteAsync(new Dictionary<string, object> { ["command_id"] = commandId });

            killResult.Success.Should().BeTrue();
            var killData = (Dictionary<string, object>)killResult.RawData!;
            ((string)killData["status"]).Should().Be("killed");

            // The killed state must survive the race with the process exit handler.
            var poll = await getOutput.ExecuteAsync(new Dictionary<string, object> { ["command_id"] = commandId });
            var pollData = (Dictionary<string, object>)poll.RawData!;
            ((string)pollData["status"]).Should().Be("killed");
        }

        [Fact]
        public async Task GetCommandOutput_UnknownId_ReturnsError()
        {
            var getOutput = new GetCommandOutputTool();

            var result = await getOutput.ExecuteAsync(new Dictionary<string, object> { ["command_id"] = "cmd_does_not_exist" });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("No background command");
        }
    }
}
