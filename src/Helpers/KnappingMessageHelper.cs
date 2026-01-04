using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace precisionknapping
{
    /// <summary>
    /// Centralized player messaging for knapping feedback
    /// </summary>
    public static class KnappingMessageHelper
    {
        private const string Prefix = "[Precision Knapping]";

        public static void NotifyMistake(IPlayer player, int remaining, float durability = -1)
        {
            if (player is IServerPlayer serverPlayer)
            {
                string msg = durability > 0
                    ? $"{Prefix} Mistake! {remaining} remaining ({durability * 100:0}% durability)"
                    : $"{Prefix} Mistake! {remaining} chance(s) remaining.";
                serverPlayer.SendMessage(0, msg, EnumChatType.Notification);
            }
        }

        public static void NotifyMistakes(IPlayer player, int count, int remaining, float durability)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(0,
                    $"{Prefix} {count} mistake(s)! {remaining} remaining ({durability * 100:0}% durability)",
                    EnumChatType.Notification);
            }
        }

        public static void NotifyFinalWarning(IPlayer player)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(0,
                    $"{Prefix} Final warning! One more mistake breaks the stone.",
                    EnumChatType.Notification);
            }
        }

        public static void NotifyStoneDestroyed(IPlayer player, string reason)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(0, $"{Prefix} {reason}", EnumChatType.Notification);
            }
        }

        public static void NotifyCompletionDurability(IPlayer player, int mistakes, float durability)
        {
            if (player is IServerPlayer serverPlayer)
            {
                string message;
                if (durability > 1.0f)
                {
                    // Bonus durability!
                    int bonusPercent = (int)((durability - 1.0f) * 100);
                    message = $"{Prefix} Perfect! +{bonusPercent}% bonus durability";
                }
                else if (durability >= 0.99f)
                {
                    // Vanilla durability
                    message = $"{Prefix} Completed - standard durability";
                }
                else
                {
                    // Penalty
                    message = $"{Prefix} Completed with {mistakes} mistake(s) - {durability * 100:0}% durability";
                }
                serverPlayer.SendMessage(0, message, EnumChatType.Notification);
            }
        }

        public static void NotifyCompletionQuantity(IPlayer player, int mistakes, int got, int max)
        {
            if (player is IServerPlayer serverPlayer && got < max)
            {
                serverPlayer.SendMessage(0,
                    $"{Prefix} Completed with {mistakes} mistake(s) - received {got}/{max}",
                    EnumChatType.Notification);
            }
        }
    }
}
