namespace OpenHisabKitab.Core.Accounts;

/// <summary>Ledgers every company must have. They can be renamed but not deleted.</summary>
public enum RequiredLedger
{
    Cash,
    Capital,
    ProfitAndLoss,
    RoundOff,
    Sales,
    Purchases,
    OutputCgst,
    OutputSgst,
    OutputUtgst,
    OutputIgst,
    OutputCess,
    InputCgst,
    InputSgst,
    InputUtgst,
    InputIgst,
    InputCess,
}
