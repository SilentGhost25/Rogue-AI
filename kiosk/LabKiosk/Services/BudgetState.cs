namespace LabKiosk.Services;

/// <summary>Per-participant AI budgets (plan section 7). Detection is wired up after spikes S2 and S3.</summary>
public sealed class BudgetState
{
    public bool AgyExhausted { get; private set; }
    public int OpenCodePrompts { get; private set; }
    public int OpenCodeCap { get; set; } = 40; // placeholder: set from the 11 Nov pilot
    public bool OpenCodeCapReached => OpenCodePrompts >= OpenCodeCap;
    public bool Eliminated => AgyExhausted && OpenCodeCapReached;

    public void MarkAgyExhausted() => AgyExhausted = true;
    public void CountOpenCodePrompt() => OpenCodePrompts++;
}
