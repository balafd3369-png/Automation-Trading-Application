using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.TestSupport;

internal static class InstrumentLotTestSetup
{
    private static bool _configured;

    [ModuleInitializer]
    internal static void ModuleInit() => EnsureDefaultIndexLots();

    public static void EnsureDefaultIndexLots()
    {
        if (_configured)
        {
            return;
        }

        InstrumentLotMetadata.Configure(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["InstrumentLots:Index:NIFTY"] = "75",
                    ["InstrumentLots:Index:BANKNIFTY"] = "30",
                    ["InstrumentLots:Index:FINNIFTY"] = "60",
                    ["InstrumentLots:Index:SENSEX"] = "20",
                })
                .Build());
        _configured = true;
    }
}