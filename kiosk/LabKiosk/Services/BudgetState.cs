namespace LabKiosk.Services;

/// <summary>Per-participant AI budgets (plan section 7).</summary>
public sealed class BudgetState
{
    public bool AgyExhausted { get; private set; }
    public int OpenCodePrompts { get; private set; }
    public int OpenCodeCap { get; set; } = 30;
    public bool OpenCodeCapReached => OpenCodePrompts >= OpenCodeCap;
    public bool BothSpent => AgyExhausted && OpenCodeCapReached;

    public void MarkAgyExhausted() => AgyExhausted = true;
    public void SetOpenCodePrompts(int total) => OpenCodePrompts = total;
}
