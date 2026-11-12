using MDSAPI.Internal.Interop;

namespace MDSAPI.Money
{
    /// <summary>
    /// The player's money.
    /// <para>
    /// Cash is what the player carries; the online balance is laundered money in the bank.
    /// They are separate numbers in the game, and a mod usually means the first.
    /// </para>
    /// <para>
    /// <c>MoneyManager</c> is a FishNet networked singleton, so in co-op the host is
    /// authoritative: changing a balance on a client will not stick. Treat the setters as
    /// safe in singleplayer and host-only otherwise.
    /// </para>
    /// </summary>
    public static class Money
    {
        private const string Manager = "Money.MoneyManager";

        // Current name first, then names this member has plausibly had or could take.
        // ROBUSTNESS.md explains why every one of these is a list rather than a string.
        private static readonly MemberSpec _cash =
            new MemberSpec(Manager, "cashBalance", "CashBalance", "_cashBalance");

        private static readonly MemberSpec _online =
            new MemberSpec(Manager, "onlineBalance", "OnlineBalance", "_onlineBalance");

        private static readonly MemberSpec _lifetime =
            new MemberSpec(Manager, "lifetimeEarnings", "LifetimeEarnings", "_lifetimeEarnings");

        private static readonly MemberSpec _changeCash =
            new MemberSpec(Manager, "ChangeCashBalance", "AdjustCashBalance", "AddCash");

        private static readonly MemberSpec _netWorth =
            new MemberSpec(Manager, "GetNetWorth", "CalculateNetWorth");

        private static readonly MemberSpec _format =
            new MemberSpec(Manager, "FormatAmount", "FormatMoney");

        // Only the members the module is useless without. Formatting and net worth are
        // conveniences; losing them should not mark the whole module unavailable.
        static Money() => Modules.Register("Money", _cash, _changeCash);

        /// <summary>True when this module found what it needs. False after a breaking update.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Money");

        /// <summary>Cash in hand. Zero if unavailable.</summary>
        public static float Cash => _cash.Get(0f);

        /// <summary>Laundered money in the bank. Zero if unavailable.</summary>
        public static float OnlineBalance => _online.Get(0f);

        /// <summary>Everything earned across this save. Zero if unavailable.</summary>
        public static float LifetimeEarnings => _lifetime.Get(0f);

        /// <summary>Cash, bank and property combined. Zero if unavailable.</summary>
        public static float NetWorth => _netWorth.Call(0f);

        /// <summary>
        /// Adds to the player's cash; a negative amount takes it away.
        /// Returns false if the module is unavailable.
        /// </summary>
        public static bool ChangeCash(float amount) => _changeCash.Invoke(amount);

        /// <summary>
        /// Formats an amount the way the game does, so mod UI reads like the rest of the
        /// game instead of inventing its own currency style. Falls back to a plain string.
        /// </summary>
        public static string FormatAmount(float amount) =>
            _format.Call(null as string, amount) ?? ("$" + amount.ToString("N0"));
    }
}
