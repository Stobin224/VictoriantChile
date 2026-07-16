namespace VictoriantChile.Simulation.Core
{
    /// <summary>
    /// Infrastructure contract marker for headless harness validation.
    /// This is not game simulation state or runtime behavior.
    /// </summary>
    public static class HeadlessAssemblyInfo
    {
        public const string ContractName = "VictoriantChile.Simulation.Core.HeadlessHarness";
        public const int ContractVersion = 1;
    }
}
