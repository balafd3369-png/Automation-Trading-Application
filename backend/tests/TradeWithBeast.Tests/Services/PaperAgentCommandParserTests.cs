using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class PaperAgentCommandParserTests
{
    [Fact]
    public void Parse_reads_indian_capital_target_and_loss_amounts()
    {
        var command = PaperAgentCommandParser.Parse(
            "I have 1 lakh capital, use 50 thousand, target 5k, max loss 1500");

        Assert.Equal(50_000m, command.AllocatedCapital);
        Assert.Equal(5_000m, command.ProfitTarget);
        Assert.Equal(1_500m, command.MaxDailyLoss);
        Assert.False(command.StopRequested);
    }

    [Fact]
    public void Parse_uses_stated_capital_when_no_deployment_amount_exists()
    {
        var command = PaperAgentCommandParser.Parse(
            "I have 40000 capital. profit 3000 and daily loss 1000");

        Assert.Equal(40_000m, command.AllocatedCapital);
        Assert.Equal(3_000m, command.ProfitTarget);
        Assert.Equal(1_000m, command.MaxDailyLoss);
    }

    [Theory]
    [InlineData("stop trading")]
    [InlineData("pause the agent")]
    [InlineData("halt")]
    public void Parse_detects_stop_commands(string text)
    {
        Assert.True(PaperAgentCommandParser.Parse(text).StopRequested);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("confirm")]
    [InlineData("go ahead")]
    [InlineData("proceed")]
    public void Parse_detects_explicit_confirmation(string text)
    {
        Assert.True(PaperAgentCommandParser.Parse(text).ConfirmRequested);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("do not proceed")]
    public void Parse_detects_explicit_rejection(string text)
    {
        Assert.True(PaperAgentCommandParser.Parse(text).RejectRequested);
    }

    [Fact]
    public void Parse_does_not_treat_stop_loss_as_stop_agent()
    {
        var command = PaperAgentCommandParser.Parse("use 1 lakh, target 5000, stop loss 1500");

        Assert.False(command.StopRequested);
    }

    [Theory]
    [InlineData("hi", PaperAgentIntent.Greeting)]
    [InlineData("help", PaperAgentIntent.Help)]
    [InlineData("status", PaperAgentIntent.Status)]
    [InlineData("what do you think?", PaperAgentIntent.Unknown)]
    public void Parse_does_not_start_for_conversation(
        string text,
        PaperAgentIntent expected)
    {
        Assert.Equal(expected, PaperAgentCommandParser.Parse(text).Intent);
    }

    [Fact]
    public void Parse_requires_explicit_configuration_or_start_intent()
    {
        Assert.Equal(
            PaperAgentIntent.ConfigureAndStart,
            PaperAgentCommandParser.Parse("start paper trading").Intent);
        Assert.Equal(
            PaperAgentIntent.ConfigureAndStart,
            PaperAgentCommandParser.Parse("use 1 lakh, target 1000, max loss 500").Intent);
    }
}
